using Avalonia.Media;

namespace Prompuff.App.Themes;

/// <summary>A tone: the color itself, and a version for text that reads on the theme's backgrounds.</summary>
public readonly record struct Tone(Color Base, Color Text)
{
    public Tone(string color, string text)
        : this(Color.Parse(color), Color.Parse(text))
    {
    }
}

/// <summary>
/// The colors that make a theme. <see cref="ThemeBuilder"/> derives the rest (subtle fills, borders, Fluent control
/// colors) the same way for every theme, so each one fills exactly the keys in <c>Tokens.axaml</c>.
/// </summary>
/// <param name="Id">Saved in settings, so it never changes once shipped.</param>
/// <param name="Backgrounds">Bg0 (the window) to Bg4 (hover), then the inset for wells and inputs.</param>
/// <param name="Borders">Border1 to Border3, faintest first.</param>
/// <param name="Texts">Text1 to Text4, strongest first.</param>
/// <param name="Accent">The interactive color, hover, pressed, and the version used for text.</param>
/// <param name="TextInverse">Text on the accent, such as a primary button's label.</param>
public sealed record ThemePalette(
    string Id,
    string Name,
    bool IsDark,
    string[] Backgrounds,
    string[] Borders,
    string[] Texts,
    string[] Accent,
    string TextInverse,
    Tone Lavender,
    Tone Sky,
    Tone Pink,
    Tone Peach,
    Tone Mint,
    Tone Blue,
    Tone Amber,
    Tone Critical)
{
    public Color Bg(int level) => Color.Parse(Backgrounds[level]);
    public Color Inset => Color.Parse(Backgrounds[5]);
    public Color Border(int level) => Color.Parse(Borders[level - 1]);
    public Color Text(int level) => Color.Parse(Texts[level - 1]);
    public Color AccentColor => Color.Parse(Accent[0]);
    public Color AccentHover => Color.Parse(Accent[1]);
    public Color AccentActive => Color.Parse(Accent[2]);
    public Color AccentText => Color.Parse(Accent[3]);
    public Color Inverse => Color.Parse(TextInverse);

    /// <summary>Prompuff's own themes live in <c>Tokens.axaml</c>; these entries only describe them for the picker.</summary>
    public bool IsBuiltIn => Id is ThemeCatalog.PrompuffDark or ThemeCatalog.PrompuffLight;
}

/// <summary>
/// Every theme Prompuff ships. Each palette follows its original's published colors; where the original has no
/// matching role (a pink, a third background), the closest color from the same family fills in, and text colors are
/// nudged where needed to keep text readable. The palettes' licenses are in <c>licenses/Themes.md</c>.
/// </summary>
public static class ThemeCatalog
{
    public const string PrompuffDark = "prompuff-dark";
    public const string PrompuffLight = "prompuff-light";

