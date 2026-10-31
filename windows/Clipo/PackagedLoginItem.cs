using System.Diagnostics;
using System.Runtime.InteropServices;
using Clipo.Services;
using Windows.ApplicationModel;

namespace Clipo;

/// <summary>
/// Starting at sign-in for the MSIX build. A packaged app's registry writes
/// are private to the package, so the <c>Run</c> key does nothing; the
/// package declares a startup task instead (<c>Package.appxmanifest</c>),
/// which Windows lists under Settings &gt; Apps &gt; Startup. Windows keeps
/// the state, including the user's switch-off, so there is nothing to repair.
/// </summary>
internal sealed class PackagedLoginItem : ILoginItem
{
    public bool IsOn => State() is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    public bool NeedsApproval => State() is StartupTaskState.DisabledByUser or StartupTaskState.DisabledByPolicy;

    public bool SetOn(bool wanted)
    {
        try
        {
            // The calls complete on other threads; waiting off the UI thread cannot deadlock.
            return Task.Run(() => ChangeAsync(wanted)).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is InvalidOperationException or COMException)
        {
            Trace.TraceError($"changing the startup task failed: {e.GetType().Name}");
            return false;
        }
    }

    public void Repair()
    {
    }

    private static async Task<bool> ChangeAsync(bool wanted)
    {
        var task = await StartupTask.GetAsync(LoginItem.StartupTaskId);
        if (!wanted)
        {
            task.Disable();
            return true;
        }
        // A switch-off in Windows stays: the state is then DisabledByUser and Settings says so.
        var state = await task.RequestEnableAsync();
        return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy
            or StartupTaskState.DisabledByUser or StartupTaskState.DisabledByPolicy;
    }

    private static StartupTaskState State()
    {
        try
        {
            return Task.Run(async () => (await StartupTask.GetAsync(LoginItem.StartupTaskId)).State).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is InvalidOperationException or COMException)
        {
            Trace.TraceError($"reading the startup task failed: {e.GetType().Name}");
            return StartupTaskState.Disabled;
        }
    }
}
