using PortableCDriveCleaner.Infrastructure;

namespace PortableCDriveCleaner.Forms;

public sealed class ElevationPromptDialog : Form
{
    private readonly Label _messageLabel = new();
    private readonly Panel _messageCard = new();

    public ElevationPromptDialog(
        string title,
        string heading,
        string message,
        string primaryButtonText,
        string? secondaryButtonText,
        string? tertiaryButtonText = null)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimizeBox = true;
        MaximizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(760, 360);
        ClientSize = new Size(780, 380);
        UiThemePalette.ApplyFormChrome(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(20, 18, 20, 18),
            BackColor = UiThemePalette.WindowBackground
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 14, FontStyle.Bold),
            ForeColor = Color.FromArgb(27, 42, 56),
            Margin = new Padding(0, 0, 0, 10),
            Text = heading
        }, 0, 0);

        _messageCard.Dock = DockStyle.Fill;
        _messageCard.BackColor = UiThemePalette.SurfaceRaised;
        _messageCard.Padding = new Padding(18, 16, 18, 16);
        _messageCard.Margin = Padding.Empty;
        UiThemePalette.AttachBorderPainter(_messageCard);
        root.Controls.Add(_messageCard, 0, 1);

        _messageLabel.AutoSize = true;
        _messageLabel.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular);
        _messageLabel.ForeColor = Color.FromArgb(72, 85, 96);
        _messageLabel.Dock = DockStyle.Top;
        _messageLabel.Text = message;
        _messageCard.Controls.Add(_messageLabel);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 14, 0, 0)
        };
        root.Controls.Add(footer, 0, 2);

        if (!string.IsNullOrWhiteSpace(tertiaryButtonText))
        {
            var tertiaryButton = CreateFooterButton(tertiaryButtonText, primary: false);
            tertiaryButton.Click += (_, _) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            footer.Controls.Add(tertiaryButton);
        }

        if (!string.IsNullOrWhiteSpace(secondaryButtonText))
        {
            var secondaryButton = CreateFooterButton(secondaryButtonText, primary: false);
            secondaryButton.Click += (_, _) =>
            {
                DialogResult = DialogResult.No;
                Close();
            };
            footer.Controls.Add(secondaryButton);
        }

        var primaryButton = CreateFooterButton(primaryButtonText, primary: true);
        primaryButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Yes;
            Close();
        };
        footer.Controls.Add(primaryButton);

        AcceptButton = primaryButton;

        Resize += (_, _) => UpdateResponsiveLayout();
        Shown += (_, _) => UpdateResponsiveLayout();
        DpiChanged += (_, _) => BeginInvoke(new Action(() =>
        {
            UiScaleHelper.RefreshRegisteredButtonSizing(this);
            UpdateResponsiveLayout();
        }));
        UiThemePalette.ApplyTreeTheme(this);
    }

    private static Button CreateFooterButton(string text, bool primary)
    {
        var button = new Button
        {
            Margin = new Padding(10, 0, 0, 0),
            Padding = new Padding(16, 0, 16, 0),
            Text = text
        };
        UiThemePalette.ApplyButtonStyle(button, primary);
        UiScaleHelper.RegisterButtonSizing(button, text, 112, 42, minHeight: 42, verticalPadding: 18);
        return button;
    }

    private void UpdateResponsiveLayout()
    {
        _messageLabel.MaximumSize = new Size(UiScaleHelper.MeasureWrapWidth(_messageCard.ClientSize.Width, 36, minWidth: 420), 0);
    }
}
