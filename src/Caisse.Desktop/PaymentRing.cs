using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Caisse.Desktop;
public sealed class PaymentRing : FrameworkElement
{
    public static readonly DependencyProperty CashProperty = DependencyProperty.Register(nameof(Cash), typeof(decimal), typeof(PaymentRing), new FrameworkPropertyMetadata(0m, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CardProperty = DependencyProperty.Register(nameof(Card), typeof(decimal), typeof(PaymentRing), new FrameworkPropertyMetadata(0m, FrameworkPropertyMetadataOptions.AffectsRender));
    public decimal Cash { get => (decimal)GetValue(CashProperty); set => SetValue(CashProperty, value); }
    public decimal Card { get => (decimal)GetValue(CardProperty); set => SetValue(CardProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var center = new Point(ActualWidth / 2, ActualHeight / 2); var radius = Math.Max(10, Math.Min(ActualWidth, ActualHeight) / 2 - 18);
        var teal = new SolidColorBrush(Color.FromRgb(8, 127, 112)); var gold = new SolidColorBrush(Color.FromRgb(241, 176, 65));
        dc.DrawEllipse(null, new Pen(Cash + Card == 0 ? Brushes.LightGray : gold, 24), center, radius, radius);
        var fraction = Cash + Card == 0 ? 0 : (double)(Cash / (Cash + Card));
        if (fraction >= 1) dc.DrawEllipse(null, new Pen(teal, 24), center, radius, radius);
        else if (fraction > 0) {
            var angle = fraction * Math.PI * 2 - Math.PI / 2;
            var geometry = new StreamGeometry(); using (var context = geometry.Open()) { context.BeginFigure(new Point(center.X, center.Y - radius), false, false); context.ArcTo(new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)), new Size(radius, radius), 0, fraction > .5, SweepDirection.Clockwise, true, false); }
            dc.DrawGeometry(null, new Pen(teal, 24), geometry);
        }
        var text = new FormattedText(Cash + Card == 0 ? "Aucune vente" : (fraction * 100).ToString("0", CultureInfo.CurrentCulture) + "%\nespèces", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 18, Brushes.DarkSlateGray, VisualTreeHelper.GetDpi(this).PixelsPerDip) { TextAlignment = TextAlignment.Center };
        dc.DrawText(text, new Point(center.X, center.Y - text.Height / 2));
    }
}
