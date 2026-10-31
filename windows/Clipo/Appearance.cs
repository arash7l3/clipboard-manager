using System.Globalization;
using Clipo.Services;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Globalization;

namespace Clipo;

/// <summary>How Clipo looks and talks: the user's theme and language applied to the app's windows.</summary>
internal static class Appearance
{
    /// <summary>
    /// Makes this process use <paramref name="language"/> for Clipo's own
    /// text, and for the direction of its windows. Call it before anything
    /// loads a string, that is before the first window: the resource system
    /// reads the language when it first looks something up, so a change takes
    /// a restart. "Match Windows" leaves what Windows gave the process.
    /// </summary>
    public static void ApplyLanguage(AppLanguage language)
    {
        if (UiLanguage.Tag(language) is not { } tag || UiLanguage.Culture(language) is not { } culture)
        {
            return;
        }
        ApplicationLanguages.PrimaryLanguageOverride = tag;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>Whether the language in use is written right to left.</summary>
    public static bool IsRightToLeft => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;

    /// <summary>The layout direction for a window's content: WinUI does not take it from the language by itself.</summary>
    public static FlowDirection Direction => IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>Gives <paramref name="root"/> the theme the user chose; "match Windows" follows the system.</summary>
    public static void ApplyTheme(FrameworkElement root, AppTheme theme) =>
        root.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
}