    public static IReadOnlyList<ThemePalette> All { get; } =
    [
        new(PrompuffDark, "Prompuff Dark", true,
            ["#0A0C10", "#0F1218", "#151920", "#1B2029", "#222834", "#07090C"],
            ["#1E242E", "#2A3240", "#3A4453"],
            ["#E8ECF1", "#98A2B3", "#5F6B7A", "#3E4754"],
            ["#2DD4BF", "#5EEAD4", "#14B8A6", "#7FF0E0"], "#0A0C10",
            new("#B48EFF", "#CDB4FF"), new("#38BDF8", "#7DD3FC"), new("#E879A9", "#F2A7C7"), new("#E08E4A", "#F0B07F"),
            new("#3DD68C", "#7BE8B3"), new("#7AA2F7", "#A9C2FA"), new("#F2B441", "#F8CE7A"), new("#F0555D", "#FF8A90")),

        new("darcula", "Darcula", true,
            ["#232525", "#2B2B2B", "#313335", "#3C3F41", "#45494A", "#1E2020"],
            ["#373A3C", "#4B4F51", "#5E6264"],
            ["#D4D4D4", "#A9B7C6", "#8C8C8C", "#5E6060"],
            ["#589DF6", "#78B0F8", "#3C7FD8", "#8FBEFA"], "#1E2020",
            new("#9876AA", "#B9A0C9"), new("#6897BB", "#92B6D5"), new("#C77DBB", "#DCA3D2"), new("#CC7832", "#E09A5F"),
            new("#6A8759", "#9CBF87"), new("#589DF6", "#8FBEFA"), new("#FFC66D", "#FFD899"), new("#FF6B68", "#FF9C99")),

        new("gruvbox-dark", "Gruvbox Dark", true,
            ["#1D2021", "#282828", "#32302F", "#3C3836", "#504945", "#161819"],
            ["#3C3836", "#504945", "#665C54"],
            ["#EBDBB2", "#BDAE93", "#A89984", "#665C54"],
            ["#8EC07C", "#A6D18F", "#689D6A", "#A9D19B"], "#1D2021",
            new("#D3869B", "#E5B0BE"), new("#83A598", "#A7C0B6"), new("#B16286", "#D3869B"), new("#FE8019", "#FEAB6B"),
            new("#B8BB26", "#CFD16B"), new("#458588", "#83A598"), new("#FABD2F", "#FCD36F"), new("#FB4934", "#FC7A6A")),

        new("dracula", "Dracula", true,
            ["#191A21", "#21222C", "#282A36", "#343746", "#44475A", "#14151B"],
            ["#2E303D", "#44475A", "#565970"],
            ["#F8F8F2", "#C5C8D6", "#8C98C4", "#6272A4"],
            ["#BD93F9", "#D0B0FB", "#A275F2", "#CFB2FB"], "#191A21",
            new("#BD93F9", "#D4B8FB"), new("#8BE9FD", "#B0F1FE"), new("#FF79C6", "#FFA3D7"), new("#FFB86C", "#FFCD98"),
            new("#50FA7B", "#8CFCA7"), new("#6C7BD8", "#9BA6E8"), new("#F1FA8C", "#F6FBB3"), new("#FF5555", "#FF8888")),

        new("nord", "Nord", true,
            ["#242933", "#2E3440", "#3B4252", "#434C5E", "#4C566A", "#20242C"],
            ["#3B4252", "#4C566A", "#616E88"],
            ["#ECEFF4", "#D8DEE9", "#A3ACBE", "#616E88"],
            ["#88C0D0", "#9FCFDC", "#6FAFC2", "#A3D2DE"], "#2E3440",
            new("#B48EAD", "#CBAEC6"), new("#88C0D0", "#ACD5E0"), new("#C48BB5", "#D9B0CF"), new("#D08770", "#E0A891"),
            new("#A3BE8C", "#BED3AD"), new("#81A1C1", "#A5BDD5"), new("#EBCB8B", "#F2DCAE"), new("#BF616A", "#E8AEB3")),

        new("one-dark", "One Dark", true,
            ["#1B1D23", "#21252B", "#282C34", "#2F343E", "#3A3F4B", "#181A1F"],
            ["#30353F", "#3E4451", "#4B5263"],
            ["#D7DAE0", "#ABB2BF", "#868B95", "#5C6370"],
            ["#61AFEF", "#82C0F3", "#4A9EE6", "#8CC5F4"], "#1B1D23",
            new("#C678DD", "#D9A2E8"), new("#56B6C2", "#85CBD4"), new("#D55FDE", "#E290E9"), new("#D19A66", "#E0B48B"),
            new("#98C379", "#B5D59E"), new("#61AFEF", "#8CC5F4"), new("#E5C07B", "#EED4A2"), new("#E06C75", "#E9969C")),

        new("tokyo-night", "Tokyo Night", true,
            ["#16161E", "#1A1B26", "#1F2335", "#24283B", "#292E42", "#13131A"],
            ["#27293A", "#3B4261", "#545C7E"],
            ["#C0CAF5", "#A9B1D6", "#7880A8", "#565F89"],
            ["#7AA2F7", "#94B5F9", "#5D8FF5", "#9FBCFA"], "#16161E",
            new("#BB9AF7", "#D0B8FA"), new("#7DCFFF", "#A6DEFF"), new("#F7768E", "#F9A0B0"), new("#FF9E64", "#FFBB92"),
            new("#9ECE6A", "#B9DC94"), new("#7AA2F7", "#9FBCFA"), new("#E0AF68", "#EAC690"), new("#DB4B4B", "#F7768E")),

        new("solarized-dark", "Solarized Dark", true,
            ["#00212B", "#002B36", "#073642", "#0A3D4A", "#104653", "#001B23"],
            ["#0E3F4C", "#284F59", "#3C626B"],
            ["#EEE8D5", "#9EACAC", "#839496", "#586E75"],
            ["#2AA198", "#3CB8AD", "#1F8A82", "#4CC2B7"], "#002B36",
            new("#6C71C4", "#9A9EE0"), new("#2AA198", "#4CC2B7"), new("#D33682", "#EC85B8"), new("#CB4B16", "#EA8C64"),
            new("#859900", "#A8BD2E"), new("#268BD2", "#5CA6E0"), new("#B58900", "#D6A92A"), new("#DC322F", "#F2807D")),

        new("catppuccin-mocha", "Catppuccin Mocha", true,
            ["#11111B", "#181825", "#1E1E2E", "#282839", "#313244", "#0E0E16"],
            ["#2A2B3C", "#45475A", "#585B70"],
            ["#CDD6F4", "#BAC2DE", "#8F94AE", "#585B70"],
            ["#CBA6F7", "#D8BCF9", "#B48EF3", "#DCC2FA"], "#11111B",
            new("#B4BEFE", "#CDD4FE"), new("#89DCEB", "#B0E8F1"), new("#F5C2E7", "#F9D9F0"), new("#FAB387", "#FCC9A8"),
            new("#A6E3A1", "#C2ECBE"), new("#89B4FA", "#ADCBFB"), new("#F9E2AF", "#FBECC9"), new("#F38BA8", "#F7ADC2")),

        new(PrompuffLight, "Prompuff Light", false,
            ["#F3F4F6", "#FAFAFB", "#FFFFFF", "#F6F7F9", "#EDEFF3", "#F7F8FA"],
            ["#E6E8EC", "#D7DBE2", "#BCC3CE"],
            ["#171B22", "#525C6B", "#7D8796", "#B3BAC5"],
            ["#0F766E", "#115E59", "#134E4A", "#0B7A70"], "#FFFFFF",
            new("#7C5CD6", "#5E3FB8"), new("#0A8FCB", "#0A6F9D"), new("#C9407F", "#A3305F"), new("#C4681F", "#94500F"),
            new("#1E9E5E", "#177A49"), new("#3B6FE0", "#2A55B5"), new("#C98A12", "#8F6100"), new("#D7343E", "#B0232C")),

        new("gruvbox-light", "Gruvbox Light", false,
            ["#F2E5BC", "#F7ECC4", "#FBF1C7", "#F5E9C0", "#EBDBB2", "#F9F5D7"],
            ["#E6D5AB", "#D5C4A1", "#BDAE93"],
            ["#3C3836", "#504945", "#6F655B", "#A89984"],
            ["#427B58", "#38694B", "#2F5A40", "#356447"], "#F9F5D7",
            new("#B16286", "#8F3F71"), new("#458588", "#076678"), new("#CC6D8F", "#963A5B"), new("#D65D0E", "#AF3A03"),
            new("#98971A", "#66620A"), new("#458588", "#076678"), new("#D79921", "#875608"), new("#CC241D", "#9D0006")),

        new("solarized-light", "Solarized Light", false,
            ["#EEE8D5", "#F6EFDC", "#FDF6E3", "#F8F1DE", "#E9E2CD", "#FFFBF0"],
            ["#E6DFCA", "#D6CFBA", "#B9B3A0"],
            ["#073642", "#4C5F66", "#657B83", "#93A1A1"],
            ["#2075B5", "#1A6399", "#155280", "#1A6399"], "#FFFFFF",
            new("#6C71C4", "#4F54A8"), new("#2AA198", "#177068"), new("#D33682", "#A8246A"), new("#CB4B16", "#A33A0F"),
            new("#859900", "#5F6E00"), new("#268BD2", "#1A6399"), new("#B58900", "#8A6800"), new("#DC322F", "#B02623")),

        new("catppuccin-latte", "Catppuccin Latte", false,
            ["#E6E9EF", "#EAEDF2", "#EFF1F5", "#E9EBF1", "#DCE0E8", "#F4F5F8"],
            ["#DCE0E8", "#CCD0DA", "#ACB0BE"],
            ["#4C4F69", "#5C5F77", "#6C6F85", "#9CA0B0"],
            ["#8839EF", "#7A2EE0", "#6B25C9", "#7A2EE0"], "#FFFFFF",
            new("#7287FD", "#3D50C8"), new("#04A5E5", "#036A93"), new("#EA76CB", "#B03A92"), new("#FE640B", "#B84804"),
            new("#40A02B", "#2F7A1F"), new("#1E66F5", "#1550C7"), new("#DF8E1D", "#8A570A"), new("#D20F39", "#B00D30")),
    ];

    public static IReadOnlyList<ThemePalette> Dark { get; } = All.Where(theme => theme.IsDark).ToList();
    public static IReadOnlyList<ThemePalette> Light { get; } = All.Where(theme => !theme.IsDark).ToList();

    /// <summary>The theme with this ID, or Prompuff's own when it's unknown, such as a theme a later version removed.</summary>
    public static ThemePalette Find(string? id, bool dark) =>
        All.FirstOrDefault(theme => theme.Id == id && theme.IsDark == dark) ?? Find(dark ? PrompuffDark : PrompuffLight, dark);
}
