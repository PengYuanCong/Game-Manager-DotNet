namespace Proposal.Tests;

public sealed class FrontendStructureTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Layout_UsesSharedGameShell()
    {
        var layout = Read("Proposal", "Views", "_Layout.cshtml");

        Assert.Contains("site.css", layout, StringComparison.Ordinal);
        Assert.Contains("class=\"skip-link\"", layout, StringComparison.Ordinal);
        Assert.Contains("nav-primary", layout, StringComparison.Ordinal);
        Assert.Contains("nav-utility", layout, StringComparison.Ordinal);
        Assert.Contains("id=\"main-content\"", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("<style>", layout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedStyles_DefineResponsiveGamePrimitives()
    {
        var css = Read("Proposal", "wwwroot", "css", "site.css");

        Assert.Contains("--color-gold:", css, StringComparison.Ordinal);
        Assert.Contains(".game-page-header", css, StringComparison.Ordinal);
        Assert.Contains(".game-toolbar", css, StringComparison.Ordinal);
        Assert.Contains(".game-card", css, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Frontend_DoesNotIntroduceAnotherFramework()
    {
        var project = Read("Proposal", "Proposal.csproj");
        var layout = Read("Proposal", "Views", "_Layout.cshtml");

        Assert.DoesNotContain("tailwind", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("react", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gsap", layout, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. segments]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Proposal.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Proposal.slnx.");
    }
}
