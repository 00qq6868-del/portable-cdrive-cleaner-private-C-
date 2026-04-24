using System.Drawing.Drawing2D;
using System.ComponentModel;
using PortableCDriveCleaner.Infrastructure;

namespace PortableCDriveCleaner.Forms;

public sealed class ThemedProgressBar : Control
{
    private int _minimum;
    private int _maximum = 100;
    private int _value;
    private bool _isIndeterminate;

    public ThemedProgressBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor,
            true);

        Height = 12;
        TrackColor = UiThemePalette.SurfaceMuted;
        FillColor = UiThemePalette.Accent;
        BorderColor = UiThemePalette.Border;
        BackColor = Color.Transparent;
        ForeColor = UiThemePalette.Accent;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minimum
    {
        get => _minimum;
        set
        {
            if (_minimum == value)
            {
                return;
            }

            _minimum = value;
            if (_maximum < _minimum)
            {
                _maximum = _minimum;
            }

            Value = Math.Clamp(_value, _minimum, _maximum);
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => _maximum;
        set
        {
            var normalized = Math.Max(value, _minimum);
            if (_maximum == normalized)
            {
                return;
            }

            _maximum = normalized;
            Value = Math.Clamp(_value, _minimum, _maximum);
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            var normalized = Math.Clamp(value, _minimum, _maximum);
            if (_value == normalized)
            {
                return;
            }

            _value = normalized;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set
        {
            if (_isIndeterminate == value)
            {
                return;
            }

            _isIndeterminate = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color TrackColor { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 6;

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Parent?.BackColor ?? UiThemePalette.WindowBackground);

        var bounds = ClientRectangle;
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        bounds.Inflate(-1, -1);
        using var trackPath = CreateRoundedRect(bounds, Math.Min(CornerRadius, Math.Min(bounds.Width, bounds.Height) / 2));
        using var trackBrush = new SolidBrush(TrackColor);
        using var borderPen = new Pen(BorderColor);
        graphics.FillPath(trackBrush, trackPath);

        var ratio = ResolveDisplayRatio();
        if (ratio > 0)
        {
            var fillWidth = Math.Clamp((int)Math.Round(bounds.Width * ratio), Math.Min(bounds.Height, bounds.Width), bounds.Width);
            var fillBounds = new Rectangle(bounds.X, bounds.Y, fillWidth, bounds.Height);
            using var clipRegion = new Region(trackPath);
            graphics.Clip = clipRegion;
            using var fillPath = CreateRoundedRect(fillBounds, Math.Min(CornerRadius, Math.Min(fillBounds.Width, fillBounds.Height) / 2));
            using var fillBrush = new SolidBrush(FillColor);
            graphics.FillPath(fillBrush, fillPath);
            graphics.ResetClip();
        }

        graphics.DrawPath(borderPen, trackPath);
    }

    private float ResolveDisplayRatio()
    {
        if (_maximum <= _minimum)
        {
            return 0f;
        }

        if (_value <= _minimum)
        {
            return _isIndeterminate ? 0.08f : 0f;
        }

        return Math.Clamp((float)(_value - _minimum) / (_maximum - _minimum), 0f, 1f);
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
}
