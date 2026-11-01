namespace Clipo.Services;

/// <summary>What a key did to a rename in the popup.</summary>
public enum PopupRenameStep
{
    /// <summary>The rename goes on; <see cref="PopupRename.Text"/> may have changed.</summary>
    Editing,

    /// <summary>Enter: save <see cref="PopupRename.Text"/> as the item's name.</summary>
    Save,

    /// <summary>Esc: keep the old name.</summary>
    Cancel,
}

/// <summary>
/// Renaming one popup row. The popup never takes the focus, so the name is
/// typed through the popup's keys (<see cref="PopupKeys"/>) like the search.
/// The old name starts out selected: the first character typed replaces it,
/// and End keeps it to type after it. Enter saves and Esc keeps the old name.
/// </summary>
/// <param name="id">The item being renamed.</param>
/// <param name="name">Its name now.</param>
public sealed class PopupRename(long id, string name)
{
    public long Id { get; } = id;

    /// <summary>The name as typed so far.</summary>
    public string Text { get; private set; } = name;

    /// <summary>The old name is still whole and selected; the next character replaces it.</summary>
    public bool IsFresh { get; private set; } = true;

    /// <summary>Applies a key press to the name.</summary>
    public PopupRenameStep Handle(PopupKeyCommand command)
    {
        switch (command.Action)
        {
            case PopupKeyAction.Type:
                Text = (IsFresh ? "" : Text) + command.Text;
                break;
            case PopupKeyAction.Backspace:
                Text = IsFresh ? "" : PopupKeys.Backspace(Text);
                break;
            case PopupKeyAction.DeleteWord:
                Text = IsFresh ? "" : PopupKeys.DeleteWord(Text);
                break;
            case PopupKeyAction.End:
                break;
            case PopupKeyAction.Pick:
                return PopupRenameStep.Save;
            case PopupKeyAction.Close:
                return PopupRenameStep.Cancel;
            default:
                // Moving through the list, switching tabs and the like wait until the rename ends.
                return PopupRenameStep.Editing;
        }
        IsFresh = false;
        return PopupRenameStep.Editing;
    }
}
