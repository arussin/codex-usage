using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CodexUsageTray;

/// <summary>
/// The frame drawn around a tray number so several numbers can be told apart at a glance.
/// Its colour names the source; its shape names the window.
/// </summary>
internal enum TrayFrame
{
    /// <summary>No frame: the original single weekly number.</summary>
    None,

    /// <summary>A square, like a calendar page: a weekly window.</summary>
    Square,

    /// <summary>A circle, like a clock: the five-hour window.</summary>
    Circle,

    /// <summary>A square with a filled corner: a weekly limit scoped to one model.</summary>
    Flagged,
}

internal static class TrayIconRenderer
{
    private const int IconSize = 32;
    private const float FrameWidth = 3;

    /// <summary>
    /// Renders a numeric tray icon for one rate-limit window.
    /// </summary>
    /// <param name="reading">The normalized limit reading.</param>
    /// <param name="textColor">The colour of the digits.</param>
    /// <param name="frame">The frame shape around the digits.</param>
    /// <param name="frameColor">The frame colour, identifying the source.</param>
    /// <returns>An independently owned Windows icon.</returns>
    internal static Icon Render(
        LimitReading reading,
        Color textColor,
        TrayFrame frame = TrayFrame.None,
        Color frameColor = default)
    {
        string text = reading.State == LimitState.Available && reading.RemainingPercent is int remaining
            ? Math.Clamp(remaining, 0, 100).ToString()
            : "--";

        using Bitmap bitmap = new(IconSize, IconSize);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        DrawFrame(graphics, frame, frameColor);

        // Framed digits are smaller so they stay clear of the frame.
        float fontSize = frame == TrayFrame.None
            ? (text.Length == 3 ? 14 : 22)
            : (text.Length == 3 ? 13 : 18);
        using Font font = new("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        TextFormatFlags flags =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            new Rectangle(1, 1, IconSize, IconSize),
            Color.Black,
            flags);
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            new Rectangle(0, 0, IconSize, IconSize),
            textColor,
            flags);

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    /// <summary>
    /// Draws the identifying frame around the edge of the icon.
    /// </summary>
    /// <param name="graphics">The icon drawing surface.</param>
    /// <param name="frame">The frame shape.</param>
    /// <param name="frameColor">The frame colour.</param>
    private static void DrawFrame(Graphics graphics, TrayFrame frame, Color frameColor)
    {
        if (frame == TrayFrame.None)
        {
            return;
        }

        SmoothingMode previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float inset = FrameWidth / 2;
        float side = IconSize - FrameWidth;
        using Pen pen = new(frameColor, FrameWidth);
        if (frame == TrayFrame.Circle)
        {
            graphics.DrawEllipse(pen, inset, inset, side, side);
        }
        else
        {
            const float Corner = 10;
            using GraphicsPath square = new();
            square.AddArc(inset, inset, Corner, Corner, 180, 90);
            square.AddArc(inset + side - Corner, inset, Corner, Corner, 270, 90);
            square.AddArc(inset + side - Corner, inset + side - Corner, Corner, Corner, 0, 90);
            square.AddArc(inset, inset + side - Corner, Corner, Corner, 90, 90);
            square.CloseFigure();
            graphics.DrawPath(pen, square);
            if (frame == TrayFrame.Flagged)
            {
                using SolidBrush brush = new(frameColor);
                graphics.FillPolygon(brush, [new PointF(18, 1), new PointF(31, 1), new PointF(31, 14)]);
            }
        }

        graphics.SmoothingMode = previous;
    }

    /// <summary>
    /// Selects the tray text colour from the weekly pacing balance.
    /// </summary>
    /// <param name="left">The percentage remaining above the pacing target.</param>
    /// <returns>Red below target, green on target, or gray when unavailable.</returns>
    internal static Color ResolveTextColor(double? left) =>
        left is null
            ? Color.Gray
            : left < 0
                ? Color.Red
                : Color.LimeGreen;

    /// <summary>
    /// Selects the tray text colour for a short window, which has no meaningful daily pacing.
    /// </summary>
    /// <param name="reading">The normalized limit reading.</param>
    /// <returns>Red at or below the low-usage alert level, green above it, or gray when unavailable.</returns>
    internal static Color ResolveShortWindowColor(LimitReading reading) =>
        reading.State != LimitState.Available || reading.RemainingPercent is not int remaining
            ? Color.Gray
            : remaining <= AlertStateStore.LowPercent
                ? Color.Red
                : Color.LimeGreen;

    /// <summary>
    /// Releases a native icon handle created from a bitmap.
    /// </summary>
    /// <param name="handle">The native icon handle.</param>
    /// <returns><see langword="true"/> when the handle was released.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
