using StrideBrowser.Engine;
using StrideBrowser.Services.Pages;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class ReleaseNotesProviderTests
{
    [Fact]
    public void ParseChangelog_ExcludesUnreleasedAndKeepsReleaseOrderAndCategories()
    {
        var releases = ReleaseNotesProvider.ParseChangelog("""
            # Changelog
            ## [Unreleased]
            ### Added
            - Pending feature.
            ## [0.1.1] - 2026-11-01
            ### Added
            - A published feature.
              More detail on the next line.
            ### Changed
            - A changed setting.
            ### Fixed
            - A fixed bug.
            ### Security
            - A security fix.
            ## [0.1.0] - 2026-10-31
            ### Added
            - Initial release.
            """);

        Assert.Equal(new[] { "0.1.1", "0.1.0" }, releases.Select(release => release.Version));
        var newest = releases[0];
        Assert.Equal("2026-11-01", newest.Date);
        Assert.Equal(new[] { "Added", "Changed", "Fixed", "Security" }, newest.Sections.Select(section => section.Category));
        Assert.Equal("A published feature. More detail on the next line.", Assert.Single(newest.Sections[0].Items));
        Assert.DoesNotContain(releases.SelectMany(release => release.Sections).SelectMany(section => section.Items), item => item.Contains("Pending"));
    }

    [Theory]
    [InlineData("## [0.1.0] - 2026-02-30")]
    [InlineData("## [0.1.0]")]
    [InlineData("## [Unreleased]")]
    public void ParseChangelog_DoesNotTreatUndatedOrInvalidSectionsAsReleases(string heading)
    {
        Assert.Empty(ReleaseNotesProvider.ParseChangelog(heading + "\n### Added\n- Not a published release."));
    }

    [Fact]
    public void ParseChangelog_StopsAtUnreleasedAndUnrecognizedHeadings()
    {
        var releases = ReleaseNotesProvider.ParseChangelog("""
            ## [0.1.0-beta.1] - 2026-10-31
            ### Added
            - Published.
            ### Internal notes
            - Private build note.
            ## [Unreleased]
            ### Added
            - Pending.
            """);
        var release = Assert.Single(releases);
        Assert.Equal("0.1.0-beta.1", release.Version);
        Assert.Equal("Published.", Assert.Single(Assert.Single(release.Sections).Items));
    }

    [Fact]
    public void Render_EncodesChangelogContentAndLabelsChangedEntries()
    {
        var releases = ReleaseNotesProvider.ParseChangelog("""
            ## [0.1.0] - 2026-10-31
            ### Changed
            - <script>alert('example')</script>
            """);
        var html = new ReleaseNotesPage().Render("0.1.0", releases, "test");
        Assert.Contains("<h3>Changed</h3>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("{{VERSION}}", html);
        Assert.DoesNotContain("{{DATE}}", html);
        Assert.DoesNotContain("{{ALL_RELEASES_HTML}}", html);
    }

    [Fact]
    public void DevelopmentBuild_HasNoInventedReleaseHistory()
    {
        var releases = ReleaseNotesProvider.GetAllReleases();
        Assert.Empty(releases);
        var html = new ReleaseNotesPage().Render("0.1.0", releases, "test");
        Assert.Contains("Version 0.1.0", html);
        Assert.Contains("Unreleased", html);
        Assert.Contains("No releases have been published yet.", html);
        Assert.DoesNotContain("v1.2.", html);
        Assert.DoesNotContain("{{VERSION}}", html);
        Assert.DoesNotContain("{{DATE}}", html);
        Assert.DoesNotContain("{{ALL_RELEASES_HTML}}", html);
    }
    [Fact]
    public void RenderGroupsCategoriesOnceAndKeepsOlderReleasesExpandable()
    {
        var releases = ReleaseNotesProvider.ParseChangelog("""
            ## [0.1.2] - 2026-10-21
            ### Added
            - First addition.
            - Second addition.
            ### Fixed
            - A repair.
            ## [0.1.1] - 2026-10-19
            ### Changed
            - An older change.
            """);
        var html = new ReleaseNotesPage().Render("0.1.2", releases, "test");
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(html, "<h3>Added</h3>").Count);
        Assert.Contains("<li>First addition.</li>", html);
        Assert.Contains("<li>Second addition.</li>", html);
        Assert.Contains("<article class=\"release\" id=\"release-0\">", html);
        Assert.Contains("<details class=\"release release-log\" id=\"release-1\">", html);
        Assert.Contains("October 21, 2026", html);
        Assert.DoesNotContain("You're up to date", html);
        Assert.DoesNotContain("{{RELEASE_NAV_HTML}}", html);
        Assert.DoesNotContain("{{RELEASE_STATUS}}", html);
        Assert.DoesNotContain("{{APP_ICON}}", html);
    }

}
