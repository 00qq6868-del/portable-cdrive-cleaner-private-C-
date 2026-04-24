using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace PortableCDriveCleaner.Forms;

internal static class UiScaleHelper
{
    private sealed record ButtonSizingSpec(int MinimumWidth, int HorizontalPadding, int MinimumHeight, int VerticalPadding);

    private static readonly ConditionalWeakTable<Button, ButtonSizingSpec> ButtonSizingSpecs = [];
    private static readonly ConcurrentDictionary<string, int> MeasurementCache = new(StringComparer.Ordinal);

    public static int MeasureTextWidth(string text, int minWidth = 0, int extraPadding = 0, Font? font = null)
    {
        var measureFont = font ?? SystemFonts.MessageBoxFont;
        var cacheKey = BuildCacheKey("text", string.IsNullOrWhiteSpace(text) ? "示例" : text, measureFont, minWidth, extraPadding, 0);
        return MeasurementCache.GetOrAdd(cacheKey, _ =>
        {
            var measured = TextRenderer.MeasureText(
                string.IsNullOrWhiteSpace(text) ? "示例" : text,
                measureFont,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            return Math.Max(minWidth, measured.Width + extraPadding + 6);
        });
    }

    public static int MeasureButtonWidth(string text, int minWidth, int horizontalPadding, Font? font = null)
    {
        var measureFont = font ?? SystemFonts.MessageBoxFont;
        var cacheKey = BuildCacheKey("button-width", text, measureFont, minWidth, horizontalPadding, 0);
        return MeasurementCache.GetOrAdd(cacheKey, _ =>
        {
            var measured = TextRenderer.MeasureText(
                text + "  ",
                measureFont,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            return Math.Max(minWidth, measured.Width + horizontalPadding + 14);
        });
    }

    public static int MeasureButtonHeight(int minHeight, int verticalPadding, Font? font = null, string sampleText = "处理中")
    {
        var measureFont = font ?? SystemFonts.MessageBoxFont;
        var cacheKey = BuildCacheKey("button-height", sampleText, measureFont, minHeight, verticalPadding, 0);
        return MeasurementCache.GetOrAdd(cacheKey, _ =>
        {
            var measured = TextRenderer.MeasureText(
                sampleText,
                measureFont,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            return Math.Max(minHeight, measured.Height + verticalPadding + 4);
        });
    }

    public static int MeasureGridColumnWidth(string text, int minWidth, int extraPadding, Font? font = null)
    {
        var measureFont = font ?? SystemFonts.MessageBoxFont;
        var cacheKey = BuildCacheKey("grid-col", text, measureFont, minWidth, extraPadding, 0);
        return MeasurementCache.GetOrAdd(cacheKey, _ =>
        {
            var measured = TextRenderer.MeasureText(text + " ", measureFont, Size.Empty, TextFormatFlags.NoPrefix);
            return Math.Max(minWidth, measured.Width + extraPadding + 6);
        });
    }

    public static int MeasureGridHeaderHeight(Font? font = null, int minHeight = 42, int verticalPadding = 18)
    {
        var headerFont = font ?? SystemFonts.MessageBoxFont;
        var cacheKey = BuildCacheKey("grid-header", "示例标题", headerFont, minHeight, verticalPadding, 0);
        return MeasurementCache.GetOrAdd(cacheKey, _ =>
        {
            var measured = TextRenderer.MeasureText("示例标题", headerFont, Size.Empty, TextFormatFlags.NoPrefix);
            return Math.Max(minHeight, measured.Height + verticalPadding + 4);
        });
    }

    public static int MeasureGridRowHeight(Font? font = null, int minHeight = 34, int verticalPadding = 16)
    {
        var rowFont = font ?? SystemFonts.MessageBoxFont;
        var cacheKey = BuildCacheKey("grid-row", "示例文本", rowFont, minHeight, verticalPadding, 0);
        return MeasurementCache.GetOrAdd(cacheKey, _ =>
        {
            var measured = TextRenderer.MeasureText("示例文本", rowFont, Size.Empty, TextFormatFlags.NoPrefix);
            return Math.Max(minHeight, measured.Height + verticalPadding + 4);
        });
    }

    public static int MeasureWrapWidth(int clientWidth, int horizontalPadding, int minWidth = 280)
    {
        return Math.Max(minWidth, clientWidth - horizontalPadding);
    }

    private static string BuildCacheKey(string prefix, string text, Font? font, int a, int b, int c)
    {
        var keyFont = font ?? SystemFonts.MessageBoxFont;
        if (keyFont is null)
        {
            return string.Join("|", prefix, text, "DefaultFont", 9f, (int)FontStyle.Regular, a, b, c);
        }

        return string.Join("|", prefix, text, keyFont.Name ?? "DefaultFont", keyFont.Size, (int)keyFont.Style, a, b, c);
    }

    public static void ApplyButtonSizing(
        Button button,
        string text,
        int minWidth,
        int horizontalPadding,
        int minHeight = 42,
        int verticalPadding = 18)
    {
        button.Text = text;
        button.AutoSize = false;
        button.AutoEllipsis = false;
        var measuredWidth = MeasureButtonWidth(text, minWidth, horizontalPadding, button.Font);
        var preferredWidth = button.GetPreferredSize(Size.Empty).Width + 10;
        var width = Math.Max(minWidth, Math.Max(measuredWidth, preferredWidth));
        var height = MeasureButtonHeight(minHeight, verticalPadding, button.Font);
        button.Width = width;
        button.Height = height;
        button.MinimumSize = new Size(width, height);
    }

    public static void RegisterButtonSizing(
        Button button,
        string text,
        int minWidth,
        int horizontalPadding,
        int minHeight = 42,
        int verticalPadding = 18)
    {
        ButtonSizingSpecs.Remove(button);
        ButtonSizingSpecs.Add(button, new ButtonSizingSpec(minWidth, horizontalPadding, minHeight, verticalPadding));
        ApplyButtonSizing(button, text, minWidth, horizontalPadding, minHeight, verticalPadding);
    }

    public static void RefreshRegisteredButtonSizing(Button button)
    {
        if (ButtonSizingSpecs.TryGetValue(button, out var spec))
        {
            ApplyButtonSizing(button, button.Text, spec.MinimumWidth, spec.HorizontalPadding, spec.MinimumHeight, spec.VerticalPadding);
        }
    }

    public static void RefreshRegisteredButtonSizing(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is Button button)
            {
                RefreshRegisteredButtonSizing(button);
            }

            if (control.HasChildren)
            {
                RefreshRegisteredButtonSizing(control);
            }
        }
    }

    public static int MeasureOptionWidth(IEnumerable<string> options, int minWidth, int horizontalPadding, Font? font = null)
    {
        var maximum = minWidth;
        foreach (var option in options.Where(option => !string.IsNullOrWhiteSpace(option)))
        {
            maximum = Math.Max(maximum, MeasureTextWidth(option, minWidth, horizontalPadding, font));
        }

        return maximum;
    }
}
