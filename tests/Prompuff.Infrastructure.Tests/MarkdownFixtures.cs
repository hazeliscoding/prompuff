using System.Globalization;

namespace Prompuff.Infrastructure.Tests;

/// <param name="File">The name 1.0 suggested when exporting it, and the file it's in.</param>
internal sealed record MarkdownFixturePrompt(
    string File,
    string Title,
    string? Description,
    string Body,
    string? Notes,
    IReadOnlyList<string> Tags,
    string? Collection,
    bool IsFavorite,
    int? Rating,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record MarkdownFixtureStep(string PromptTitle, string? Note);

internal sealed record MarkdownFixtureWorkflow(string File, string Name, string? Description, IReadOnlyList<MarkdownFixtureStep> Steps);

/// <summary>
/// Markdown as Prompuff 1.0 exported it, committed in <c>Fixtures/markdown-1.0.0/</c>: each prompt as its own file, a
/// workflow under <c>workflows/</c>, and all of them in <c>library.zip</c>. The prompts cover what the format has to
/// carry: every metadata key, Unicode and emoji, values that need quoting, a body with its own <c># Notes</c> heading
/// and a code fence, and a prompt with nothing but a title and a body. The normal test run only reads the files;
/// <c>MarkdownFixtureTests.Write_fixtures</c> wrote them with 1.0's export and pinned them.
/// </summary>
internal static class MarkdownFixtures
{
    public const string FolderName = "markdown-1.0.0";
    public const string ZipName = "library.zip";

    /// <summary>A committed file, as the build copies it beside the tests.</summary>
    public static string PathFor(string file) => Path.Combine(AppContext.BaseDirectory, "Fixtures", FolderName, file);

    /// <summary>Where <c>Write_fixtures</c> put the files in the source tree.</summary>
    public static string SourceFolder => Path.Combine(SchemaFixtures.SourceFolder, FolderName);

    public static IReadOnlyList<MarkdownFixturePrompt> Prompts { get; } =
    [
        new(
            "angular-upgrade-planner.md",
            "Angular Upgrade Planner",
            "Produces a phased migration plan from the current Angular version to a target, with breaking changes called out per phase.",
            "You are a senior Angular engineer planning an upgrade of {{repo_name}} to Angular {{target_version}}.\n\n" +
            "List the dependencies that will block the upgrade, then give a phased plan:\n\n" +
            "1. Pre-flight: tooling, Node and TypeScript versions\n" +
            "2. Framework bump, one major at a time\n" +
            "3. Verification: tests, build, bundle size\n\n" +
            "For each phase, give the command to run with {{package_manager}}, such as:\n\n" +
            "```bash\nng update @angular/core@{{target_version}} @angular/cli@{{target_version}}\n```",
            "Phasing by major version stopped the model from skipping steps.\n\nAsking for the exact command made the output immediately actionable.",
            ["angular", "migration", "copilot"],
            "Engineering",
            IsFavorite: true,
            Rating: 5,
            At("2026-10-07T09:05:17Z"),
            At("2026-10-07T13:47:58Z")),
        new(
            "caf-menu-translator.md",
            "Café menu translator ☕",
            "Keeps prices, dish names and the café's tone.",
            "Translate the menu of {{cafe_name}} into {{language}}.\n\n" +
            "Keep prices exactly as written, such as “Café crème — 3,50 €”, and keep a dish's name when it has no common translation.",
            "Naming a sample price stopped it from converting currencies. Works in 日本語 too.",
            ["i18n", "café"],
            "Writing & docs",
            IsFavorite: false,
            Rating: 4,
            At("2026-10-07T09:30:44Z"),
            At("2026-10-07T09:36:20Z")),
        new(
            "prompt.md",
            "日本語の要約",
            "Summarizes Japanese text in plain Japanese.",
            "次の文章を{{length}}で要約してください。\n\n{{text}}",
            "敬語を避けると短くなる。",
            ["日本語", "summary"],
            null,
            IsFavorite: true,
            Rating: null,
            At("2026-10-02T11:00:00Z"),
            At("2026-10-07T10:19:02Z")),
        new(
            "bug-reproduction-request.md",
            "Bug reproduction request 🐛",
            null,
            "Here is a bug report for {{project}}:\n\n{{issue}}\n\nAsk me for anything missing, then write the smallest steps that reproduce it.",
            null,
            ["review", "bugs"],
            "Engineering",
            IsFavorite: false,
            Rating: 2,
            At("2026-10-07T11:02:36Z"),
            At("2026-10-07T11:02:36Z")),
        new(
            "review-naming-1-draft.md",
            "Review: \"naming\" #1 — draft",
            "Checks the names in C:\\src\\app\\ and suggests \"better\" ones.",
            "Review the names in {{file}}.\n\nFor each one, answer: keep, rename (to what?), or ask.\n\n> Names that need a comment usually need a new name.",
            "Works best on one file at a time.\n\n---\n\nOn a whole repository it got vague.",
            ["review", "2026"],
            "Code review: weekly",
            IsFavorite: false,
            Rating: 3,
            At("2026-10-08T07:45:10Z"),
            At("2026-10-08T08:02:33Z")),
        new(
            "release-notes-writer.md",
            "Release notes writer",
            null,
            "Write the release notes for {{version}} in this shape:\n\n# Notes\n\n- One line per change, newest first\n- Link each issue, as in #{{issue}}",
            null,
            ["writing"],
            null,
            IsFavorite: false,
            Rating: null,
            At("2026-10-08T08:30:00Z"),
            At("2026-10-08T08:30:00Z")),
        new(
            "plain.md",
            "Plain",
            null,
            "Explain {{topic}} to {{audience}} in three sentences.",
            null,
            [],
            null,
            IsFavorite: false,
            Rating: null,
            At("2026-10-08T09:00:00Z"),
            At("2026-10-08T09:00:00Z")),
    ];

    public static MarkdownFixtureWorkflow Workflow { get; } = new(
        "workflows/angular-upgrade-start-to-finish.md",
        "Angular upgrade, start to finish",
        "Plan the upgrade, reproduce what breaks, then plan the next major. 🚀",
        [
            new("Angular Upgrade Planner", "The phased plan and its risk table.\nPaste the first failure into {{issue}}."),
            new("Bug reproduction request 🐛", null),
            new("Angular Upgrade Planner", "Run it again for the next major."),
        ]);

    public static MarkdownFixturePrompt Prompt(string title) => Prompts.Single(prompt => prompt.Title == title);

    private static DateTimeOffset At(string time) =>
        DateTimeOffset.Parse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
