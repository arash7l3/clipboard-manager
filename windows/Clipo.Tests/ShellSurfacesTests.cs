using Clipo.Services;

namespace Clipo.Tests;

public sealed class ShellSurfacesTests
{
    [Theory]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("NotifyIconOverflowWindow")]
    [InlineData("TopLevelWindowForOverflowXamlIsland")]
    public void TheTaskbarAndItsFlyoutsAreNotApps(string className) => Assert.True(ShellSurfaces.Contains(className));

    [Theory]
    [InlineData("Notepad")]
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("ApplicationFrameWindow")]
    [InlineData("shell_traywnd")]
    [InlineData("")]
    [InlineData(null)]
    public void AppWindowsAreKept(string? className) => Assert.False(ShellSurfaces.Contains(className));
}
