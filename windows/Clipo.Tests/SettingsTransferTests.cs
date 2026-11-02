using Clipo.Core;
using Clipo.Services;

namespace Clipo.Tests;

public sealed class SettingsTransferTests
{
    private static readonly AppSettings s_custom = new()
    {
        AutoPaste = true,
        Theme = AppTheme.Dark,
        Language = AppLanguage.Farsi,
        Hotkey = HotkeyChoice.ControlAlt,
        StartTab = StartTab.LastUsed,
        MaxItems = 500,
        AutoClearDays = 7,
        PrivateMode = true,
        IgnoredApps = ["Secrets.exe", "Vault.exe"],
    };

    [Fact]
    public void EverySettingRoundTrips()
    {
        var exported = SettingsTransfer.Export(s_custom);

        Assert.Equal(s_custom, SettingsTransfer.Import(new AppSettings(), exported));
        Assert.Equal(new AppSettings(), SettingsTransfer.Import(s_custom, SettingsTransfer.Export(new AppSettings())));
    }

    [Fact]
    public void SharedKeysUseTheMacValues()
    {
        var exported = SettingsTransfer.Export(s_custom).ToDictionary(s => s.Key, s => s.Value);

        Assert.Equal("dark", exported[SettingsTransfer.AppearanceKey]);
        Assert.Equal("fa", exported[SettingsTransfer.LanguageKey]);
        Assert.Equal("lastUsed", exported[SettingsTransfer.StartTabKey]);
        Assert.Equal("500", exported[SettingsTransfer.MaxItemsKey]);
        Assert.Equal("true", exported[SettingsTransfer.PrivateModeKey]);
    }

    [Fact]
    public void AMacExportKeepsTheWindowsOnlySettings()
    {
        Setting[] fromMac =
        [
            new("interface.language", "en"),
            new("macos.hotkey", "commandOption"),
            new("macos.ignoredApps", "com.example.vault"),
        ];

        var next = SettingsTransfer.Import(s_custom, fromMac);

        Assert.Equal(s_custom with { Language = AppLanguage.English }, next);
    }

    [Fact]
    public void UnreadableValuesAndUnknownKeysChangeNothing()
    {
        Setting[] odd =
        [
            new(SettingsTransfer.MaxItemsKey, "lots"),
            new(SettingsTransfer.PrivateModeKey, "yes"),
            new(SettingsTransfer.AppearanceKey, "sepia"),
            new(SettingsTransfer.HotkeyKey, "42"),
            new("future.setting", "1"),
        ];

        Assert.Equal(s_custom, SettingsTransfer.Import(s_custom, odd));
    }

    [Fact]
    public void ImportedValuesAreBroughtIntoRange()
    {
        Setting[] wild =
        [
            new(SettingsTransfer.MaxItemsKey, "0"),
            new(SettingsTransfer.IgnoredAppsKey, "a\nA.exe\n\nKeePass.exe"),
        ];

        var next = SettingsTransfer.Import(new AppSettings(), wild);

        Assert.Equal(1, next.MaxItems);
        Assert.Equal(["a.exe"], next.IgnoredApps);
    }
}
