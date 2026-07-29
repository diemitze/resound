using MudBlazor;

namespace ResoundServer.Web.Shared;


public static class ResoundTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = "#F0A202",
            Secondary = "#2EC4B6",
            Tertiary = "#8D8D92",

            Background = "#131316",
            Surface = "#1B1B1F",
            AppbarBackground = "#0E0E10",
            AppbarText = "#F2F2F2",
            DrawerBackground = "#1B1B1F",

            TextPrimary = "#ECECEE",
            TextSecondary = "#9A9AA2",
            ActionDefault = "#9A9AA2",
            Divider = "#2A2A30",
            DividerLight = "#232329",
            LinesDefault = "#2A2A30",
            TableLines = "#232329",
            TableHover = "#212127",

            Success = "#2EC4B6",
            Warning = "#F0A202",
            Error = "#E5484D",
            Info = "#5B8DEF"
        },

        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "10px"
        }
    };
}
