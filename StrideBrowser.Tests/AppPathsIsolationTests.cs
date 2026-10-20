using System.IO;
using StrideBrowser.Helpers;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Path rules for the STRIDE_DATA_DIR relocation used by the recovery
/// verification harness. Every case that is not an explicit, separate,
/// absolute directory has to land on the production profile path so a mistyped
/// override can never write into the installed browser's data.
/// </summary>
public sealed class AppPathsIsolationTests
{
    private const string LocalAppData = @"C:\Users\Daniel\AppData\Local";

    private static string Production => Path.Combine(LocalAppData, "StrideBrowser");

    [Fact]
    public void Unset_UsesProductionPath()
    {
        Assert.Equal(Production, AppPaths.ResolveDataDirectory(null, LocalAppData));
    }

    [Fact]
    public void Empty_UsesProductionPath()
    {
        Assert.Equal(Production, AppPaths.ResolveDataDirectory("", LocalAppData));
    }

    [Fact]
    public void Whitespace_UsesProductionPath()
    {
        Assert.Equal(Production, AppPaths.ResolveDataDirectory("   \t ", LocalAppData));
    }

    [Fact]
    public void RelativeOverride_UsesProductionPath()
    {
        // A relative value depends on the current directory of whatever started
        // the process, which differs between a shortcut, a shell and a test host.
        Assert.Equal(Production, AppPaths.ResolveDataDirectory(@"tools\verify\profile", LocalAppData));
        Assert.Equal(Production, AppPaths.ResolveDataDirectory(@".\profile", LocalAppData));
        Assert.Equal(Production, AppPaths.ResolveDataDirectory(@"..\profile", LocalAppData));
    }

    [Fact]
    public void AbsoluteOverride_IsUsed()
    {
        var chosen = @"C:\dev\Stride\tools\RecoveryVerification\profile-abc";
        Assert.Equal(chosen, AppPaths.ResolveDataDirectory(chosen, LocalAppData));
    }

    [Fact]
    public void OverrideWithQuotes_IsTrimmedAndUsed()
    {
        var chosen = @"C:\dev\Stride\tools\profile-abc";
        Assert.Equal(chosen, AppPaths.ResolveDataDirectory($"\"{chosen}\"", LocalAppData));
    }

    [Fact]
    public void OverrideWithTrailingSeparator_IsTheSameDirectory()
    {
        var chosen = @"C:\dev\Stride\tools\profile-abc";
        Assert.Equal(chosen, AppPaths.ResolveDataDirectory(chosen + @"\", LocalAppData));
    }

    [Fact]
    public void OverridePointingAtProduction_UsesProductionPath()
    {
        Assert.Equal(Production, AppPaths.ResolveDataDirectory(Production, LocalAppData));
        Assert.Equal(Production, AppPaths.ResolveDataDirectory(Production + @"\", LocalAppData));
    }

    [Fact]
    public void OverridePointingAtProductionIgnoringCase_UsesProductionPath()
    {
        var shouty = Production.ToUpperInvariant();
        Assert.Equal(Production, AppPaths.ResolveDataDirectory(shouty, LocalAppData));
    }

    [Fact]
    public void OverrideContainingProduction_UsesProductionPath()
    {
        // LocalAppData itself would scatter settings.json, history.json and the
        // WebView2 profile next to the real one and make "delete the profile"
        // ambiguous, so it is refused.
        Assert.Equal(Production, AppPaths.ResolveDataDirectory(LocalAppData, LocalAppData));
        Assert.Equal(Production, AppPaths.ResolveDataDirectory(@"C:\Users\Daniel\AppData", LocalAppData));
    }

    [Fact]
    public void OverrideThatIsAnExistingFile_UsesProductionPath()
    {
        var dir = Directory.CreateTempSubdirectory("stride-path-boundary");
        try
        {
            var file = Path.Combine(dir.FullName, "not-a-directory.txt");
            File.WriteAllText(file, "x");
            Assert.Equal(Production, AppPaths.ResolveDataDirectory(file, LocalAppData));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void IsolatedFlag_IsFalseForProduction()
    {
        var resolved = AppPaths.ResolveDataDirectory(null, LocalAppData);
        Assert.False(AppPaths.IsSameOrAncestor(resolved, AppPaths.ResolveDataDirectory(@"D:\elsewhere", LocalAppData)));
        Assert.True(AppPaths.IsSameOrAncestor(resolved, Path.Combine(resolved, "session.json")));
    }

    [Fact]
    public void PathBoundary_RequiresFullPath()
    {
        Assert.False(AppPaths.IsSameOrAncestor(@"C:\a", @"C:\ab"));
        Assert.True(AppPaths.IsSameOrAncestor(@"C:\a", @"C:\a\b"));
        Assert.True(AppPaths.IsSameOrAncestor(@"C:\a", @"C:\A"));
    }

    [Fact]
    public void ClosedTabsFile_LivesBesideTheSessionFile()
    {
        // The closed history and the startup snapshot are separate files, so
        // emptying one cannot touch the other.
        Assert.NotEqual(AppPaths.SessionFile, AppPaths.ClosedTabsFile);
        Assert.Equal(Path.GetDirectoryName(AppPaths.SessionFile), Path.GetDirectoryName(AppPaths.ClosedTabsFile));
    }
}