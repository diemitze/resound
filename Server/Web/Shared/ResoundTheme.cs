using MudBlazor;

namespace ResoundServer.Web.Shared;

// Near-black chassis and one soft accent, reserved for what is selected.
public static class ResoundTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = "#5B9CFF",
            Secondary = "#35C2A8",
            Tertiary = "#8D8D92",

            Background = "#0C0C0F",
            Surface = "#15151A",
            AppbarBackground = "#08080A",
            AppbarText = "#F2F2F2",
            DrawerBackground = "#15151A",

            TextPrimary = "#F4F5F7",
            TextSecondary = "#B4B7BF",
            ActionDefault = "#9A9AA2",
            Divider = "#2C2C35",
            DividerLight = "#202027",
            LinesDefault = "#2C2C35",
            TableLines = "#202027",
            TableHover = "#1C1C22",

            Success = "#35C2A8",
            Warning = "#E0B072",
            Error = "#F0575B",
            Info = "#5B9CFF"
        },

        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "10px"
        }
    };
}
