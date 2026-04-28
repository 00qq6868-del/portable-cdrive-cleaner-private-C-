using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PortableCDriveCleaner.Forms;

namespace PortableCDriveCleaner.Infrastructure;

public static class UiThemePalette
{
    public static Color WindowBackground => Color.FromArgb(4, 6, 9);
    public static Color Surface => Color.FromArgb(7, 10, 14);
    public static Color SurfaceRaised => Color.FromArgb(11, 16, 21);
    public static Color SurfaceMuted => Color.FromArgb(5, 8, 11);
    public static Color SurfaceHeader => Color.FromArgb(13, 19, 25);
    public static Color SurfaceHover => Color.FromArgb(16, 23, 30);
    public static Color Border => Color.FromArgb(26, 34, 42);
    public static Color BorderMuted => Color.FromArgb(18, 25, 32);
    public static Color BorderStrong => Color.FromArgb(41, 52, 63);
    public static Color GridLine => Color.FromArgb(24, 31, 38);
    public static Color TextPrimary => Color.FromArgb(240, 244, 247);
    public static Color TextSecondary => Color.FromArgb(184, 193, 201);
    public static Color TextMuted => Color.FromArgb(132, 143, 151);
    public static Color DisabledText => Color.FromArgb(108, 118, 125);
    public static Color Accent => Color.FromArgb(49, 208, 122);
    public static Color AccentStrong => Color.FromArgb(105, 240, 174);
    public static Color AccentSoftBorder => Color.FromArgb(48, 125, 86);
    public static Color AccentSurface => Color.FromArgb(9, 30, 21);
    public static Color AccentSurfaceRaised => Color.FromArgb(12, 39, 27);
    public static Color Selection => Color.FromArgb(14, 45, 31);
    public static Color SelectionText => TextPrimary;
    public static Color Warning => Color.FromArgb(238, 185, 73);
    public static Color WarningSurface => Color.FromArgb(56, 44, 17);
    public static Color Danger => Color.FromArgb(230, 103, 103);
    public static Color DangerSurface => Color.FromArgb(59, 24, 24);
    public static Color Info => Color.FromArgb(123, 184, 255);
    public static Color InfoSurface => Color.FromArgb(18, 36, 58);
    public static Color InputBackground => Color.FromArgb(11, 15, 18);

    public static void ApplyFormChrome(Form form)
    {
        form.BackColor = WindowBackground;
        form.ForeColor = TextPrimary;
        EnableDoubleBuffering(form);
        if (form.ShowIcon)
        {
            try
            {
                form.Icon = ApplicationIconCache.GetAppIcon();
            }
            catch
            {
            }
        }

        ApplyImmersiveDarkMode(form);
    }

    public static void ApplySurface(Panel panel, bool raised = true)
    {
        panel.BackColor = raised ? SurfaceRaised : Surface;
        EnableDoubleBuffering(panel);
        if (raised)
        {
            AttachBorderPainter(panel, BorderMuted);
        }
    }

    public static void EnableDoubleBuffering(Control control)
    {
        try
        {
            var property = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            property?.SetValue(control, true);
        }
        catch
        {
        }
    }

    public static void AttachBorderPainter(Control control, Color? borderColor = null)
    {
        if (control.Tag is string tagValue && tagValue.Contains("theme-border", StringComparison.Ordinal))
        {
            return;
        }

        EnableDoubleBuffering(control);
        control.Tag = control.Tag is null
            ? "theme-border"
            : $"{control.Tag};theme-border";

        control.Paint += (_, e) =>
        {
            using var pen = new Pen(borderColor ?? Border);
            var bounds = control.ClientRectangle;
            bounds.Width -= 1;
            bounds.Height -= 1;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawRectangle(pen, bounds);
        };
    }

    public static void ApplyButtonStyle(Button button, bool primary)
    {
        if (button is ThemedButton themedButton)
        {
            themedButton.VisualStyle = primary ? ThemedButtonVisualStyle.Primary : ThemedButtonVisualStyle.Secondary;
            themedButton.BackColor = primary ? Accent : Surface;
            themedButton.ForeColor = primary ? TextPrimary : TextPrimary;
            themedButton.FlatAppearance.BorderSize = 0;
            return;
        }

        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = primary ? Accent : Surface;
        button.ForeColor = primary ? TextPrimary : TextPrimary;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? Accent : BorderStrong;
        button.FlatAppearance.MouseDownBackColor = primary ? AccentStrong : SurfaceRaised;
        button.FlatAppearance.MouseOverBackColor = primary ? AccentStrong : SurfaceRaised;
    }

