using MudBlazor;

namespace HuntOps.Web.Components.Layout;

internal static class HuntOpsTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#3a5a40",
            Secondary = "#a3743b",
            Tertiary = "#588157",
            Info = "#2f6690",
            AppbarBackground = "#344e41",
            AppbarText = "#ffffff",
            Background = "#f6f5f0",
            DrawerBackground = "#ffffff",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#8fbc8f",
            Secondary = "#d4a373",
            Tertiary = "#a3b18a",
        },
    };
}
