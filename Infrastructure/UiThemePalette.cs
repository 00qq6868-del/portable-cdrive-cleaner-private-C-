using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PortableCDriveCleaner.Infrastructure;

public static class UiThemePalette
{
    public static Color WindowBackground => Color.FromArgb(7, 9, 11);
    public static Color Surface => Color.FromArgb(14, 18, 22);
    public static Color SurfaceRaised => Color.FromArgb(18, 23, 28);
    public static Color SurfaceMuted => Color.FromArgb(11, 14, 17);
    public static Color Border => Color.FromArgb(42, 50, 57);
    public static Color BorderStrong => Color.FromArgb(60, 71, 79);
    public static Color TextPrimary => Color.FromArgb(236, 241, 244);
    public static Color TextSecondary => Color.FromArgb(183, 193, 200);
    public static Color TextMuted => Color.FromArgb(129, 141, 149);
    public static Color DisabledText => Color.FromArgb(92, 102, 109);
    public static Color Accent => Color.FromArgb(48, 196, 115);
    public static Color AccentStrong => Color.FromArgb(68, 216, 135);
    public static Color AccentSurface => Color.FromArgb(17, 45, 31);
    public static Color AccentSurfaceRaised => Color.FromArgb(21, 56, 38);
    public static Color Selection => Color.FromArgb(23, 49, 35);
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
    }

    public static void ApplySurface(Panel panel, bool raised = true)
    {
        panel.BackColor = raised ? SurfaceRaised : Surface;
        AttachBorderPainter(panel);
    }

    public static void AttachBorderPainter(Control control, Color? borderColor = null)
    {
        if (control.Tag is string tagValue && tagValue.Contains("theme-border", StringComparison.Ordinal))
        {
            return;
        }

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
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = primary ? Accent : Surface;
        button.ForeColor = primary ? Color.White : TextPrimary;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? Accent : BorderStrong;
        button.FlatAppearance.MouseDownBackColor = primary ? AccentStrong : SurfaceRaised;
        button.FlatAppearance.MouseOverBackColor = primary ? AccentStrong : SurfaceRaised;
    }

    public static void ApplyPillButtonStyle(Button button, bool selected)
    {
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
        grid.GridColor = Border;
        grid.BorderStyle = BorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceRaised;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = TextPrimary;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceRaised;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextPrimary;
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = TextSecondary;
        grid.DefaultCellStyle.SelectionBackColor = Selection;
        grid.DefaultCellStyle.SelectionForeColor = SelectionText;
        grid.AlternatingRowsDefaultCellStyle.BackColor = SurfaceMuted;
        grid.AlternatingRowsDefaultCellStyle.ForeColor = TextSecondary;
        grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = Selection;
        grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = SelectionText;
        grid.RowHeadersDefaultCellStyle.BackColor = SurfaceRaised;
        grid.RowHeadersDefaultCellStyle.ForeColor = TextSecondary;
        grid.RowHeadersDefaultCellStyle.SelectionBackColor = Selection;
        grid.RowHeadersDefaultCellStyle.SelectionForeColor = SelectionText;
        grid.DefaultCellStyle.NullValue = string.Empty;
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
                if (ShouldNormalizeLayoutBackground(tableLayoutPanel.BackColor))
                {
                    tableLayoutPanel.BackColor = tableLayoutPanel.Parent?.BackColor ?? WindowBackground;
                }

                break;
            case FlowLayoutPanel flowLayoutPanel:
                if (ShouldNormalizeLayoutBackground(flowLayoutPanel.BackColor))
                {
                    flowLayoutPanel.BackColor = flowLayoutPanel.Parent?.BackColor ?? WindowBackground;
                }

                break;
            case Panel panel:
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
}
