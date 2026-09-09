namespace Caisse.Domain;

public static class RefundCalculation
{
    public static List<RefundLine> Calculate(Sale sale, IEnumerable<Refund> history, IReadOnlyList<RefundRequest> requests)
    {
        var previous = history.Where(r => r.SaleId == sale.Id).ToList();
        if (previous.Any(r => r.Lines.Count == 0)) throw new InvalidOperationException("Ce ticket a déjà été remboursé intégralement dans l’ancienne version.");
        if (requests.Count == 0 || requests.Any(r => r.Quantity <= 0) || requests.Select(r => r.SaleLineId).Distinct().Count() != requests.Count) throw new InvalidOperationException("Sélectionnez des produits et des quantités positives sans doublons.");
        var subtotal = sale.Lines.Sum(l => l.Total); decimal running = 0, allocated = 0;
        var amounts = new Dictionary<int, decimal>();
        foreach (var line in sale.Lines.OrderBy(l => l.Id)) {
            running += line.Total;
            var cumulative = subtotal == 0 ? 0 : decimal.Round(sale.Total * running / subtotal, 2, MidpointRounding.AwayFromZero);
            amounts[line.Id] = cumulative - allocated; allocated = cumulative;
        }
        var result = new List<RefundLine>();
        foreach (var request in requests) {
            var line = sale.Lines.SingleOrDefault(l => l.Id == request.SaleLineId) ?? throw new InvalidOperationException("Produit absent du ticket original.");
            var already = previous.SelectMany(r => r.Lines).Where(l => l.SaleLineId == line.Id).Sum(l => l.Quantity);
            if (request.Quantity > line.Quantity - already) throw new InvalidOperationException("Quantité supérieure au solde remboursable : " + line.ProductName);
            decimal At(int quantity) => decimal.Round(amounts[line.Id] * quantity / line.Quantity, 2, MidpointRounding.AwayFromZero);
            result.Add(new RefundLine { SaleLineId = line.Id, ProductId = line.ProductId, ProductName = line.ProductName, Quantity = request.Quantity, Amount = At(already + request.Quantity) - At(already) });
        }
        return result;
    }
}
