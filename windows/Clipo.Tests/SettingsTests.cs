using Clipo.Core;
using Clipo.Services;

namespace Clipo.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("clipo-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void DefaultsFollowDecision0004()
    {
        var settings = new AppSettings();

        Assert.False(settings.AutoPaste);
        Assert.False(settings.PrivateMode);
        Assert.Equal(200, settings.MaxItems);
        Assert.Null(settings.AutoClearAfter);
        Assert.Equal(HotkeyChord.ShowHistory, HotkeyChoices.Chord(settings.Hotkey));
        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Equal(AppLanguage.System, settings.Language);
    }

    [Fact]
    public void DefaultItemLimitIsTheCoresDefault()
    {
        var ready = Assert.IsType<OpenResult.Ready>(History.Open(History.DatabasePath(_folder), new MemoryKeyStore()));
        using var store = ready.Store;

        Assert.Equal((ulong)AppSettings.DefaultMaxItems, store.Limits().MaxItems);
    }

    [Fact]
    public void EveryChoiceOfferedIsInRange()
    {
        Assert.Contains(AppSettings.DefaultMaxItems, AppSettings.MaxItemsChoices);
        Assert.Contains(0, AppSettings.AutoClearDaysChoices);
    }

    [Fact]
    public void SettingsRoundTripThroughTheFile()
    {
        var store = new AppSettingsStore(Path.Combine(_folder, "settings.json"));
        var settings = new AppSettings
        {
            AutoPaste = true,
            Theme = AppTheme.Dark,
            Language = AppLanguage.Farsi,
            Hotkey = HotkeyChoice.ControlAlt,
            MaxItems = 500,
            AutoClearDays = 7,
            PrivateMode = true,
            IgnoredApps = ["Secrets.exe"],
        };

        Assert.True(store.Save(settings));

        Assert.Equal(settings, store.Load());
    }

    [Fact]
    public void FilesFromStep33StillLoad()
    {
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, """{ "autoPaste": true }""");

        Assert.Equal(new AppSettings { AutoPaste = true }, new AppSettingsStore(path).Load());
    }

    [Fact]
    public void ValuesOutOfRangeAreBroughtBack()
    {
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, """{ "maxItems": 0, "autoClearDays": -5, "hotkey": 42, "ignoredApps": ["", "keepass", "NOTES.exe", "notes.EXE"] }""");

        var settings = new AppSettingsStore(path).Load();

        Assert.Equal(1, settings.MaxItems);
        Assert.Equal(0, settings.AutoClearDays);
        Assert.Equal(HotkeyChoice.ControlShift, settings.Hotkey);
        // KeePass.exe is in the built-in list already; the repeated Notes.exe is dropped.
        Assert.Equal(["NOTES.exe"], settings.IgnoredApps);
    }

    [Fact]
    public void AnUnknownNameInTheFileMeansDefaults()
    {
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, """{ "theme": "sepia" }""");

        Assert.Equal(new AppSettings(), new AppSettingsStore(path).Load());
    }

    [Fact]
    public void SettingsWithTheSameListCompareEqual()
    {
        var one = new AppSettings { IgnoredApps = ["A.exe", "B.exe"] };
        var same = new AppSettings { IgnoredApps = ["A.exe", "B.exe"] };

        Assert.Equal(one, same);
        Assert.Equal(one.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(one, new AppSettings { IgnoredApps = ["A.exe"] });
    }

    [Fact]
    public void OnlyRetentionSettingsNeedTheHistoryUpdated()
    {
        var settings = new AppSettings { IgnoredApps = ["Vault.exe"] };

        Assert.True(settings.HasSameRetentionAs(settings with { Theme = AppTheme.Dark, Hotkey = HotkeyChoice.AltShift, AutoPaste = true }));
        Assert.True(settings.HasSameRetentionAs(settings with { IgnoredApps = ["Vault.exe"] }));
        Assert.False(settings.HasSameRetentionAs(settings with { MaxItems = 50 }));
        Assert.False(settings.HasSameRetentionAs(settings with { AutoClearDays = 1 }));
        Assert.False(settings.HasSameRetentionAs(settings with { PrivateMode = true }));
        Assert.False(settings.HasSameRetentionAs(settings with { IgnoredApps = [] }));
    }

    [Theory]
    [InlineData("keepass", "keepass.exe")]
    [InlineData("  Notes.exe ", "Notes.exe")]
    [InlineData(@"C:\Program Files\Tools\Vault.exe", "Vault.exe")]
    [InlineData("\"C:\\Tools\\Vault.exe\"", "Vault.exe")]
    [InlineData("tools/vault", "vault.exe")]
    public void IgnoredAppsAreKeptAsFileNames(string typed, string expected)
    {
        Assert.Equal(expected, IgnoredAppList.Normalize(typed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\Tools\")]
    public void NothingTypedAddsNothing(string? typed)
    {
        Assert.Null(IgnoredAppList.Normalize(typed));
        Assert.Empty(IgnoredAppList.Add([], typed));
    }

    [Fact]
    public void AddingAnAppKnownAlreadyChangesNothing()
    {
        IReadOnlyList<string> own = ["Vault.exe"];

        Assert.Equal(own, IgnoredAppList.Add(own, "vault"));
        Assert.Equal(own, IgnoredAppList.Add(own, "keepassxc.exe"));
        Assert.Equal(["Vault.exe", "Other.exe"], IgnoredAppList.Add(own, "Other"));
    }

    [Fact]
    public void RemovingIgnoresCase()
    {
        Assert.Equal(["B.exe"], IgnoredAppList.Remove(["A.exe", "B.exe"], "a.EXE"));
    }

    [Fact]
    public void RulesAddTheUsersAppsToTheBuiltInList()
    {
        var rules = CaptureSettings.Rules(new AppSettings { PrivateMode = true, AutoClearDays = 30, IgnoredApps = ["Vault.exe"] });

        Assert.True(rules.PrivateMode);
        Assert.Equal(TimeSpan.FromDays(30), rules.AutoClearAfter);
        Assert.Contains("KeePassXC.exe", rules.IgnoredApps);
        Assert.Contains("Vault.exe", rules.IgnoredApps);
    }

    [Fact]
    public void ReadingIsRefusedForAnAppTheUserAdded()
    {
        var rules = CaptureSettings.Rules(new AppSettings { IgnoredApps = ["Vault.exe"] });

        Assert.False(CaptureSettings.AllowsReading(new SourceApp("vault.exe"), rules));
        Assert.True(CaptureSettings.AllowsReading(new SourceApp("Notepad.exe"), rules));
    }

    [Fact]
    public void LoweringTheLimitTrimsTheHistory()
    {
        using var store = OpenStore();
        for (var i = 0; i < 5; i++)
        {
            store.Record(Copy($"item {i}", DateTime.UtcNow));
        }

        Assert.True(HistorySettings.Apply(store, new AppSettings { MaxItems = 3 }, DateTime.UtcNow));

        Assert.Equal(3ul, store.Count());
        Assert.Equal(3ul, store.Limits().MaxItems);
    }

    [Fact]
    public void ApplyDeletesExpiredUnpinnedItemsOnly()
    {
        using var store = OpenStore();
        var now = DateTime.UtcNow;
        store.Record(Copy("old", now.AddDays(-10)));
        store.Record(Copy("older but pinned", now.AddDays(-20)));
        store.Record(Copy("new", now));
        store.SetPinned(store.Search(new Query(Category.All, "pinned")).Single().Id, true);

        Assert.True(HistorySettings.Apply(store, new AppSettings { AutoClearDays = 7 }, now));

        Assert.Equal(2ul, store.Count());
    }

    [Fact]
    public void ApplyHandsTheRulesToTheCore()
    {
        using var store = OpenStore();

        HistorySettings.Apply(store, new AppSettings { PrivateMode = true, IgnoredApps = ["Vault.exe"] }, DateTime.UtcNow);

        Assert.True(store.Rules().PrivateMode);
        Assert.Contains("Vault.exe", store.Rules().IgnoredApps);
    }

    [Fact]
    public void ApplyReportsAClosedHistory()
    {
        var store = OpenStore();
        store.Dispose();

        Assert.False(HistorySettings.Apply(store, new AppSettings(), DateTime.UtcNow));
    }

    [Fact]
    public void StartTabDefaultsToAllAndIsSavedAndLoaded()
    {
        var store = new AppSettingsStore(Path.Combine(_folder, "settings.json"));
        Assert.Equal(StartTab.All, store.Load().StartTab);

        Assert.True(store.Save(new AppSettings { StartTab = StartTab.LastUsed }));

        Assert.Equal(StartTab.LastUsed, store.Load().StartTab);
    }

    [Fact]
    public void UnknownStartTabInAHandEditedFileBecomesAll()
    {
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, """{"StartTab": 42}""");

        Assert.Equal(StartTab.All, new AppSettingsStore(path).Load().StartTab);
    }

    [Theory]
    [InlineData(StartTab.All, Category.Images, Category.All)]
    [InlineData(StartTab.Text, Category.Images, Category.Text)]
    [InlineData(StartTab.Images, Category.All, Category.Images)]
    [InlineData(StartTab.Files, Category.All, Category.Files)]
    [InlineData(StartTab.Pinned, Category.All, Category.Pinned)]
    [InlineData(StartTab.LastUsed, Category.Files, Category.Files)]
    [InlineData(StartTab.LastUsed, Category.All, Category.All)]
    public void StartCategoryFollowsTheChoice(StartTab choice, Category lastUsed, Category expected) =>
        Assert.Equal(expected, new AppSettings { StartTab = choice }.StartCategory(lastUsed));

    [Fact]
    public void StartTabDoesNotChangeTheRetention()
    {
        Assert.True(new AppSettings().HasSameRetentionAs(new AppSettings { StartTab = StartTab.Pinned }));
        Assert.NotEqual(new AppSettings(), new AppSettings { StartTab = StartTab.Pinned });
    }

    [Fact]
    public void EveryHotkeyChoiceIsADifferentChordOnTheVKey()
    {
        var chords = Enum.GetValues<HotkeyChoice>().Select(HotkeyChoices.Chord).ToList();

        Assert.Equal(chords.Count, chords.Distinct().Count());
        Assert.All(chords, chord => Assert.Equal((uint)'V', chord.VirtualKey));
        Assert.Equal(
            ["Ctrl+Shift+V", "Ctrl+Alt+V", "Alt+Shift+V", "Ctrl+Alt+Shift+V"],
            Enum.GetValues<HotkeyChoice>().Select(choice => HotkeyChoices.Chord(choice).Display));
    }

    [Theory]
    [InlineData(AppLanguage.System, null)]
    [InlineData(AppLanguage.English, "en-US")]
    [InlineData(AppLanguage.Farsi, "fa")]
    public void LanguagesMapToTheirResourceFolders(AppLanguage language, string? tag)
    {
        Assert.Equal(tag, UiLanguage.Tag(language));
        Assert.Equal(tag is null, UiLanguage.Culture(language) is null);
        if (tag is not null)
        {
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Strings", tag, "Resources.resw")));
        }
    }

    [Fact]
    public void FarsiIsRightToLeftAndEnglishIsNot()
    {
        Assert.True(UiLanguage.Culture(AppLanguage.Farsi)!.TextInfo.IsRightToLeft);
        Assert.False(UiLanguage.Culture(AppLanguage.English)!.TextInfo.IsRightToLeft);
    }

    private ClipStore OpenStore() =>
        Assert.IsType<OpenResult.Ready>(History.Open(History.DatabasePath(_folder), new MemoryKeyStore())).Store;

    private static Capture Copy(string text, DateTime at) => new(new ItemContent.Text(new TextContent(text)), at);
}

public sealed class LoginItemTests
{
    private const string Exe = @"C:\Apps\Clipo\Clipo.exe";

    [Fact]
    public void StartsOffAndSavesAQuotedCommand()
    {
        var registry = new MemoryStartupRegistry();
        var item = new LoginItem(registry, Exe);
        Assert.False(item.IsOn);

        Assert.True(item.SetOn(true));

        Assert.True(item.IsOn);
        Assert.Equal("\"C:\\Apps\\Clipo\\Clipo.exe\" --background", registry.Commands[LoginItem.ValueName]);
    }

    [Fact]
    public void TurningItOffRemovesTheCommand()
    {
        var registry = new MemoryStartupRegistry();
        var item = new LoginItem(registry, Exe);
        item.SetOn(true);

        Assert.True(item.SetOn(false));

        Assert.False(item.IsOn);
        Assert.Empty(registry.Commands);
    }

    [Fact]
    public void ASwitchOffInWindowsNeedsTheUsersApproval()
    {
        var registry = new MemoryStartupRegistry();
        var item = new LoginItem(registry, Exe);
        item.SetOn(true);
        registry.Disabled.Add(LoginItem.ValueName);

        Assert.True(item.IsOn);
        Assert.True(item.NeedsApproval);
    }

    [Fact]
    public void ChoosingItAgainInClipoClearsTheSwitchOff()
    {
        var registry = new MemoryStartupRegistry();
        var item = new LoginItem(registry, Exe);
        item.SetOn(true);
        registry.Disabled.Add(LoginItem.ValueName);

        item.SetOn(true);

        Assert.False(item.NeedsApproval);
    }

    [Fact]
    public void NotOnIsNeverNeedingApproval()
    {
        var registry = new MemoryStartupRegistry();
        registry.Disabled.Add(LoginItem.ValueName);

        Assert.False(new LoginItem(registry, Exe).NeedsApproval);
    }

    [Fact]
    public void RepairPointsAnOldEntryAtThisCopyAndKeepsTheSwitchOff()
    {
        var registry = new MemoryStartupRegistry();
        new LoginItem(registry, @"D:\Old\Clipo.exe").SetOn(true);
        registry.Disabled.Add(LoginItem.ValueName);
        var item = new LoginItem(registry, Exe);

        item.Repair();

        Assert.Equal(item.Command, registry.Commands[LoginItem.ValueName]);
        Assert.True(item.NeedsApproval);
    }

    [Fact]
    public void RepairDoesNotTurnItOn()
    {
        var registry = new MemoryStartupRegistry();

        new LoginItem(registry, Exe).Repair();

        Assert.Empty(registry.Commands);
    }

    [Fact]
    public void ARegistryThatRefusesIsReportedNotThrown()
    {
        var item = new LoginItem(new MemoryStartupRegistry { Refuse = true }, Exe);

        Assert.False(item.SetOn(true));
        Assert.False(item.SetOn(false));
    }
}

/// <summary>In-memory stand-in for the <c>Run</c> key.</summary>
internal sealed class MemoryStartupRegistry : IStartupRegistry
{
    public Dictionary<string, string> Commands { get; } = [];

    public HashSet<string> Disabled { get; } = [];

    public bool Refuse { get; init; }

    public string? GetCommand(string name) => Commands.GetValueOrDefault(name);

    public void SetCommand(string name, string command)
    {
        ThrowIfRefusing();
        Commands[name] = command;
    }

    public void Remove(string name)
    {
        ThrowIfRefusing();
        Commands.Remove(name);
    }

    public bool IsDisabledByUser(string name) => Disabled.Contains(name);

    public void ClearDisabledByUser(string name) => Disabled.Remove(name);

    private void ThrowIfRefusing()
    {
        if (Refuse)
        {
            throw new UnauthorizedAccessException();
        }
    }
}
