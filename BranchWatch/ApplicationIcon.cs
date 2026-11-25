using System.Drawing;
using System.IO;
using Forms = System.Windows.Forms;

namespace BranchWatch;

internal static class ApplicationIcon
{
    internal const string ResourceUri = "pack://application:,,,/Assets/BranchWatch.ico";

    internal static Icon LoadTrayIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri(ResourceUri, UriKind.Absolute))
            ?? throw new FileNotFoundException("The BranchWatch icon resource is missing.");

        using var stream = resource.Stream;
        using var icon = new Icon(stream, Forms.SystemInformation.SmallIconSize);
        return (Icon)icon.Clone();
    }
}
