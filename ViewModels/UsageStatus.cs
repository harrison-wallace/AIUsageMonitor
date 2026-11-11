using System.Windows;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using MediaBrush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace AIUsageMonitor.ViewModels;

// The VortexOne status scale. A status always shows with its icon and its label, never by colour
// alone. The text is in the status colour on a transparent chip.
public sealed class UsageStatus
{
    private static readonly UsageStatus Optimal = new("Optimal", "#7FD1A8", StatusIcon.FilledCheck);
    private static readonly UsageStatus Good = new("Good", "#8AB4F2", StatusIcon.OutlineCheck);
    private static readonly UsageStatus Watch = new("Watch", "#E5B869", StatusIcon.Triangle);
    private static readonly UsageStatus NeedsAttention = new("Needs attention", "#EF8A7A", StatusIcon.Octagon);
    public static readonly UsageStatus Unavailable = new("No data", "#AEA69C", StatusIcon.Dash);
    public static readonly UsageStatus Balance = new("Balance", "#AEA69C", StatusIcon.Dash);

    private enum StatusIcon { FilledCheck, OutlineCheck, Triangle, Octagon, Dash }

    private UsageStatus(string label, string foreground, StatusIcon icon)
    {
        Label = label;
        Foreground = UsageBrushes.FrozenBrush(foreground);
        Background = Brushes.Transparent;
        Icon = BuildIcon(icon, Foreground);
    }

    public string Label { get; }

    public MediaBrush Foreground { get; }

    public MediaBrush Background { get; }

    public ImageSource Icon { get; }

    // The pace icon uses the Watch colour when usage runs ahead of the time passed (ADR 0002).
    public static MediaBrush AheadOfPaceBrush => Watch.Foreground;

    public static UsageStatus FromRemainingPercent(double remainingPercent) => FromUsedPercent(100 - remainingPercent);

    public static UsageStatus FromUsedPercent(double usedPercent)
    {
        return usedPercent switch
        {
            >= 90 => NeedsAttention,
            >= 75 => Watch,
            >= 50 => Good,
            _ => Optimal
        };
    }

    private static ImageSource BuildIcon(StatusIcon icon, MediaBrush brush)
    {
        var pen = new Pen(brush, 1.4) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var group = new DrawingGroup();
        // A fixed 16x16 box, so every icon lines up.
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        var check = Geometry.Parse("M4.8,8.2 L6.9,10.3 L11.2,5.9");
        switch (icon)
        {
            case StatusIcon.FilledCheck:
                group.Children.Add(new GeometryDrawing(brush, null, new EllipseGeometry(new Point(8, 8), 7, 7)));
                group.Children.Add(new GeometryDrawing(null, new Pen(UsageBrushes.Canvas, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, check));
                break;
            case StatusIcon.OutlineCheck:
                group.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new Point(8, 8), 6.3, 6.3)));
                group.Children.Add(new GeometryDrawing(null, pen, check));
                break;
            case StatusIcon.Triangle:
                group.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M8,1.8 L14.6,13.8 L1.4,13.8 Z")));
                group.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M8,6.2 L8,9.8 M8,11.6 L8,11.8")));
                break;
            case StatusIcon.Octagon:
                group.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M5.2,1.5 L10.8,1.5 L14.5,5.2 L14.5,10.8 L10.8,14.5 L5.2,14.5 L1.5,10.8 L1.5,5.2 Z")));
                group.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M8,4.6 L8,8.8 M8,10.8 L8,11")));
                break;
            default:
                group.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M4.5,8 L11.5,8")));
                break;
        }

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
