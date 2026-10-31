using System.Xml.Linq;
using Clipo.Services;

namespace Clipo.Tests;

/// <summary>Checks the MSIX manifest against the code and the files it names.</summary>
public sealed class PackageManifestTests
{
    private static readonly XNamespace s_package = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace s_desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";
    private static readonly XNamespace s_uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";

    private static XElement Manifest() =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Package.appxmanifest")).Root!;

    [Fact]
    public void VersionHasFourNumbersAndEndsInZero()
    {
        var version = (string)Manifest().Element(s_package + "Identity")!.Attribute("Version")!;

        Assert.Matches(@"^\d+\.\d+\.\d+\.0$", version);
    }

    [Fact]
    public void StartupTaskIsTheOneTheAppAsksFor()
    {
        var task = Manifest().Descendants(s_desktop + "StartupTask").Single();

        Assert.Equal(LoginItem.StartupTaskId, (string)task.Attribute("TaskId")!);
        // Off until the user chooses it in Clipo's settings.
        Assert.Equal("false", (string)task.Attribute("Enabled")!);
    }

    [Fact]
    public void LanguagesAreTheStringFolders()
    {
        var declared = Manifest().Element(s_package + "Resources")!.Elements().Select(e => (string)e.Attribute("Language")!);
        var folders = Directory.GetDirectories(Path.Combine(AppContext.BaseDirectory, "Strings")).Select(Path.GetFileName);

        Assert.Equal(folders.Order(StringComparer.OrdinalIgnoreCase), declared.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void LogosExist()
    {
        var root = Manifest();
        var visual = root.Descendants(s_uap + "VisualElements").Single();
        var logos = new[]
        {
            (string)root.Element(s_package + "Properties")!.Element(s_package + "Logo")!,
            (string)visual.Attribute("Square150x150Logo")!,
            (string)visual.Attribute("Square44x44Logo")!,
        };

        foreach (var logo in logos)
        {
            var path = Path.Combine(AppContext.BaseDirectory, logo.Replace('\\', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"{logo} is missing");
        }
    }
}
