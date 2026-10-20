using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using StrideBrowser.Helpers;

namespace StrideBrowser.Engine;

public static class ReleaseNotesProvider
{
    public record ReleaseSection(string Category, IReadOnlyList<string> Items);
    public record ReleaseVersion(string Version, string Date, IReadOnlyList<ReleaseSection> Sections);

    private static readonly Lazy<IReadOnlyList<ReleaseVersion>> Releases = new(
        () => ParseChangelog(ResourceLoader.Load("CHANGELOG.md")));

    public static IReadOnlyList<ReleaseVersion> GetAllReleases() => Releases.Value;

    // Only dated releases are shown in the app. Unreleased entries stay in the repository.
    public static IReadOnlyList<ReleaseVersion> ParseChangelog(string markdown)
    {
        var releases = new List<ReleaseVersion>();
        string? version = null;
        string date = "";
        var sections = new List<ReleaseSection>();
        string? category = null;
        var items = new List<string>();

        void FinishSection()
        {
            if (category is not null && items.Count > 0)
                sections.Add(new ReleaseSection(category, items.ToArray()));
            category = null;
            items.Clear();
        }

        void FinishRelease()
        {
            FinishSection();
            if (version is not null)
                releases.Add(new ReleaseVersion(version, date, sections.ToArray()));
            version = null;
            sections.Clear();
        }

        using var reader = new StringReader(markdown);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                FinishRelease();
                var heading = Regex.Match(line,
                    @"^## \[(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)\] - (\d{4}-\d{2}-\d{2})$");
                if (heading.Success && DateOnly.TryParseExact(heading.Groups[2].Value,
                    "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    version = heading.Groups[1].Value;
                    date = heading.Groups[2].Value;
                }
            }
            else if (version is not null && line.StartsWith("### ", StringComparison.Ordinal))
            {
                FinishSection();
                var name = line[4..].Trim();
                if (name is "Added" or "Changed" or "Deprecated" or "Removed" or "Fixed" or "Security")
                    category = name;
            }
            else if (version is not null && category is not null)
            {
                if (line.StartsWith("- ", StringComparison.Ordinal))
                    items.Add(line[2..].Trim());
                else if (items.Count > 0 && line.StartsWith("  ", StringComparison.Ordinal))
                    items[^1] += " " + line.Trim();
            }
        }
        FinishRelease();
        return releases.ToArray();
    }
}
