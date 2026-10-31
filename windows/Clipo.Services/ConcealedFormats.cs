namespace Clipo.Services;

/// <summary>
/// Clipboard formats that apps add to say "do not keep this", used by
/// password managers. Data with any of them is not even read.
/// </summary>
public static class ConcealedFormats
{
    /// <summary>Documented by Microsoft for clipboard history; also honored by clipboard managers in general. Presence alone means "exclude".</summary>
    public const string ExcludeFromMonitoring = "ExcludeClipboardContentFromMonitorProcessing";

    /// <summary>A DWORD that is 0 when the data must stay out of Windows' clipboard history.</summary>
    public const string CanIncludeInHistory = "CanIncludeInClipboardHistory";

    private static readonly HashSet<string> s_names = new(StringComparer.OrdinalIgnoreCase)
    {
        ExcludeFromMonitoring,
        // Older, used by KeePass and others.
        "Clipboard Viewer Ignore",
    };

    /// <summary>Whether any of <paramref name="formatNames"/> marks the data as one to forget.</summary>
    public static bool IsConcealed(IEnumerable<string> formatNames)
    {
        ArgumentNullException.ThrowIfNull(formatNames);
        return formatNames.Any(s_names.Contains);
    }

    /// <summary>Whether the value of <see cref="CanIncludeInHistory"/> opts out of history.</summary>
    public static bool OptsOutOfHistory(ReadOnlySpan<byte> value) =>
        value.Length >= 4 && BitConverter.ToUInt32(value) == 0;
}
