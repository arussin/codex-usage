using System.Runtime.InteropServices;

namespace CodexUsageTray;

/// <summary>
/// The bar drawn under a tray number so several numbers can be told apart at a glance.
/// </summary>
internal enum TrayBar
{
    None,
    Half,
    Full,
    Split,
}

internal static class TrayIconRenderer
{
    private const int IconSize = 32;
    private const int BarHeight = 4;

    /// <summary>
    /// Renders a numeric tray icon for one rate-limit window.
    /// </summary>
    /// <param name="reading">The normalized limit reading.</param>
    /// <param name="textColor">The colour of the digits.</param>
    /// <param name="bar">The bar shape under the digits.</param>
    /// <param name="barColor">The bar colour, identifying the source.</param>
    /// <returns>An independently owned Windows icon.</returns>
    internal static Icon Render(
        LimitReading reading,
        Color textColor,
        TrayBar bar = TrayBar.None,
        Color barColor = default)
    {
        string text = reading.State == LimitState.Available && reading.RemainingPercent is int remaining
            ? Math.Clamp(remaining, 0, 100).ToString()
            : "--";

        using Bitmap bitmap = new(IconSize, IconSize);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);

        // A bar takes the bottom rows, so the digits move up to stay clear of it.
        int textHeight = bar == TrayBar.None ? IconSize : IconSize - BarHeight;
        float fontSize = text.Length == 3 ? 14 : 22;
        using Font font = new("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        TextFormatFlags flags =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            new Rectangle(1, 1, IconSize, textHeight),
            Color.Black,
            flags);
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            new Rectangle(0, 0, IconSize, textHeight),
            textColor,
            flags);
        DrawBar(graphics, bar, barColor);

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
    /// Draws the identifying bar along the bottom edge of the icon.
    /// </summary>
    /// <param name="graphics">The icon drawing surface.</param>
    /// <param name="bar">The bar shape.</param>
    /// <param name="barColor">The bar colour.</param>
    private static void DrawBar(Graphics graphics, TrayBar bar, Color barColor)
    {
        if (bar == TrayBar.None)
        {
            return;
        }

        int top = IconSize - BarHeight;
        using SolidBrush brush = new(barColor);
        switch (bar)
        {
            case TrayBar.Half:
                graphics.FillRectangle(brush, 0, top, IconSize / 2, BarHeight);
                break;
            case TrayBar.Split:
                graphics.FillRectangle(brush, 0, top, 12, BarHeight);
                graphics.FillRectangle(brush, IconSize - 12, top, 12, BarHeight);
                break;
            default:
                graphics.FillRectangle(brush, 0, top, IconSize, BarHeight);
                break;
        }
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
