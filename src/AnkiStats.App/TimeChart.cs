using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AnkiStats.Core;

namespace AnkiStats.App;

public sealed class TimeChart : FrameworkElement
{
    private List<TimePeriodRow> rows = [];
    private double? allAverage, recentAverage;
    public void SetRows(List<TimePeriodRow> values, double? all, double? recent)
    {
        rows = values; allAverage = all; recentAverage = recent;
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        void Label(string text, double x, double y) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.LightSlateGray, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
        var width = Math.Max(1, ActualWidth - 70);
        var height = Math.Max(1, ActualHeight - 34);
        if (rows.Count == 0 || rows.Sum(r => (long)r.Stats.TimedAnswers) == 0) { Label("No recorded answer durations in this range", 8, height / 2); return; }
        var size = Math.Max(1, (int)Math.Ceiling(rows.Count / Math.Max(1, width / 5)));
        var bins = rows.Chunk(size).Select(chunk => chunk.Average(row => row.Minutes)).ToArray();
        var maximum = Math.Max(1, Math.Max(bins.Max(), Math.Max(allAverage ?? 0, recentAverage ?? 0)));
        for (var i = 0; i < 4; i++)
        {
            var y = height - i * height / 3;
            dc.DrawLine(new Pen(Brushes.DimGray, 0.4), new Point(52, y), new Point(width + 52, y));
            Label((maximum * i / 3).ToString("N0"), 0, Math.Max(0, y - 10));
        }
        for (var i = 0; i < bins.Length; i++)
        {
            var barHeight = height * bins[i] / maximum;
            dc.DrawRectangle(Brushes.SteelBlue, null, new Rect(52 + i * width / bins.Length, height - barHeight, Math.Max(1, width / bins.Length - 1), barHeight));
        }
        void AverageLine(double? value, Brush color)
        {
            if (value is not {} average) return;
            var y = height - height * average / maximum;
            dc.DrawLine(new Pen(color, 1.5) { DashStyle = DashStyles.Dash }, new Point(52, y), new Point(width + 52, y));
        }
        AverageLine(allAverage, Brushes.LightGray);
        AverageLine(recentAverage, Brushes.Goldenrod);
        Label(rows[0].Start.ToString("MMM yyyy"), 52, height + 10);
        Label(rows[^1].End.ToString("MMM yyyy"), Math.Max(52, width - 20), height + 10);
        ToolTip = size > 1 ? $"Each bar averages up to {size} adjacent periods. Exact totals are listed below." : "One bar per period. Exact totals are listed below.";
    }
}
