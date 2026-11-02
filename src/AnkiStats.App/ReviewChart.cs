using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AnkiStats.Core;

namespace AnkiStats.App;

public sealed class ReviewChart : FrameworkElement
{
    private List<DailyRow> rows = [];
    public void SetRows(IEnumerable<DailyRow> source, bool weekly)
    {
        rows = source.GroupBy(d => weekly ? d.Day.AddDays(-(((int)d.Day.DayOfWeek + 6) % 7)) : d.Day)
            .Select(g => new DailyRow(g.Key, g.Sum(d => d.Answers))).OrderBy(d => d.Day).ToList();
        if (rows.Count > 0)
        {
            var step = weekly ? 7 : 1;
            var counts = rows.ToDictionary(d => d.Day, d => d.Answers);
            var filled = new List<DailyRow>();
            for (var day = rows[0].Day; day <= rows[^1].Day; day = day.AddDays(step)) filled.Add(new(day, counts.GetValueOrDefault(day)));
            rows = filled;
        }
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        void Label(string text, double x, double y) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.LightSlateGray, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
        var w = Math.Max(1, ActualWidth - 55); var h = Math.Max(1, ActualHeight - 34);
        if (rows.Count == 0) { Label("No review answers in this scope", 8, h / 2); return; }
        // Aggregate long series into adjacent bins sized for the available pixels.
        var size = Math.Max(1, (int)Math.Ceiling(rows.Count / Math.Max(1, w / 5)));
        var bins = rows.Chunk(size).Select(c => c.Sum(d => d.Answers)).ToArray();
        var max = Math.Max(1, bins.Max());
        for (var i = 0; i < 4; i++)
        {
            var y = h - i * h / 3;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(58, 64, 78)), 0.5), new(45, y), new(w + 45, y));
            Label((max * i / 3).ToString("N0"), 0, Math.Max(0, y - 10));
        }
        var brush = new SolidColorBrush(Color.FromRgb(125, 169, 231));
        for (var i = 0; i < bins.Length; i++)
        {
            var bh = h * bins[i] / max;
            dc.DrawRoundedRectangle(brush, null, new Rect(45 + i * w / bins.Length, h - bh, Math.Max(1, w / bins.Length - 1.5), bh), 1, 1);
        }
        Label(rows[0].Day.ToString("MMM yyyy"), 45, h + 10);
        Label(rows[^1].Day.ToString("MMM yyyy"), Math.Max(45, w - 10), h + 10);
        ToolTip = size > 1 ? $"Long series grouped into {size} adjacent periods per bar. Exact daily counts are available in Reviews." : "Exact daily counts are available in Reviews.";
    }
}
