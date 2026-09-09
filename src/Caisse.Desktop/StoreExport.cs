using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Caisse.Application;

namespace Caisse.Desktop;

// A portable XLSX snapshot: no Excel installation, macros, or external links are needed.
public static class StoreExport
{
    private sealed record Sheet(string Name, string[] Headers, IEnumerable<object?[]> Rows);
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Package = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static void Write(IStore store, ShopSettings settings, string path)
    {
        var products = store.GetProducts(); var sales = store.GetSales(); var sessions = store.GetSessions(); var refunds = store.GetRefunds();
        var sessionMap = sessions.ToDictionary(s => s.Id);
        var productMap = products.ToDictionary(p => p.Id);
        string Operator(int? id) => id.HasValue && sessionMap.TryGetValue(id.Value, out var s) ? s.Operator : "Non attribué";
        string Local(DateTime date) => date.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var cashiers = CashierStatistics.Build(sessions, sales, refunds, DateTime.MinValue, DateTime.MaxValue);
        Sheet[] sheets = [
            new("Stock restant", ["Code / référence", "Produit", "Catégorie", "Prix de vente", "Quantité restante", "Valeur au prix de vente", "Devise", "À réapprovisionner"],
                products.Select(p => new object?[] { p.Barcode, p.Name, p.Category, p.Price, p.Stock, p.Price * p.Stock, settings.Currency, p.Stock <= 5 ? "Oui" : "Non" })),
            new("Caissiers", ["Caissier", "État actuel", "Sessions", "Tickets", "Articles vendus", "Ventes après remises", "Retours traités", "Solde ventes - retours", "Panier moyen", "Solde espèces", "Solde carte", "Écarts clôturés", "Devise"],
                cashiers.Select(c => new object?[] { c.Name, c.State, c.Sessions, c.Tickets, c.Units, c.Sales, c.Refunds, c.Net, decimal.Round(c.Average, 2), c.Cash, c.Card, c.Difference, settings.Currency })),
            new("Ventes", ["Ticket", "Date locale", "Caissier", "Session", "Paiement", "Total après remise", "Remise", "Reçu", "Monnaie rendue", "Devise"],
                sales.Select(s => new object?[] { s.Number, Local(s.CreatedAt), Operator(s.CashSessionId), s.CashSessionId, s.PaymentMethod, s.Total, s.Discount, s.Tendered, s.Change, settings.Currency })),
            new("Articles vendus", ["Ticket", "Date locale", "Caissier", "Code / référence", "Produit vendu", "Quantité", "Prix unitaire", "Total avant remise globale", "Devise"],
                sales.SelectMany(s => s.Lines.Select(l => new object?[] { s.Number, Local(s.CreatedAt), Operator(s.CashSessionId), l.Barcode, l.ProductName, l.Quantity, l.UnitPrice, l.Total, settings.Currency }))),
            new("Sessions", ["Session", "Caissier", "Ouverture locale", "Clôture locale", "État", "Fond initial", "Attendu à la clôture", "Compté", "Écart", "Devise"],
                sessions.Select(s => new object?[] { s.Id, s.Operator, Local(s.OpenedAt), s.ClosedAt.HasValue ? Local(s.ClosedAt.Value) : "", s.State, s.OpeningAmount, s.ExpectedAtClose, s.CountedAmount, s.Difference, settings.Currency })),
            new("Retours", ["Retour", "Ticket d’origine", "Date locale", "Caissier ayant traité le retour", "Session", "Montant", "Paiement", "Remis en stock", "Motif", "Devise", "Manager autorisant", "Compte demandeur", "Caisse", "Motif prédéfini"],
                refunds.Select(r => new object?[] { r.Id, r.SaleNumber, Local(r.CreatedAt), Operator(r.CashSessionId), r.CashSessionId, r.Amount, r.PaymentMethod, r.Restock ? "Oui" : "Non", r.Reason, settings.Currency, r.ApprovedBy, r.RequestedBy, r.Register, r.ReasonCode })),
            new("Mouvements espèces", ["Date locale", "Caissier", "Session", "Montant signé", "Motif", "Devise"],
                store.GetCashMovements().Select(m => new object?[] { Local(m.CreatedAt), Operator(m.CashSessionId), m.CashSessionId, m.Amount, m.Reason, settings.Currency })),
            new("Journal de stock", ["Date locale", "Produit", "Quantité signée", "Motif / référence"],
                store.GetStockMovements().Select(m => new object?[] { Local(m.CreatedAt), productMap.GetValueOrDefault(m.ProductId)?.Name ?? $"Produit #{m.ProductId}", m.Quantity, m.Reason })),
            new("Articles remboursés", ["Retour", "Ticket", "Produit", "Quantité remboursée", "Montant après remise", "Devise", "Remise en stock"], refunds.SelectMany(r => r.Lines.Select(l => new object?[] { r.Id, r.SaleNumber, l.ProductName, l.Quantity, l.Amount, settings.Currency, r.Restock ? "Oui" : "Non" }))),
            new("Journal opérations", ["Date locale", "Compte", "Shift", "Caisse", "Opération", "Détails"], store.GetAudit().Select(a => new object?[] { Local(a.CreatedAt), a.UserName, a.CashSessionId, a.Register, a.Action, a.Details })),
            new("À lire", ["Information", "Détail"], new object?[][] {
                ["Magasin", settings.Name], ["Exporté le", Local(DateTime.UtcNow)], ["Période", "Tout l’historique enregistré ; stock actuel à la date de l’export."],
                ["Retours", "Attribués au caissier qui les traite. Ils sont déduits à la date du remboursement."],
                ["Caissiers", "Les nouveaux shifts sont liés au compte authentifié. Les anciennes sessions restent identifiées par leur nom historique."],
                ["Stock", "Valeur calculée au prix de vente ; ce montant ne représente ni le coût d’achat ni le bénéfice."],
                ["Restauration", "Ce classeur est destiné à la lecture. Utiliser la copie de restauration pour récupérer les données dans la caisse."] })
        ];
        var temporary = Path.GetFullPath(path) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                XNamespace types = "http://schemas.openxmlformats.org/package/2006/content-types";
                Save(zip, "[Content_Types].xml", new XElement(types + "Types",
                    new XElement(types + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(types + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                    new XElement(types + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                    new XElement(types + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")),
                    sheets.Select((_, i) => new XElement(types + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{i + 1}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
                Save(zip, "_rels/.rels", new XElement(Package + "Relationships", Relationship("rId1", "officeDocument", "xl/workbook.xml")));
                Save(zip, "xl/workbook.xml", new XElement(Main + "workbook", new XAttribute(XNamespace.Xmlns + "r", Rel),
                    new XElement(Main + "sheets", sheets.Select((s, i) => new XElement(Main + "sheet", new XAttribute("name", s.Name), new XAttribute("sheetId", i + 1), new XAttribute(Rel + "id", $"rId{i + 1}"))))));
                Save(zip, "xl/_rels/workbook.xml.rels", new XElement(Package + "Relationships",
                    sheets.Select((_, i) => Relationship($"rId{i + 1}", "worksheet", $"worksheets/sheet{i + 1}.xml")), Relationship("styles", "styles", "styles.xml")));
                Save(zip, "xl/styles.xml", XElement.Parse(Styles));
                for (var i = 0; i < sheets.Length; i++) WriteSheet(zip, sheets[i], i + 1);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static XElement Relationship(string id, string type, string target) => new(Package + "Relationship", new XAttribute("Id", id), new XAttribute("Type", Rel.NamespaceName + "/" + type), new XAttribute("Target", target));
    private static void Save(ZipArchive zip, string path, XElement element)
    {
        using var stream = zip.CreateEntry(path, CompressionLevel.Optimal).Open(); element.Save(stream);
    }
    private static void WriteSheet(ZipArchive zip, Sheet sheet, int index)
    {
        using var stream = zip.CreateEntry($"xl/worksheets/sheet{index}.xml", CompressionLevel.Optimal).Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
        writer.WriteStartDocument(); writer.WriteStartElement("worksheet", Main.NamespaceName);
        writer.WriteStartElement("sheetViews"); writer.WriteStartElement("sheetView"); writer.WriteAttributeString("workbookViewId", "0");
        writer.WriteStartElement("pane"); writer.WriteAttributeString("ySplit", "1"); writer.WriteAttributeString("topLeftCell", "A2"); writer.WriteAttributeString("activePane", "bottomLeft"); writer.WriteAttributeString("state", "frozen"); writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
        writer.WriteStartElement("cols");
        for (var col = 1; col <= sheet.Headers.Length; col++) {
            writer.WriteStartElement("col"); writer.WriteAttributeString("min", col.ToString()); writer.WriteAttributeString("max", col.ToString());
            writer.WriteAttributeString("width", sheet.Name == "À lire" && col == 2 ? "100" : Math.Clamp(sheet.Headers[col - 1].Length + 5, 20, 38).ToString()); writer.WriteAttributeString("customWidth", "1"); writer.WriteEndElement();
        }
        writer.WriteEndElement(); writer.WriteStartElement("sheetData");
        var row = 1;
        void Row(IEnumerable<object?> values, bool header)
        {
            if (row > 1048576) throw new InvalidOperationException("L’export dépasse la capacité d’une feuille Excel.");
            writer.WriteStartElement("row"); writer.WriteAttributeString("r", row.ToString()); writer.WriteAttributeString("ht", header ? "38" : "30"); writer.WriteAttributeString("customHeight", "1");
            var column = 1;
            foreach (var value in values) {
                writer.WriteStartElement("c"); writer.WriteAttributeString("r", Column(column++) + row);
                writer.WriteAttributeString("s", header ? "1" : value is decimal ? (row % 2 == 0 ? "4" : "2") : (row % 2 == 0 ? "3" : "0"));
                if (value is decimal or int or long) { writer.WriteElementString("v", Convert.ToString(value, CultureInfo.InvariantCulture)); }
                else {
                    // Explicit text cells preserve barcode zeros and prevent formula injection.
                    writer.WriteAttributeString("t", "inlineStr"); writer.WriteStartElement("is"); writer.WriteStartElement("t");
                    writer.WriteAttributeString("xml", "space", XNamespace.Xml.NamespaceName, "preserve");
                    var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                    writer.WriteString(new string(text.Where(XmlConvert.IsXmlChar).Take(32767).ToArray())); writer.WriteEndElement(); writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
            writer.WriteEndElement(); row++;
        }
        Row(sheet.Headers, true); foreach (var values in sheet.Rows) Row(values, false);
        writer.WriteEndElement(); writer.WriteStartElement("autoFilter"); writer.WriteAttributeString("ref", $"A1:{Column(sheet.Headers.Length)}{row - 1}"); writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndDocument();
    }
    private static string Column(int number) { var name = ""; while (number > 0) { number--; name = (char)('A' + number % 26) + name; number /= 26; } return name; }
    private const string Styles = """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <fonts count="2"><font><sz val="11"/><name val="Calibri"/><color rgb="FF172B35"/></font><font><b/><sz val="11"/><name val="Calibri"/><color rgb="FFFFFFFF"/></font></fonts>
          <fills count="4"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF103D40"/><bgColor indexed="64"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFF0F6F3"/><bgColor indexed="64"/></patternFill></fill></fills>
          <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="5">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="center"/></xf>
            <xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1" applyAlignment="1"><alignment vertical="center" wrapText="1"/></xf>
            <xf numFmtId="4" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment vertical="center"/></xf>
            <xf numFmtId="0" fontId="0" fillId="3" borderId="0" xfId="0" applyFill="1" applyAlignment="1"><alignment vertical="center"/></xf>
            <xf numFmtId="4" fontId="0" fillId="3" borderId="0" xfId="0" applyNumberFormat="1" applyFill="1" applyAlignment="1"><alignment vertical="center"/></xf>
          </cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
        </styleSheet>
        """;
}
