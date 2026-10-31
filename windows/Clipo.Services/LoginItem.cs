using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Clipo.Services;

/// <summary>Where Windows keeps the programs it starts at sign-in.</summary>
public interface IStartupRegistry
{
    /// <summary>The command stored under <paramref name="name"/>, or <c>null</c> if there is none.</summary>
    string? GetCommand(string name);

    void SetCommand(string name, string command);

    void Remove(string name);

    /// <summary>Whether the user turned <paramref name="name"/> off in Task Manager or Windows Settings.</summary>
    bool IsDisabledByUser(string name);

    /// <summary>Forgets that the user switched <paramref name="name"/> off, so it starts again.</summary>
    void ClearDisabledByUser(string name);
}

/// <summary>
/// Whether Windows starts Clipo when the user signs in. An unpackaged app
/// does it with a value in the per-user <c>Run</c> key, which needs no
/// administrator rights. The user can still switch it off in Task Manager or
/// Settings; Windows keeps that choice apart from the value (the
/// <c>StartupApproved</c> key), so Clipo can tell and say so.
/// </summary>
/// <param name="registry">Where the value lives.</param>
/// <param name="executablePath">Clipo's own executable.</param>
public sealed class LoginItem(IStartupRegistry registry, string executablePath)
{
    /// <summary>The name of the value.</summary>
    public const string ValueName = "Clipo";

    /// <summary>
    /// Given to the copy that Windows starts: it opens the history without
    /// showing the status window, which would be in the way at every sign-in.
    /// </summary>
    public const string BackgroundArgument = "--background";

    /// <summary>The command Windows runs.</summary>
    public string Command => $"\"{executablePath}\" {BackgroundArgument}";

    /// <summary>Whether Clipo is set to start at sign-in, even if the user has since switched it off in Windows.</summary>
    public bool IsOn => registry.GetCommand(ValueName) is not null;

    /// <summary>On, but switched off by the user in Windows: only they can turn it back on.</summary>
    public bool NeedsApproval => IsOn && registry.IsDisabledByUser(ValueName);

    /// <summary>Adds or removes Clipo from the programs started at sign-in. Returns whether it worked.</summary>
    public bool SetOn(bool wanted)
    {
        try
        {
            if (wanted)
            {
                registry.SetCommand(ValueName, Command);
                // Choosing it again in Clipo's own settings is a switch-on.
                registry.ClearDisabledByUser(ValueName);
            }
            else
            {
                registry.Remove(ValueName);
            }
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
        {
            Trace.TraceError($"changing the login item failed: {e.GetType().Name}");
            return false;
        }
    }

    /// <summary>Points an existing entry at this copy of Clipo, which matters after the folder was moved or the app updated.</summary>
    public void Repair()
    {
        if (!IsOn || registry.GetCommand(ValueName) == Command)
        {
            return;
        }
        try
        {
            // Not SetOn: a switch-off the user made in Windows stays.
            registry.SetCommand(ValueName, Command);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
        {
            Trace.TraceError($"repairing the login item failed: {e.GetType().Name}");
        }
    }
}

/// <summary>The current user's <c>Run</c> key.</summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryStartup : IStartupRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public string? GetCommand(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(name) as string;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
        {
            return null;
        }
    }

    public void SetCommand(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(name, command, RegistryValueKind.String);
    }

    public void Remove(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void ClearDisabledByUser(string name)
    {
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <summary>The first byte of the <c>StartupApproved</c> value is even when enabled and odd when disabled.</summary>
    public bool IsDisabledByUser(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return key?.GetValue(name) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
        {
            return false;
        }
    }
}
