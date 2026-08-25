using MudBlazor;

namespace SpoolDatTorrent.Web
{
    /// <summary>
    /// Central MudBlazor theme for the whole app. Overrides the default light/dark
    /// palettes so every component (cards, drawer, appbar, buttons) picks up the same
    /// brand colours and supports light/dark mode automatically via MudThemeProvider.
    /// </summary>
    public static class AppTheme
    {
        /// <summary>
        /// Build a fresh <see cref="MudTheme"/>. Called each render so that editing this
        /// file and hot-reloading immediately applies colour changes (no restart needed).
        /// </summary>
        public static MudTheme Theme => new()
        {
            PaletteLight = new PaletteLight
            {
                Primary = "#49847E",
                Secondary = "#E6957F",
                Tertiary = "#26A69A",
                AppbarBackground = "#808080",
                AppbarText = "#FFFFFF",
                DrawerBackground = "#FFFFFF",
                DrawerText = "rgba(0,0,0,0.87)",
                Surface = "#FFFFFF",
                Background = "#F3F4F6",
                Success = "#4CAF50",
                Info = "#2196F3",
                Warning = "#FF9800",
                Error = "#F44336",
                TextPrimary = "rgba(0,0,0,0.87)",
                TextSecondary = "rgba(0,0,0,0.6)"
            },
            PaletteDark = new PaletteDark
            {
                Primary = "#5BA59E",
                Secondary = "#E07A5F",
                Tertiary = "#E9C46A",
                AppbarBackground = "#1E1E1E",
                AppbarText = "#FFFFFF",
                DrawerBackground = "#2D2D30",
                DrawerText = "#FFFFFF",
                Surface = "#2D2D30",
                Background = "#1E1E1E",
                Success = "#66BB6A",
                Info = "#42A5F5",
                Warning = "#FFA726",
                Error = "#EF5350",
                TextPrimary = "#FFFFFF",
                TextSecondary = "rgba(255,255,255,0.7)"
            }
        };
    }
}