    public static void ApplyPillButtonStyle(Button button, bool selected)
    {
        if (button is ThemedButton themedButton)
        {
            themedButton.VisualStyle = selected ? ThemedButtonVisualStyle.PillSelected : ThemedButtonVisualStyle.Pill;
            themedButton.BackColor = selected ? AccentSurfaceRaised : SurfaceMuted;
            themedButton.ForeColor = selected ? AccentStrong : TextSecondary;
            themedButton.FlatAppearance.BorderSize = 0;
            return;
        }

        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = selected ? AccentSurfaceRaised : SurfaceMuted;
        button.ForeColor = selected ? AccentStrong : TextSecondary;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = selected ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = selected ? AccentSurfaceRaised : Surface;
        button.FlatAppearance.MouseDownBackColor = selected ? AccentSurfaceRaised : SurfaceRaised;
    }

    public static void ApplyTextBoxStyle(TextBox textBox)
    {
        textBox.BackColor = InputBackground;
        textBox.ForeColor = TextPrimary;
        textBox.BorderStyle = BorderStyle.FixedSingle;
    }

    public static void ApplyComboBoxStyle(ComboBox comboBox)
    {
        comboBox.BackColor = InputBackground;
        comboBox.ForeColor = TextPrimary;
        comboBox.FlatStyle = FlatStyle.Flat;
    }

    public static void ApplyNumericUpDownStyle(NumericUpDown numericUpDown)
    {
        numericUpDown.BackColor = InputBackground;
        numericUpDown.ForeColor = TextPrimary;
        numericUpDown.BorderStyle = BorderStyle.FixedSingle;
    }

    public static void ApplyCheckBoxStyle(CheckBox checkBox)
    {
        checkBox.ForeColor = ResolveTextColor(checkBox.ForeColor, checkBox.Font);
        checkBox.BackColor = Color.Transparent;
    }

    public static void ApplyLinkStyle(LinkLabel linkLabel, bool warning = false)
    {
        var activeColor = warning ? Warning : AccentStrong;
        linkLabel.LinkColor = activeColor;
        linkLabel.ActiveLinkColor = activeColor;
        linkLabel.VisitedLinkColor = activeColor;
        linkLabel.ForeColor = activeColor;
        linkLabel.BackColor = Color.Transparent;
    }

    public static void ApplyStatusStripStyle(StatusStrip statusStrip)
    {
        statusStrip.BackColor = Surface;
        statusStrip.ForeColor = TextSecondary;
        foreach (ToolStripItem item in statusStrip.Items)
        {
            item.ForeColor = TextSecondary;
        }
    }

    public static void ApplyDataGridTheme(DataGridView grid)
    {
        grid.BackgroundColor = Surface;
        grid.GridColor = GridLine;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        ApplyDataGridBorderModel(grid);

        grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceHeader;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = TextPrimary;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceHeader;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextPrimary;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(0, 2, 0, 2);
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = TextSecondary;
        grid.DefaultCellStyle.SelectionBackColor = Selection;
        grid.DefaultCellStyle.SelectionForeColor = SelectionText;
        grid.DefaultCellStyle.Padding = new Padding(0, 0, 0, 0);
        grid.AlternatingRowsDefaultCellStyle.BackColor = SurfaceMuted;
        grid.AlternatingRowsDefaultCellStyle.ForeColor = TextSecondary;
        grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = Selection;
        grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = SelectionText;
        grid.RowHeadersDefaultCellStyle.BackColor = SurfaceHeader;
        grid.RowHeadersDefaultCellStyle.ForeColor = TextSecondary;
        grid.RowHeadersDefaultCellStyle.SelectionBackColor = Selection;
        grid.RowHeadersDefaultCellStyle.SelectionForeColor = SelectionText;
        grid.DefaultCellStyle.NullValue = string.Empty;
    }

    private static void ApplyDataGridBorderModel(DataGridView grid)
    {
        try
        {
            grid.AdvancedCellBorderStyle.Left = DataGridViewAdvancedCellBorderStyle.None;
            grid.AdvancedCellBorderStyle.Right = DataGridViewAdvancedCellBorderStyle.None;
            grid.AdvancedCellBorderStyle.Top = DataGridViewAdvancedCellBorderStyle.None;
            grid.AdvancedCellBorderStyle.Bottom = DataGridViewAdvancedCellBorderStyle.Single;
            grid.AdvancedColumnHeadersBorderStyle.Left = DataGridViewAdvancedCellBorderStyle.None;
            grid.AdvancedColumnHeadersBorderStyle.Right = DataGridViewAdvancedCellBorderStyle.None;
            grid.AdvancedColumnHeadersBorderStyle.Top = DataGridViewAdvancedCellBorderStyle.None;
            grid.AdvancedColumnHeadersBorderStyle.Bottom = DataGridViewAdvancedCellBorderStyle.Single;
        }
        catch
        {
        }
    }

