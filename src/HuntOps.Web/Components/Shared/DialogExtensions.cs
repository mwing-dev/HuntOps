using MudBlazor;

namespace HuntOps.Web.Components.Shared;

public static class DialogExtensions
{
    /// <summary>Asks for confirmation before a destructive-looking action (archive, revoke).</summary>
    public static async Task<bool> ConfirmAsync(this IDialogService dialogs, string title, string message, string confirmText)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        var result = await dialogs.ShowMessageBoxAsync(title, message, yesText: confirmText, cancelText: "Cancel");
        return result == true;
    }
}
