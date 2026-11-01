using Clipo.Services;

namespace Clipo.Tests;

public sealed class PopupKeysTests
{
    private const HotkeyModifiers None = HotkeyModifiers.None;
    private const HotkeyModifiers Shift = HotkeyModifiers.Shift;
    private const HotkeyModifiers Control = HotkeyModifiers.Control;
    private const HotkeyModifiers Alt = HotkeyModifiers.Alt;

    private static readonly HotkeyChord s_hotkey = HotkeyChord.ShowHistory;

    private static PopupKeyCommand? Press(uint key, HotkeyModifiers modifiers = None, string text = "") =>
        PopupKeys.Classify(key, modifiers, text, s_hotkey);

    [Theory]
    [InlineData(0x26u, PopupKeyAction.Up)]
    [InlineData(0x28u, PopupKeyAction.Down)]
    [InlineData(0x21u, PopupKeyAction.PageUp)]
    [InlineData(0x22u, PopupKeyAction.PageDown)]
    [InlineData(0x24u, PopupKeyAction.Home)]
    [InlineData(0x23u, PopupKeyAction.End)]
    [InlineData(0x09u, PopupKeyAction.NextTab)]
    [InlineData(0x0Du, PopupKeyAction.Pick)]
    [InlineData(0x1Bu, PopupKeyAction.Close)]
    [InlineData(0x08u, PopupKeyAction.Backspace)]
    public void TheListKeysDriveThePopup(uint key, PopupKeyAction action) =>
        Assert.Equal(new PopupKeyCommand(action), Press(key));

    [Fact]
    public void ShiftTabGoesBackAndShiftEnterPastesPlain()
    {
        Assert.Equal(PopupKeyAction.PreviousTab, Press(0x09, Shift)?.Action);
        Assert.Equal(new PopupKeyCommand(PopupKeyAction.Pick, Plain: true), Press(0x0D, Shift));
    }

    [Theory]
    [InlineData('A', "a")]
    [InlineData('A', "ش")]
    [InlineData(0x20u, " ")]
    [InlineData(0xBEu, ".")]
    [InlineData(PopupKeys.Packet, "ی")]
    public void TypingKeysSearch(uint key, string text) =>
        Assert.Equal(new PopupKeyCommand(PopupKeyAction.Type, Text: text), Press(key, None, text));

    [Fact]
    public void ShiftedCharactersSearch() =>
        Assert.Equal(new PopupKeyCommand(PopupKeyAction.Type, Text: "A"), Press('A', Shift, "A"));

    [Fact]
    public void AltGrCharactersSearch() =>
        Assert.Equal(new PopupKeyCommand(PopupKeyAction.Type, Text: "€"), Press('E', Control | Alt, "€"));

    [Fact]
    public void ADeadKeyIsKeptFromTheAppButTypesNothing() =>
        Assert.Equal(new PopupKeyCommand(PopupKeyAction.None), Press(0xDE));

    [Fact]
    public void CtrlShortcutsWork()
    {
        Assert.Equal(new PopupKeyCommand(PopupKeyAction.Pin), Press('P', Control));
        Assert.Equal(new PopupKeyCommand(PopupKeyAction.QuickPaste, Number: 3), Press('3', Control));
        Assert.Equal(new PopupKeyCommand(PopupKeyAction.QuickPaste, Plain: true, Number: 9), Press(0x69, Control | Shift));
        Assert.Equal(new PopupKeyCommand(PopupKeyAction.DeleteWord), Press(0x08, Control));
    }

    [Theory]
    [InlineData('A')]
    [InlineData('C')]
    [InlineData('Z')]
    public void OtherCtrlLettersDoNothingInTheFieldUnderneath(uint key) =>
        Assert.Equal(new PopupKeyCommand(PopupKeyAction.None), Press(key, Control));

    [Theory]
    [InlineData(0x25u)]
    [InlineData(0x27u)]
    [InlineData(0x2Eu)]
    public void CaretKeysDoNothingInTheFieldUnderneath(uint key) =>
        Assert.Equal(new PopupKeyCommand(PopupKeyAction.None), Press(key));

    [Fact]
    public void TheShortcutClosesThePopup() =>
        Assert.Equal(new PopupKeyCommand(PopupKeyAction.Close), Press('V', Control | Shift));

    [Fact]
    public void AltF4ClosesThePopupNotTheApp() =>
        Assert.Equal(new PopupKeyCommand(PopupKeyAction.Close), Press(0x73, Alt));

    [Theory]
    // The modifiers and lock keys themselves.
    [InlineData(0x10u, None)]
    [InlineData(0xA2u, Control)]
    [InlineData(0x12u, Alt)]
    [InlineData(0x5Bu, None)]
    [InlineData(0x14u, None)]
    // Alt+Tab, Win+D, Ctrl+Esc, F-keys, Print Screen and a volume key.
    [InlineData(0x09u, Alt)]
    [InlineData('D', HotkeyModifiers.Windows)]
    [InlineData(0x1Bu, Control)]
    [InlineData(0x71u, None)]
    [InlineData(0x2Cu, None)]
    [InlineData(0xAFu, None)]
    public void WindowsKeysGoThrough(uint key, HotkeyModifiers modifiers) => Assert.Null(Press(key, modifiers));

    [Fact]
    public void WithoutAShortcutCtrlShiftVIsJustKeptFromTheApp() =>
        Assert.Equal(new PopupKeyCommand(PopupKeyAction.None), PopupKeys.Classify('V', Control | Shift, "", hotkey: null));

    [Theory]
    [InlineData("", "")]
    [InlineData("abc", "ab")]
    [InlineData("سلام", "سلا")]
    [InlineData("a😀", "a")]
    [InlineData("é", "")]
    public void BackspaceRemovesOneCharacter(string search, string expected) =>
        Assert.Equal(expected, PopupKeys.Backspace(search));

    [Theory]
    [InlineData("", "")]
    [InlineData("hello world", "hello ")]
    [InlineData("hello world  ", "hello ")]
    [InlineData("hello", "")]
    public void CtrlBackspaceRemovesTheLastWord(string search, string expected) =>
        Assert.Equal(expected, PopupKeys.DeleteWord(search));
}
