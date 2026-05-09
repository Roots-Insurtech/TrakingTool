using MudBlazor;

namespace TrakingTool.Theme;

public static class NotionDarkTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteDark = new PaletteDark
        {
            Black = "#0f0f0f",
            Background = "#191919",
            Surface = "#202020",
            DrawerBackground = "#171717",
            DrawerText = "#cfcfcf",
            DrawerIcon = "#9b9b9b",
            AppbarBackground = "#191919",
            AppbarText = "#e6e6e6",
            TextPrimary = "#e6e6e6",
            TextSecondary = "#9b9b9b",
            TextDisabled = "#5a5a5a",
            ActionDefault = "#cfcfcf",
            ActionDisabled = "#3a3a3a",
            ActionDisabledBackground = "#262626",
            Divider = "#2a2a2a",
            DividerLight = "#222222",
            LinesDefault = "#2a2a2a",
            LinesInputs = "#3a3a3a",
            TableLines = "#222222",
            TableStriped = "#1c1c1c",
            TableHover = "#242424",
            Primary = "#e9a356",
            PrimaryContrastText = "#1a1a1a",
            Secondary = "#7aa6ff",
            Tertiary = "#b48ead",
            Info = "#7aa6ff",
            Success = "#7bbf6b",
            Warning = "#e9a356",
            Error = "#e06c75",
            Dark = "#101010",
            HoverOpacity = 0.06,
            RippleOpacity = 0.08,
            RippleOpacitySecondary = 0.15
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = new[]
                {
                    "Inter",
                    "ui-sans-serif",
                    "system-ui",
                    "-apple-system",
                    "Segoe UI",
                    "Helvetica Neue",
                    "Arial",
                    "sans-serif"
                },
                FontSize = "0.9rem",
                FontWeight = "400",
                LineHeight = "1.55",
                LetterSpacing = "normal"
            },
            H1 = new H1Typography { FontSize = "1.875rem", FontWeight = "700", LineHeight = "1.25" },
            H2 = new H2Typography { FontSize = "1.5rem", FontWeight = "700", LineHeight = "1.3" },
            H3 = new H3Typography { FontSize = "1.25rem", FontWeight = "600", LineHeight = "1.35" },
            H4 = new H4Typography { FontSize = "1.05rem", FontWeight = "600", LineHeight = "1.4" },
            H5 = new H5Typography { FontSize = "0.95rem", FontWeight = "600", LineHeight = "1.4" },
            H6 = new H6Typography { FontSize = "0.875rem", FontWeight = "600", LineHeight = "1.4" },
            Subtitle1 = new Subtitle1Typography { FontSize = "0.875rem", FontWeight = "500" },
            Subtitle2 = new Subtitle2Typography { FontSize = "0.8125rem", FontWeight = "500" },
            Body1 = new Body1Typography { FontSize = "0.9rem", FontWeight = "400", LineHeight = "1.6" },
            Body2 = new Body2Typography { FontSize = "0.8125rem", FontWeight = "400", LineHeight = "1.55" },
            Button = new ButtonTypography { FontSize = "0.8125rem", FontWeight = "500", TextTransform = "none" },
            Caption = new CaptionTypography { FontSize = "0.75rem", FontWeight = "400" },
            Overline = new OverlineTypography { FontSize = "0.6875rem", FontWeight = "500", TextTransform = "uppercase", LetterSpacing = "0.08em" }
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "6px",
            DrawerWidthLeft = "260px",
            AppbarHeight = "52px"
        }
    };
}
