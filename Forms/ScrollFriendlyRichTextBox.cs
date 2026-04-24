using System.Runtime.InteropServices;

namespace PortableCDriveCleaner.Forms;

public sealed class ScrollFriendlyRichTextBox : RichTextBox, IHorizontalWheelTarget
{
    private const int WmMouseHWheel = 0x020E;
    private const int EmLineScroll = 0x00B6;

    public ScrollFriendlyRichTextBox()
    {
        BorderStyle = BorderStyle.None;
        DetectUrls = false;
        ReadOnly = true;
        ScrollBars = RichTextBoxScrollBars.Both;
        TabStop = false;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (!Focused)
        {
            Focus();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Focused)
        {
            Focus();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Shift) == Keys.Shift && TryHandleHorizontalWheel(e.Delta))
        {
            return;
        }

        base.OnMouseWheel(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmMouseHWheel)
        {
            var delta = (short)((m.WParam.ToInt64() >> 16) & 0xffff);
            if (TryHandleHorizontalWheel(delta))
            {
                return;
            }
        }

        base.WndProc(ref m);
    }

    public bool TryHandleHorizontalWheel(int delta)
    {
        if (WordWrap || string.IsNullOrWhiteSpace(Text))
        {
            return false;
        }

        var charStep = Math.Max(3, SystemInformation.MouseWheelScrollLines * 2);
        var horizontalDelta = delta > 0 ? -charStep : charStep;
        SendMessage(Handle, EmLineScroll, (IntPtr)horizontalDelta, IntPtr.Zero);
        return true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
