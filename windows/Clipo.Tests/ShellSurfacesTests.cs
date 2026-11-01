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

    private const uint Own = 42;

    [Fact]
    public void AnotherAppsShownWindowCounts() =>
        Assert.True(ShellSurfaces.CountsAsApp(7, Own, "Notepad", visible: true, extendedStyle: 0));

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    public void TheDesktopCountsEvenAsAToolWindow(string className) =>
        Assert.True(ShellSurfaces.CountsAsApp(7, Own, className, visible: true, extendedStyle: 0x80));

    [Theory]
    // Clipo's own windows, such as the tray menu's hidden owner.
    [InlineData(Own, "io.github.arash7l3.Clipo.MessageWindow", true, 0x80)]
    [InlineData(Own, "WinUIDesktopWin32WindowClass", true, 0)]
    // The taskbar and the hidden-icons flyout, as seen on Windows Server 2025.
    [InlineData(7u, "Shell_TrayWnd", true, 0x80)]
    [InlineData(7u, "TopLevelWindowForOverflowXamlIsland", true, 0x80)]
    // A shell flyout whose class is not listed, caught by its tool-window style.
    [InlineData(7u, "SomeNewShellFlyout", true, 0x80)]
    // A hidden window cannot take a paste.
    [InlineData(7u, "Notepad", false, 0)]
    [InlineData(0u, "Notepad", true, 0)]
    public void ShellClipoAndHiddenWindowsDoNotCount(uint processId, string className, bool visible, long extendedStyle) =>
        Assert.False(ShellSurfaces.CountsAsApp(processId, Own, className, visible, extendedStyle));

    [Theory]
    [InlineData("CabinetWClass", false)]
    [InlineData("Progman", false)]
    [InlineData("WorkerW", false)]
    // An Open or Save dialog, which holds a folder view.
    [InlineData("#32770", true)]
    public void ExplorerTheDesktopAndFolderViewsShowFolders(string className, bool holdsFolderView) =>
        Assert.True(ShellSurfaces.ShowsFolder(className, holdsFolderView));

    [Theory]
    [InlineData("Notepad")]
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("#32770")]
    [InlineData(null)]
    public void OtherWindowsDoNot(string? className) => Assert.False(ShellSurfaces.ShowsFolder(className, holdsFolderView: false));
}
