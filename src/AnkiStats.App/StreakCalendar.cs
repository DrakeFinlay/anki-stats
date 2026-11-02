using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AnkiStats.Core;

namespace AnkiStats.App;

// One drawing surface keeps decades of history lightweight instead of creating a control per day.
public sealed class StreakCalendar : FrameworkElement
{
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);
    private const double Step = 28, Top = 30;
    private static readonly Brush[][] Heat = [
        [Brush("#B5DCF2"), Brush("#87BDDF"), Brush("#5899C8"), Brush("#326FA9"), Brush("#204B82")],
        [Brush("#E0C9F5"), Brush("#C6A0E7"), Brush("#AA78D3"), Brush("#8B51BA"), Brush("#69358F")],
        [Brush("#FFF0B3"), Brush("#F5D778"), Brush("#E7BA43"), Brush("#CC9826"), Brush("#A87717")]
    ];
    private static readonly Brush Text = Brush("#A8AFBF");
    private ReviewStreak? streak;
    private int selected;
    private int offset;
    private int maximum;
    public event Action<string>? DaySelected;
    public StreakCalendar()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        ToolTipService.SetInitialShowDelay(this, 100);
    }
    private static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
    public void SetStreak(ReviewStreak? value)
    {
        streak = value;
        selected = 0;
        offset = value is null ? 0 : (int)value.Start.DayOfWeek;
        maximum = value?.Days.Max(d => d.Answers) ?? 1;
        InvalidateMeasure();
        InvalidateVisual();
        Announce();
    }
    protected override Size MeasureOverride(Size availableSize) =>
        new(streak is null ? 0 : Math.Ceiling((offset + streak.Length) / 7.0) * Step, Top + 7 * Step);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (streak is null) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        string? lastMonth = null;
        for (var i = 0; i < streak.Length; i++)
        {
            var day = streak.Days[i];
            var cell = i + offset;
            var x = cell / 7 * Step;
            var y = Top + cell % 7 * Step;
            var month = day.Day.ToString("MMM yyyy", CultureInfo.CurrentCulture);
            // Label at most once per month, leaving space for the full month and year.
            if ((i == 0 || cell % 7 == 0) && month != lastMonth && (i == 0 || cell / 7 >= 4))
            {
                dc.DrawText(new FormattedText(month, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 11, Text, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, 2));
                lastMonth = month;
            }
            var (band, level) = Density(day.Answers, maximum);
            dc.DrawRoundedRectangle(Heat[band][level], null, new Rect(x + 2, y + 2, 23, 23), 2, 2);
            if (i == selected && IsKeyboardFocusWithin)
                dc.DrawRectangle(null, new Pen(Brushes.White, 2), new Rect(x, y, 27, 27));
        }
    }
    private static (int Band, int Level) Density(int answers, int maximum)
    {
        if (answers <= 499) return (0, Math.Clamp((answers - 1) * 5 / 499, 0, 4));
        if (answers <= 999) return (1, (answers - 500) / 100);
        // Gold is relative to this streak's busiest day, with no fixed upper limit.
        if (maximum <= 1000) return (2, 4);
        return (2, Math.Min(4, (int)((long)(answers - 1000) * 5 / (maximum - 999))));
    }
    private int Hit(Point point)
    {
        if (streak is null || point.X < 0 || point.Y < Top || point.Y >= Top + 7 * Step) return -1;
        var index = (int)(point.X / Step) * 7 + (int)((point.Y - Top) / Step) - offset;
        return index >= 0 && index < streak.Length ? index : -1;
    }
    private string Description(int index) => streak is null ? "" : $"{streak.Days[index].Day:dddd, MMMM d, yyyy} · {streak.Days[index].Answers:N0} answers";
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = Hit(e.GetPosition(this));
        ToolTip = index < 0 ? null : Description(index);
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var index = Hit(e.GetPosition(this));
        if (index < 0) return;
        selected = index;
        Focus();
        Announce();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (streak is null) return;
        var next = e.Key switch { Key.Left => selected - 7, Key.Right => selected + 7, Key.Up => selected - 1,
            Key.Down => selected + 1, Key.Home => 0, Key.End => streak.Length - 1, _ => selected };
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)) return;
        selected = Math.Clamp(next, 0, streak.Length - 1);
        Announce();
        BringIntoView(new Rect((selected + offset) / 7 * Step, 0, Step, RenderSize.Height));
        e.Handled = true;
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
    private void Announce()
    {
        var text = streak is null ? "" : Description(selected);
        System.Windows.Automation.AutomationProperties.SetName(this, "Streak calendar. " + text);
        DaySelected?.Invoke(text);
        InvalidateVisual();
    }
}
