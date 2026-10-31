using System.Runtime.Versioning;
using Clipo.Services.Native;

namespace Clipo.Services;

/// <summary>The registered clipboard formats Clipo reads and writes, by id.</summary>
[SupportedOSPlatform("windows")]
internal static class ClipboardFormats
{
    public static readonly uint Rtf = NativeMethods.RegisterClipboardFormat("Rich Text Format");
    public static readonly uint Html = NativeMethods.RegisterClipboardFormat("HTML Format");
    public static readonly uint Png = NativeMethods.RegisterClipboardFormat("PNG");
    public static readonly uint CanIncludeInHistory = NativeMethods.RegisterClipboardFormat(ConcealedFormats.CanIncludeInHistory);

    /// <summary>Tells Explorer whether pasted files are copied or moved.</summary>
    public static readonly uint PreferredDropEffect = NativeMethods.RegisterClipboardFormat("Preferred DropEffect");
}
