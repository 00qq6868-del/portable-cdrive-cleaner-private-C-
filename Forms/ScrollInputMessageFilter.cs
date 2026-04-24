namespace PortableCDriveCleaner.Forms;

internal sealed class ScrollInputMessageFilter : IMessageFilter
{
    private const int WmMouseHWheel = 0x020E;

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WmMouseHWheel)
        {
            return false;
        }

        var control = ResolveControl(m.HWnd);
        if (control is null)
        {
            return false;
        }

        var delta = (short)((m.WParam.ToInt64() >> 16) & 0xffff);
        return TryDispatch(control, delta);
    }

    private static Control? ResolveControl(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        return Control.FromChildHandle(handle) ?? Control.FromHandle(handle);
    }

    private static bool TryDispatch(Control? control, int delta)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (current is IHorizontalWheelTarget target && target.TryHandleHorizontalWheel(delta))
            {
                return true;
            }
        }

        return false;
    }
}
