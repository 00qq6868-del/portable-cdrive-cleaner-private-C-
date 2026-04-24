namespace PortableCDriveCleaner.Forms;

public sealed class ScrollFriendlyPanel : Panel, IHorizontalWheelTarget
{
    private const int WmMouseHWheel = 0x020E;

    public ScrollFriendlyPanel()
    {
        AutoScroll = true;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
        ControlAdded += (_, e) =>
        {
            if (e.Control is not null)
            {
                HookRecursive(e.Control);
            }
        };
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        foreach (Control child in Controls)
        {
            HookRecursive(child);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Focus();
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
        if ((ModifierKeys & Keys.Shift) == Keys.Shift)
        {
            if (!TryHandleHorizontalWheel(e.Delta))
            {
                base.OnMouseWheel(e);
            }

            return;
        }

        if (!TryScrollByWheel(e.Delta))
        {
            base.OnMouseWheel(e);
        }
    }

    public bool TryScrollByWheel(int delta)
    {
        if (!VerticalScroll.Visible)
        {
            return false;
        }

        var step = Math.Max(24, SystemInformation.MouseWheelScrollLines * 28);
        var current = -AutoScrollPosition.Y;
        var max = Math.Max(0, VerticalScroll.Maximum - VerticalScroll.LargeChange + 1);
        var next = delta > 0 ? current - step : current + step;
        next = Math.Clamp(next, 0, max);
        if (next == current)
        {
            return false;
        }

        AutoScrollPosition = new Point(0, next);
        return true;
    }

    public bool TryScrollHorizontalByWheel(int delta)
    {
        if (!HorizontalScroll.Visible)
        {
            return false;
        }

        var step = Math.Max(36, SystemInformation.MouseWheelScrollLines * 28);
        var currentX = -AutoScrollPosition.X;
        var currentY = -AutoScrollPosition.Y;
        var max = Math.Max(0, HorizontalScroll.Maximum - HorizontalScroll.LargeChange + 1);
        var next = delta > 0 ? currentX + step : currentX - step;
        next = Math.Clamp(next, 0, max);
        if (next == currentX)
        {
            return false;
        }

        AutoScrollPosition = new Point(next, currentY);
        return true;
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

    private void HookRecursive(Control control)
    {
        if (ReferenceEquals(control, this))
        {
            return;
        }

        control.MouseEnter -= ChildMouseEnter;
        control.MouseEnter += ChildMouseEnter;
        control.MouseMove -= ChildMouseMove;
        control.MouseMove += ChildMouseMove;
        control.MouseWheel -= ChildMouseWheel;
        control.MouseWheel += ChildMouseWheel;
        control.ControlAdded -= ChildControlAdded;
        control.ControlAdded += ChildControlAdded;

        foreach (Control child in control.Controls)
        {
            HookRecursive(child);
        }
    }

    private void ChildControlAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control is not null)
        {
            HookRecursive(e.Control);
        }
    }

    private void ChildMouseEnter(object? sender, EventArgs e)
    {
        Focus();
    }

    private void ChildMouseMove(object? sender, MouseEventArgs e)
    {
        if (!Focused)
        {
            Focus();
        }
    }

    private void ChildMouseWheel(object? sender, MouseEventArgs e)
    {
        if (sender is NumericUpDown or ComboBox)
        {
            return;
        }

        if ((ModifierKeys & Keys.Shift) == Keys.Shift)
        {
            if (!TryHandleHorizontalWheel(e.Delta))
            {
                OnMouseWheel(e);
            }

            return;
        }

        if (!TryScrollByWheel(e.Delta))
        {
            OnMouseWheel(e);
        }
    }

    public bool TryHandleHorizontalWheel(int delta)
    {
        return TryScrollHorizontalByWheel(delta);
    }
}
