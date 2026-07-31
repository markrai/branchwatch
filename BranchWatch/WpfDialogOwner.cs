using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace BranchWatch;

internal sealed class WpfDialogOwner : Forms.IWin32Window
{
    public WpfDialogOwner(Window window)
    {
        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        Handle = helper.Handle;
    }

    public IntPtr Handle { get; }
}
