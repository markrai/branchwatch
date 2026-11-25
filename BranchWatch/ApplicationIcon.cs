using System.Drawing;
using System.IO;
using Forms = System.Windows.Forms;

namespace BranchWatch;

internal static class ApplicationIcon
{
    internal const string ResourceUri = "/BranchWatch;component/Assets/BranchWatch.ico";

    internal static Stream OpenResourceStream()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri(ResourceUri, UriKind.Relative))
            ?? throw new FileNotFoundException("The BranchWatch icon resource is missing.");

        return resource.Stream;
    }

    internal static Icon LoadTrayIcon()
    {
        using var stream = OpenResourceStream();
        using var icon = new Icon(stream, Forms.SystemInformation.SmallIconSize);
        return (Icon)icon.Clone();
    }
}
