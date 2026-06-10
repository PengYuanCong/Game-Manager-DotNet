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

    [Fact]
    public void Home_PrioritizesRecommendationFlow()
    {
        var home = Read("Proposal", "Views", "Home", "Index.cshtml");

        Assert.Contains("home-command", home, StringComparison.Ordinal);
        Assert.Contains("開始 AI 推薦", home, StringComparison.Ordinal);
        Assert.Contains("選擇英雄", home, StringComparison.Ordinal);
        Assert.Contains("比較三個海克斯", home, StringComparison.Ordinal);
        Assert.Contains("取得推薦", home, StringComparison.Ordinal);
        Assert.DoesNotContain("flow-card-4", home, StringComparison.Ordinal);
    }

    [Fact]
    public void AiRecommendation_PresentsFourInputStagesAndDecisionPanel()
    {
        var page = Read("Proposal", "Views", "AiRecommendation", "Index.cshtml");

        Assert.Contains("recommendation-stage", page, StringComparison.Ordinal);
        Assert.Contains("鎖定英雄", page, StringComparison.Ordinal);
        Assert.Contains("選擇對局階段", page, StringComparison.Ordinal);
        Assert.Contains("帶入本輪三個海克斯", page, StringComparison.Ordinal);
        Assert.Contains("補充對局狀況", page, StringComparison.Ordinal);
        Assert.Contains("decision-preview", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("LolAramGuides", "英雄資料庫")]
    [InlineData("LolAramAugments", "海克斯資料庫")]
    [InlineData("Equipment", "裝備資料庫")]
    public void LibraryPages_UseSharedHeaderAndToolbar(string viewFolder, string expectedTitle)
    {
        var page = Read("Proposal", "Views", viewFolder, "Index.cshtml");

        Assert.Contains("game-page-header", page, StringComparison.Ordinal);
        Assert.Contains("game-toolbar", page, StringComparison.Ordinal);
        Assert.Contains("game-card", page, StringComparison.Ordinal);
        Assert.Contains(expectedTitle, page, StringComparison.Ordinal);
    }

    [Fact]
    public void SecondaryAndAccountPages_UseTheGameDesignSystem()
    {
        var calculator = Read("Proposal", "Views", "Calculator", "Index.cshtml");
        var highlights = Read("Proposal", "Views", "Media", "Highlights.cshtml");
        var profile = Read("Proposal", "Views", "User", "Profile.cshtml");
        var login = Read("Proposal", "Views", "Account", "Login.cshtml");
        var register = Read("Proposal", "Views", "Account", "Register.cshtml");
        var notFound = Read("Proposal", "Views", "Home", "NotFoundPage.cshtml");

        Assert.Contains("game-page-header", calculator, StringComparison.Ordinal);
        Assert.Contains("game-page-header", highlights, StringComparison.Ordinal);
        Assert.Contains("game-page-header", profile, StringComparison.Ordinal);
        Assert.Contains("~/css/site.css", login, StringComparison.Ordinal);
        Assert.Contains("/images/poro-guide.png", login, StringComparison.Ordinal);
        Assert.Contains("~/css/site.css", register, StringComparison.Ordinal);
        Assert.Contains("/images/poro-guide.png", register, StringComparison.Ordinal);
        Assert.Contains("error-page", notFound, StringComparison.Ordinal);
        Assert.DoesNotContain("infinite", notFound, StringComparison.OrdinalIgnoreCase);
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
