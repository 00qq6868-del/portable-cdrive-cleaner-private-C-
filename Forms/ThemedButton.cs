using System.Drawing.Drawing2D;
using System.ComponentModel;
using PortableCDriveCleaner.Infrastructure;

namespace PortableCDriveCleaner.Forms;

public enum ThemedButtonVisualStyle
{
    Secondary,
    Primary,
    Pill,
    PillSelected,
    Ghost
}

public sealed class ThemedButton : Button
{
    private bool _hovered;
    private bool _pressed;
    private ThemedButtonVisualStyle _visualStyle = ThemedButtonVisualStyle.Secondary;

    public ThemedButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor,
            true);

        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        BackColor = UiThemePalette.Surface;
        ForeColor = UiThemePalette.TextPrimary;
        FlatAppearance.BorderSize = 0;
        TabStop = true;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemedButtonVisualStyle VisualStyle
    {
        get => _visualStyle;
        set
        {
            if (_visualStyle == value)
            {
                return;
            }

            _visualStyle = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 10;

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        if (mevent.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _pressed = false;
        Invalidate();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Invalidate();
    }

    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        Invalidate();
    }

    protected override void OnForeColorChanged(EventArgs e)
    {
        base.OnForeColorChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var graphics = pevent.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(ResolveParentBackColor());

        var bounds = ClientRectangle;
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        bounds.Inflate(-1, -1);
        var state = ResolveVisualState();
        using var path = CreateRoundedRect(bounds, Math.Min(CornerRadius, Math.Min(bounds.Width, bounds.Height) / 2));
        using var fillBrush = new SolidBrush(state.FillColor);
        using var borderPen = new Pen(state.BorderColor);
        graphics.FillPath(fillBrush, path);
        graphics.DrawPath(borderPen, path);

        if (VisualStyle is ThemedButtonVisualStyle.Primary or ThemedButtonVisualStyle.PillSelected)
        {
            var highlightBounds = bounds;
            highlightBounds.Inflate(-2, -2);
            highlightBounds.Height = Math.Max(1, Math.Min(3, highlightBounds.Height / 5));
            using var highlightPen = new Pen(Color.FromArgb(90, UiThemePalette.AccentStrong));
            graphics.DrawLine(highlightPen, highlightBounds.Left + CornerRadius / 2, highlightBounds.Top, highlightBounds.Right - CornerRadius / 2, highlightBounds.Top);
        }

        if (Focused && ShowFocusCues && Enabled)
        {
            var focusBounds = bounds;
            focusBounds.Inflate(-4, -4);
            using var focusPath = CreateRoundedRect(focusBounds, Math.Max(4, CornerRadius - 3));
            using var focusPen = new Pen(Color.FromArgb(120, UiThemePalette.AccentStrong)) { DashStyle = DashStyle.Dot };
            graphics.DrawPath(focusPen, focusPath);
        }

        var textBounds = Rectangle.Inflate(bounds, -5, -3);
        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            textBounds,
            state.TextColor,
            TextFormatFlags.HorizontalCenter
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPrefix
            | TextFormatFlags.NoPadding
            | TextFormatFlags.SingleLine);
    }

    private Color ResolveParentBackColor()
    {
        return Parent?.BackColor ?? UiThemePalette.WindowBackground;
    }

    private ButtonVisualState ResolveVisualState()
    {
        if (!Enabled)
        {
            return new ButtonVisualState(
                UiThemePalette.SurfaceMuted,
                VisualStyle is ThemedButtonVisualStyle.Primary ? UiThemePalette.BorderStrong : UiThemePalette.BorderMuted,
                UiThemePalette.DisabledText);
        }

        return VisualStyle switch
        {
            ThemedButtonVisualStyle.Primary => CreateState(
                UiThemePalette.Accent,
                UiThemePalette.AccentStrong,
                Color.FromArgb(3, 16, 10),
                hoverBoost: 0.06f,
                pressBoost: -0.08f),
            ThemedButtonVisualStyle.Pill => CreateState(
                UiThemePalette.Surface,
                UiThemePalette.BorderMuted,
                UiThemePalette.TextSecondary,
                hoverBoost: 0.05f,
                pressBoost: 0.09f),
            ThemedButtonVisualStyle.PillSelected => CreateState(
                UiThemePalette.AccentSurface,
                UiThemePalette.AccentSoftBorder,
                UiThemePalette.AccentStrong,
                hoverBoost: 0.05f,
                pressBoost: -0.02f),
            ThemedButtonVisualStyle.Ghost => CreateState(
                Color.FromArgb(0, 0, 0, 0),
                Color.FromArgb(0, 0, 0, 0),
                UiThemePalette.TextSecondary,
                hoverBoost: 0.06f,
                pressBoost: -0.04f),
            _ => CreateState(
                UiThemePalette.SurfaceMuted,
                UiThemePalette.Border,
                UiThemePalette.TextSecondary,
                hoverBoost: 0.05f,
                pressBoost: 0.09f)
        };
    }

    private ButtonVisualState CreateState(Color fill, Color border, Color text, float hoverBoost, float pressBoost)
    {
        if (_pressed)
        {
            return new ButtonVisualState(Shift(fill, pressBoost), Shift(border, pressBoost), text);
        }

        if (_hovered)
        {
            return new ButtonVisualState(Shift(fill, hoverBoost), Shift(border, hoverBoost), text);
        }

        return new ButtonVisualState(fill, border, text);
    }

    private static Color Shift(Color color, float amount)
    {
        if (color.A <= 0)
        {
            return color;
        }

        var target = amount >= 0 ? Color.White : Color.Black;
        var blend = Math.Abs(amount);
        return Blend(color, target, blend);
    }

    private static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(from.A + ((to.A - from.A) * amount)),
            (int)Math.Round(from.R + ((to.R - from.R) * amount)),
            (int)Math.Round(from.G + ((to.G - from.G) * amount)),
            (int)Math.Round(from.B + ((to.B - from.B) * amount)));
    }

    private static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 1)
        {
            path.AddRectangle(bounds);
            path.CloseFigure();
            return path;
        }

        var diameter = radius * 2;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private readonly record struct ButtonVisualState(Color FillColor, Color BorderColor, Color TextColor);
}
