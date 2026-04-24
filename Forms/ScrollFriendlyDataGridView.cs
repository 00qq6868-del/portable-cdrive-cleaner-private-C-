namespace PortableCDriveCleaner.Forms;

public sealed class ScrollFriendlyDataGridView : DataGridView, IHorizontalWheelTarget
{
    private const int WmMouseHWheel = 0x020E;

    public ScrollFriendlyDataGridView()
    {
        DoubleBuffered = true;
        ScrollBars = ScrollBars.Both;
        StandardTab = true;
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

        if (RowCount == 0 || !VerticalScrollBar.Visible || ModifierKeys != Keys.None)
        {
            base.OnMouseWheel(e);
            return;
        }

        var currentIndex = FirstDisplayedScrollingRowIndex;
        if (currentIndex < 0)
        {
            base.OnMouseWheel(e);
            return;
        }

        base.OnMouseWheel(e);
        if (FirstDisplayedScrollingRowIndex != currentIndex)
        {
            return;
        }

        var step = Math.Max(1, SystemInformation.MouseWheelScrollLines);
        var nextIndex = e.Delta > 0 ? currentIndex - step : currentIndex + step;
        nextIndex = Math.Clamp(nextIndex, 0, RowCount - 1);

        try
        {
            FirstDisplayedScrollingRowIndex = nextIndex;
        }
        catch
        {
            base.OnMouseWheel(e);
        }
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
        if (!HorizontalScrollBar.Visible)
        {
            return false;
        }

        var step = Math.Max(48, SystemInformation.MouseWheelScrollLines * 32);
        var current = HorizontalScrollingOffset;
        var max = Math.Max(0, Columns.GetColumnsWidth(DataGridViewElementStates.Visible) - ClientSize.Width);
        var next = delta > 0 ? current + step : current - step;
        next = Math.Clamp(next, 0, max);
        if (next == current)
        {
            return false;
        }

        HorizontalScrollingOffset = next;
        return true;
    }
}