    public static void ApplyTreeTheme(Control root)
    {
        foreach (Control control in root.Controls)
        {
            ApplyControlTheme(control);
            if (control.HasChildren)
            {
                ApplyTreeTheme(control);
            }
        }
    }

    public static Color ResolveTextColor(Color original, Font font)
    {
        if (LooksLikeDanger(original))
        {
            return Danger;
        }

        if (LooksLikeWarning(original))
        {
            return Warning;
        }

        if (LooksLikeAccent(original))
        {
            return AccentStrong;
        }

        if (LooksLikeInfo(original))
        {
            return Info;
        }

        return font.Bold || font.Size >= 10.5f
            ? TextPrimary
            : TextSecondary;
    }

    private static void ApplyControlTheme(Control control)
    {
        switch (control)
        {
            case LinkLabel linkLabel:
                ApplyLinkStyle(linkLabel, LooksLikeWarning(linkLabel.ForeColor));
                break;
            case Label label:
                label.ForeColor = ResolveTextColor(label.ForeColor, label.Font);
                if (label.BackColor != Color.Transparent)
                {
                    label.BackColor = Color.Transparent;
                }

                break;
            case Button button:
                ApplyButtonStyle(button, LooksLikeAccent(button.BackColor));
                break;
            case TextBox textBox:
                ApplyTextBoxStyle(textBox);
                break;
            case ComboBox comboBox:
                ApplyComboBoxStyle(comboBox);
                break;
            case NumericUpDown numericUpDown:
                ApplyNumericUpDownStyle(numericUpDown);
                break;
            case CheckBox checkBox:
                ApplyCheckBoxStyle(checkBox);
                break;
            case StatusStrip statusStrip:
                ApplyStatusStripStyle(statusStrip);
                break;
            case DataGridView dataGridView:
                ApplyDataGridTheme(dataGridView);
                break;
            case TableLayoutPanel tableLayoutPanel:
                EnableDoubleBuffering(tableLayoutPanel);
                if (ShouldNormalizeLayoutBackground(tableLayoutPanel.BackColor))
                {
                    tableLayoutPanel.BackColor = tableLayoutPanel.Parent?.BackColor ?? WindowBackground;
                }

                break;
            case FlowLayoutPanel flowLayoutPanel:
                EnableDoubleBuffering(flowLayoutPanel);
                if (ShouldNormalizeLayoutBackground(flowLayoutPanel.BackColor))
                {
                    flowLayoutPanel.BackColor = flowLayoutPanel.Parent?.BackColor ?? WindowBackground;
                }

                break;
            case Panel panel:
                EnableDoubleBuffering(panel);
                if (ShouldDarkenSurface(panel.BackColor))
                {
                    panel.BackColor = Surface;
                }

                break;
            case RichTextBox richTextBox:
                richTextBox.BackColor = SurfaceMuted;
                richTextBox.ForeColor = TextSecondary;
                break;
        }
    }

    private static bool ShouldDarkenSurface(Color color)
    {
        return color.A > 0 && color.GetBrightness() > 0.35f;
    }

    private static bool ShouldNormalizeLayoutBackground(Color color)
    {
        return color == default || color == SystemColors.Control || color.GetBrightness() > 0.35f;
    }

    private static bool LooksLikeAccent(Color color)
    {
        return color.G >= color.R + 18 && color.G >= color.B + 8;
    }

    private static bool LooksLikeWarning(Color color)
    {
        return color.R >= 120 && color.G >= 90 && color.B <= 120;
    }

    private static bool LooksLikeDanger(Color color)
    {
        return color.R >= color.G + 24 && color.R >= color.B + 18;
    }

    private static bool LooksLikeInfo(Color color)
    {
        return color.B >= color.R + 24 && color.B >= color.G + 12;
    }

    private static void ApplyImmersiveDarkMode(Form form)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        if (form.IsHandleCreated)
        {
            TryEnableImmersiveDarkMode(form.Handle);
        }
        else
        {
            form.HandleCreated += (_, _) => TryEnableImmersiveDarkMode(form.Handle);
        }
    }

    private static void TryEnableImmersiveDarkMode(IntPtr handle)
    {
        try
        {
            const int DwmwaUseImmersiveDarkMode = 20;
            const int DwmwaUseImmersiveDarkModeLegacy = 19;
            const int DwmwaBorderColor = 34;
            const int DwmwaCaptionColor = 35;
            const int DwmwaTextColor = 36;

            var enabled = 1;
            var captionColor = ColorTranslator.ToWin32(WindowBackground);
            var borderColor = ColorTranslator.ToWin32(BorderMuted);
            var textColor = ColorTranslator.ToWin32(TextPrimary);

            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
            DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
            DwmSetWindowAttribute(handle, DwmwaBorderColor, ref borderColor, sizeof(int));
            DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, sizeof(int));
        }
        catch
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
}
