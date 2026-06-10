# 遊戲網站前端收尾 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 將現有 ARAM Mayhem MVC 網站整理成一致、成熟、可快速操作的「海克斯戰情室 + 普羅助手」遊戲工具，同時保留既有搜尋、CRUD、推薦、收藏與登入流程。

**Architecture:** 沿用 Razor Views、Bootstrap 5、Bootstrap Icons 與原生 JavaScript，將共用設計 token、導覽、頁面框架與互動狀態集中到 `site.css`、`site.js` 和唯一的根 Layout。各功能頁只保留該頁專屬版面規則，透過共用 `game-page`、`game-page-header`、`game-toolbar`、`game-card` 與 `game-empty-state` 類別收斂視覺，不改控制器、資料庫或推薦演算法。

**Tech Stack:** ASP.NET Core MVC / Razor Views、Bootstrap 5.3、Bootstrap Icons、CSS custom properties、原生 JavaScript、xUnit、Playwright in-app browser

---

## File Map

- Modify `Proposal/Views/_Layout.cshtml`: 唯一共用 Layout、導覽分組、skip link、CSS/JS 載入與登入狀態。
- Modify `Proposal/Views/Shared/_Layout.cshtml`: 保持與根 Layout 一致，避免部分 Razor 搜尋路徑載入舊殼層。
- Modify `Proposal/wwwroot/css/site.css`: 設計 token、頁面框架、導覽、按鈕、表單、卡片、動畫與響應式規則。
- Modify `Proposal/wwwroot/js/site.js`: 導覽收合、頁面 ready 狀態、圖片 fallback 與 reduced-motion 友善互動。
- Modify `Proposal/Views/Home/Index.cshtml`: 三階段首頁與普羅助手。
- Modify `Proposal/Views/AiRecommendation/Index.cshtml`: 分段輸入、右側決策預覽與結果層級。
- Modify `Proposal/Views/LolAramGuides/Index.cshtml`: 英雄資料頁共用 header、toolbar 與卡片狀態。
- Modify `Proposal/Views/LolAramAugments/Index.cshtml`: 海克斯資料頁共用 header、固定尺寸屬性抽屜與卡片狀態。
- Modify `Proposal/Views/Equipment/Index.cshtml`: 裝備資料頁共用 header、toolbar、選取列與卡片狀態。
- Modify `Proposal/Views/Calculator/Index.cshtml`: 公式頁共用框架與結果層級。
- Modify `Proposal/Views/Media/Highlights.cshtml`: 精彩操作頁共用框架與影片清單。
- Modify `Proposal/Views/User/Profile.cshtml`: 個人資料頁共用框架與最近活動。
- Modify `Proposal/Views/Account/Login.cshtml`: 登入頁設計 token 與普羅輔助視覺。
- Modify `Proposal/Views/Account/Register.cshtml`: 註冊頁設計 token 與欄位層級。
- Modify `Proposal/Views/Home/NotFoundPage.cshtml`: 404 頁設計 token 與返回操作。
- Create `Proposal.Tests/FrontendStructureTests.cs`: 靜態檢查共用殼層、核心 CTA、語意結構和外部套件限制。

### Task 1: 建立可測試的共用視覺殼層

**Files:**
- Create: `Proposal.Tests/FrontendStructureTests.cs`
- Modify: `Proposal/Views/_Layout.cshtml`
- Modify: `Proposal/Views/Shared/_Layout.cshtml`
- Modify: `Proposal/wwwroot/css/site.css`
- Modify: `Proposal/wwwroot/js/site.js`

- [x] **Step 1: Write the failing shell tests**

建立 `FrontendStructureTests.cs`，用專案根目錄定位 Razor/CSS 原始檔，先鎖定共用 stylesheet、skip link、主要/工具導覽群組、ready class 與禁止新增前端框架：

```csharp
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
```

- [x] **Step 2: Run the tests to verify they fail**

Run:

```powershell
dotnet test Proposal.Tests\Proposal.Tests.csproj --filter FrontendStructureTests
```

Expected: FAIL because the current Layout contains inline `<style>`, lacks the semantic nav groups, and `site.css` lacks the new primitives.

- [x] **Step 3: Replace the inline Layout styling with the shared shell**

In both Layout files:

1. Add `<link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />`.
2. Remove the inline `<style>` block.
3. Add `<a class="skip-link" href="#main-content">跳到主要內容</a>` immediately after `<body>`.
4. Split the existing navigation list:

```html
<ul class="navbar-nav nav-primary me-xl-2">
    <!-- AI 推薦、英雄、海克斯、裝備 -->
</ul>
<ul class="navbar-nav nav-utility me-auto">
    <!-- 公式、精彩操作、個人資料 -->
</ul>
```

5. Rename short labels to `海克斯`、`裝備`、`公式`.
6. Change the main element to:

```html
<main id="main-content" class="main-content" tabindex="-1">
    @RenderBody()
</main>
```

7. Load the existing script before page scripts:

```html
<script src="~/js/site.js" asp-append-version="true"></script>
```

- [x] **Step 4: Add the design tokens and shared primitives**

Replace the default template CSS with:

```css
:root {
  --color-ink-950: #05090c;
  --color-ink-900: #081117;
  --color-ink-850: #0b161d;
  --color-ink-800: #102029;
  --color-line: rgba(144, 171, 181, 0.24);
  --color-text: #edf2ef;
  --color-muted: #9eabb1;
  --color-gold: #c89b3c;
  --color-gold-bright: #e6c66d;
  --color-teal: #29b8b0;
  --color-danger: #d8525c;
  --radius-control: 5px;
  --radius-card: 6px;
  --shadow-raised: 0 16px 36px rgba(0, 0, 0, 0.28);
  --content-max: 1500px;
}

html {
  min-height: 100%;
  font-size: 16px;
  color-scheme: dark;
}

body {
  min-height: 100vh;
  margin: 0;
  color: var(--color-text);
  background: var(--color-ink-950);
  font-family: "Microsoft JhengHei", "Noto Sans TC", "Segoe UI", sans-serif;
}

.main-content {
  width: min(100%, var(--content-max));
  min-height: calc(100vh - 64px);
  margin-inline: auto;
  padding: clamp(18px, 2.4vw, 36px);
}

.game-page {
  display: grid;
  gap: 24px;
}

.game-page-header {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 24px;
  align-items: end;
  padding-bottom: 18px;
  border-bottom: 1px solid var(--color-line);
}

.game-toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  align-items: center;
}

.game-card {
  border: 1px solid var(--color-line);
  border-radius: var(--radius-card);
  background: var(--color-ink-850);
  transition: transform 180ms ease, border-color 180ms ease, box-shadow 180ms ease;
}

.game-card:hover {
  transform: translateY(-3px);
  border-color: rgba(200, 155, 60, 0.55);
  box-shadow: var(--shadow-raised);
}

@media (max-width: 991.98px) {
  .game-page-header {
    grid-template-columns: 1fr;
    align-items: start;
  }
}

@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    scroll-behavior: auto !important;
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
  }
}
```

Extend this same file with the navbar, buttons, forms, focus ring, rarity tokens, `.game-empty-state`, `.game-section-label`, `.game-kicker`, `.game-page-title`, `.game-page-description`, mobile 44px controls, and no-horizontal-overflow rules described in the approved spec.

- [x] **Step 5: Add deterministic page-ready and image fallback behavior**

Use the existing `site.js` for progressive enhancement only:

```javascript
document.documentElement.classList.add("js");

document.addEventListener("DOMContentLoaded", () => {
  document.body.classList.add("page-ready");

  document.querySelectorAll("img[data-fallback-src]").forEach((image) => {
    image.addEventListener("error", () => {
      const fallback = image.dataset.fallbackSrc;
      if (fallback && image.src !== fallback) {
        image.src = fallback;
      }
    }, { once: true });
  });
});
```

CSS must keep the body visible without JavaScript and apply only a short `opacity`/`translateY` reveal to `.js body:not(.page-ready) .main-content`.

- [x] **Step 6: Run shell tests and the full suite**

Run:

```powershell
dotnet test Proposal.Tests\Proposal.Tests.csproj --filter FrontendStructureTests
dotnet test Proposal.Tests\Proposal.Tests.csproj
```

Expected: all frontend structure tests pass and the existing suite remains green.

- [x] **Step 7: Commit**

```powershell
git add Proposal.Tests\FrontendStructureTests.cs Proposal\Views\_Layout.cshtml Proposal\Views\Shared\_Layout.cshtml Proposal\wwwroot\css\site.css Proposal\wwwroot\js\site.js
git commit -m "feat: establish game UI shell"
```

### Task 2: Replace the homepage process wheel with the three-step ARAM flow

**Files:**
- Modify: `Proposal.Tests/FrontendStructureTests.cs`
- Modify: `Proposal/Views/Home/Index.cshtml`
- Modify: `Proposal/wwwroot/css/site.css`

- [x] **Step 1: Add the failing homepage contract test**

Add:

```csharp
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
```

- [x] **Step 2: Run the test to verify it fails**

Run:

```powershell
dotnet test Proposal.Tests\Proposal.Tests.csproj --filter Home_PrioritizesRecommendationFlow
```

Expected: FAIL because the current homepage still renders five absolutely positioned flow cards.

- [x] **Step 3: Replace the homepage markup**

Render:

```html
<section class="home-command" aria-labelledby="home-title">
    <div class="home-command__copy">
        <span class="game-kicker">ARAM Mayhem 決策工具</span>
        <h1 id="home-title">本輪海克斯，選得更有把握</h1>
        <p>鎖定英雄、帶入眼前的三個選項，快速取得海克斯優先級與裝備方向。</p>
        <div class="home-command__actions">
            <a class="btn btn-primary-game" asp-controller="AiRecommendation" asp-action="Index">
                <i class="bi bi-stars" aria-hidden="true"></i>開始 AI 推薦
            </a>
            <a class="btn btn-outline-game" asp-controller="LolAramGuides" asp-action="Index">
                瀏覽英雄
            </a>
        </div>
    </div>

    <div class="home-mission" aria-label="推薦流程">
        <ol class="home-mission__steps">
            <li><span>01</span><strong>選擇英雄</strong><small>先確認技能型態與隊伍功能。</small></li>
            <li><span>02</span><strong>比較三個海克斯</strong><small>依本輪實際選項判斷，不必翻完整資料庫。</small></li>
            <li><span>03</span><strong>取得推薦</strong><small>查看選擇理由、裝備方向與注意事項。</small></li>
        </ol>
        <div class="poro-guide" aria-label="普羅助手">
            <img src="/images/poro-guide.png" data-fallback-src="/favicon.ico" alt="普羅推薦助手">
            <p>把這輪看到的三個海克斯交給我。</p>
        </div>
    </div>
</section>
```

Below the hero add one asymmetric data gateway: a large hero library link and two compact augment/equipment links. Do not restore the five-card process map.

- [x] **Step 4: Add homepage-specific styling**

Add `.home-command`, `.home-command__copy`, `.home-mission`, `.home-mission__steps`, `.poro-guide`, and `.home-library-grid` to `site.css`. Use the existing `/images/守岸.jpg` as a low-contrast background layer, keep the hero under 760px tall, and switch to one column below 992px.

- [x] **Step 5: Run the focused test and build**

Run:

```powershell
dotnet test Proposal.Tests\Proposal.Tests.csproj --filter Home_PrioritizesRecommendationFlow
dotnet build Proposal\Proposal.csproj -c Release
```

Expected: PASS and Release build succeeds.

- [x] **Step 6: Commit**

```powershell
git add Proposal.Tests\FrontendStructureTests.cs Proposal\Views\Home\Index.cshtml Proposal\wwwroot\css\site.css
git commit -m "feat: redesign ARAM command home"
```

### Task 3: Clarify the AI recommendation workflow

**Files:**
- Modify: `Proposal.Tests/FrontendStructureTests.cs`
- Modify: `Proposal/Views/AiRecommendation/Index.cshtml`
- Modify: `Proposal/wwwroot/css/site.css`

- [x] **Step 1: Add the failing AI page structure test**

Add:

```csharp
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
```

- [x] **Step 2: Run the test to verify it fails**

Run:

```powershell
dotnet test Proposal.Tests\Proposal.Tests.csproj --filter AiRecommendation_PresentsFourInputStagesAndDecisionPanel
```

Expected: FAIL because the existing page does not use the approved staged structure.

- [x] **Step 3: Group existing controls without changing their names or bindings**

Wrap the existing champion picker, stage select, augment round inputs, optional notes and submit button in:

```html
<div class="recommendation-stage" data-stage="1">
    <div class="recommendation-stage__number">01</div>
    <div class="recommendation-stage__body">
        <h2>鎖定英雄</h2>
        <!-- existing asp-for champion input and picker link -->
    </div>
</div>
```

Repeat with stages 2-4 using the approved labels. Preserve every existing `asp-for`, hidden field, antiforgery form, `data-save-draft`, element id and JavaScript query target.

- [x] **Step 4: Fill the right-side empty state and normalize result hierarchy**

When `Model.Recommendation` is null, render:

```html
<section class="decision-preview game-empty-state" aria-labelledby="decision-preview-title">
    <span class="game-kicker">推薦預覽</span>
    <h2 id="decision-preview-title">先鎖定英雄，再比較這輪三個選項</h2>
    <ol>
        <li>英雄定位與技能觸發方式</li>
        <li>已選海克斯的套裝與屬性關聯</li>
        <li>裝備方向、風險與替代方案</li>
    </ol>
</section>
```

When a recommendation exists, order its existing content as `推薦選擇`、`選擇原因`、`裝備方向`、`注意事項`; do not alter service data or POST actions.

- [x] **Step 5: Add responsive recommendation layout**

Use a `minmax(340px, 0.78fr) minmax(0, 1.22fr)` desktop grid, one column below 992px, stable minimum result height on desktop, and no fixed height on mobile.

- [x] **Step 6: Run the focused test, full tests and build**

Run:

```powershell
dotnet test Proposal.Tests\Proposal.Tests.csproj --filter AiRecommendation_PresentsFourInputStagesAndDecisionPanel
dotnet test Proposal.Tests\Proposal.Tests.csproj
dotnet build Proposal\Proposal.csproj -c Release
```

Expected: all commands succeed.

- [x] **Step 7: Commit**

```powershell
git add Proposal.Tests\FrontendStructureTests.cs Proposal\Views\AiRecommendation\Index.cshtml Proposal\wwwroot\css\site.css
git commit -m "feat: clarify AI recommendation workflow"
```

### Task 4: Unify heroes, augments and equipment as data libraries

**Files:**
- Modify: `Proposal.Tests/FrontendStructureTests.cs`
- Modify: `Proposal/Views/LolAramGuides/Index.cshtml`
- Modify: `Proposal/Views/LolAramAugments/Index.cshtml`
- Modify: `Proposal/Views/Equipment/Index.cshtml`
- Modify: `Proposal/wwwroot/css/site.css`
- Modify: `Proposal/wwwroot/js/site.js`

- [ ] **Step 1: Add failing library structure tests**

Add:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```powershell
dotnet test Proposal.Tests\Proposal.Tests.csproj --filter LibraryPages_UseSharedHeaderAndToolbar
```

Expected: FAIL until the three pages share the new structure.

- [ ] **Step 3: Normalize each page header and toolbar**

For each page, retain its current form action, query string names, admin checks and inputs, but use:

```html
<header class="game-page-header">
    <div>
        <span class="game-kicker">ARAM MAYHEM</span>
        <h1 class="game-page-title">英雄資料庫</h1>
        <p class="game-page-description">依定位、人工判斷與推薦摘要快速找到本場方向。</p>
    </div>
    <div class="game-toolbar">
        <!-- existing search, filters and authorized actions -->
    </div>
</header>
```

Use the corresponding title and description for augments and equipment. Search remains first; CRUD/import buttons remain behind existing admin conditions.

- [ ] **Step 4: Apply common card interaction classes**

Add `game-card` to the outermost champion, augment and equipment result cards without renaming existing page-specific classes. Keep current image URLs and add:

```html
loading="lazy"
decoding="async"
data-fallback-src="/favicon.ico"
```

Only list images use lazy loading; first-viewport hero/background media must remain eager.

- [ ] **Step 5: Constrain the augment property drawer**

Keep the existing property filters and toggle behavior, but constrain both collapsed and expanded states:

```css
.augment-tag-drawer {
  width: min(680px, calc(100vw - 32px));
  max-height: min(420px, calc(100vh - 120px));
  overflow: auto;
}

.augment-tag-drawer:not(.is-open) {
  width: auto;
  max-width: 100%;
  max-height: 48px;
  overflow: hidden;
}
```

The exact selector must match the existing `#augmentTagDrawer` JavaScript contract; adapt by adding the class rather than changing its id.

- [ ] **Step 6: Preserve equipment selection across filters**

Do not change the existing local/session storage implementation. Add shared selected-state styling based on the current checkbox/selection classes and verify the persisted selected count still updates after a filtered navigation.

- [ ] **Step 7: Run tests and build**

Run:

```powershell
dotnet test Proposal.Tests\Proposal.Tests.csproj --filter LibraryPages_UseSharedHeaderAndToolbar
dotnet test Proposal.Tests\Proposal.Tests.csproj
dotnet build Proposal\Proposal.csproj -c Release
```

Expected: PASS without changing authorization or endpoint behavior.

- [ ] **Step 8: Commit**

```powershell
git add Proposal.Tests\FrontendStructureTests.cs Proposal\Views\LolAramGuides\Index.cshtml Proposal\Views\LolAramAugments\Index.cshtml Proposal\Views\Equipment\Index.cshtml Proposal\wwwroot\css\site.css Proposal\wwwroot\js\site.js
git commit -m "feat: unify ARAM data libraries"
```

### Task 5: Bring secondary, account and error pages into the same product

**Files:**
- Modify: `Proposal/Views/Calculator/Index.cshtml`
- Modify: `Proposal/Views/Media/Highlights.cshtml`
- Modify: `Proposal/Views/User/Profile.cshtml`
- Modify: `Proposal/Views/Account/Login.cshtml`
- Modify: `Proposal/Views/Account/Register.cshtml`
- Modify: `Proposal/Views/Home/NotFoundPage.cshtml`
- Modify: `Proposal/wwwroot/css/site.css`

- [ ] **Step 1: Add shared page wrappers without changing form contracts**

Apply `game-page`, `game-page-header`, `game-page-title`, `game-page-description`, `game-toolbar`, `game-card` and `game-empty-state` to calculator, highlights and profile. Preserve all input names, select ids, fetch URLs and controller actions.

- [ ] **Step 2: Fix calculator select readability using native color rules**

Use:

```css
.calculator-page .form-select {
  color: var(--color-text);
  background-color: var(--color-ink-800);
  border-color: var(--color-line);
}

.calculator-page .form-select option {
  color: #10171b;
  background: #edf3f2;
}

.calculator-page .form-select option:checked {
  color: #071013;
  background: #d5b35c;
}
```

Keep result tables tabular and right-align numeric columns.

- [ ] **Step 3: Reframe highlights and profile**

Highlights use a compact filter header and thumbnail-led list; profile uses account summary, recent recommendations, saved loadouts and recent activity. Empty states use a single useful CTA instead of decorative copy.

- [ ] **Step 4: Align standalone Login and Register pages**

Because these pages use `Layout = null`, link the same `site.css`, add `auth-page`, `auth-panel`, `auth-aside`, and `auth-form` classes, keep antiforgery and validation markup unchanged, and use one centered Poro helper image rather than a large gradient illustration.

- [ ] **Step 5: Align the 404 page**

Use a compact error code, current cat image as optional secondary media, and clear `返回首頁` / `回上一頁` actions. Remove comments visible only to source readers.

- [ ] **Step 6: Run full tests and Release build**

Run:

```powershell
dotnet test Proposal.Tests\Proposal.Tests.csproj
dotnet build Proposal\Proposal.csproj -c Release
```

Expected: all tests pass and Release build succeeds.

- [ ] **Step 7: Commit**

```powershell
git add Proposal\Views\Calculator\Index.cshtml Proposal\Views\Media\Highlights.cshtml Proposal\Views\User\Profile.cshtml Proposal\Views\Account\Login.cshtml Proposal\Views\Account\Register.cshtml Proposal\Views\Home\NotFoundPage.cshtml Proposal\wwwroot\css\site.css
git commit -m "feat: polish secondary and account pages"
```

### Task 6: Verify served desktop/mobile behavior and close regressions

**Files:**
- Modify only files proven necessary by browser findings.
- Update: `docs/superpowers/plans/2026-06-10-game-ui-finalization.md`

- [ ] **Step 1: Run the automated verification gate**

Run:

```powershell
dotnet test Proposal.Tests\Proposal.Tests.csproj
dotnet build Proposal\Proposal.csproj -c Release
powershell -ExecutionPolicy Bypass -File .\Tools\RunLocalSmoke.ps1
```

Expected: test suite, Release build and local HTTP smoke all pass. If the smoke wrapper reports an external database/provider blocker, retain the exact report and still verify public/login pages.

- [ ] **Step 2: Start the app with the repository-safe wrapper**

Use the existing safe launch approach that removes the duplicate `PATH`/`Path` environment entry and starts the app hidden on an unused localhost port. Confirm the health endpoint or homepage returns HTTP 200 before browser inspection.

- [ ] **Step 3: Verify desktop pages in the in-app browser**

Inspect at 1280×900:

- `/`
- `/AiRecommendation`
- `/LolAramGuides`
- `/LolAramAugments`
- `/Equipment`
- `/Calculator`
- `/Media/Highlights`
- `/User/Profile`
- `/Account/Login`
- `/Account/Register`

For authenticated routes, use the existing signed-in browser session only; never place credentials in scripts or source.

- [ ] **Step 4: Verify responsive pages**

Inspect the homepage, AI recommendation, one data library and login at:

- 1024×768
- 768×1024
- 390×844

Check:

```javascript
({
  viewportWidth: window.innerWidth,
  bodyWidth: document.body.scrollWidth,
  horizontalOverflow: document.body.scrollWidth > window.innerWidth,
  navOpen: document.querySelector(".navbar-collapse")?.classList.contains("show")
})
```

Expected: `horizontalOverflow` is `false`; nav content does not overlap; mobile controls are at least 44px high.

- [ ] **Step 5: Check visual and interaction states**

Verify:

- first paint does not show an unstyled vertical card list;
- navbar active state is visible but not glowing;
- keyboard focus is visible;
- card hover moves no more than 3px;
- `prefers-reduced-motion` removes nonessential movement;
- broken champion/augment/equipment images use fallback without layout shift;
- admin actions remain hidden from non-admin users;
- AI form draft, picker return and equipment selection still work.

- [ ] **Step 6: Fix only evidence-backed defects**

For every defect, record the page, viewport, selector and screenshot, then make the smallest CSS/markup change that resolves it. Re-run the focused page and the full automated gate after the final fix.

- [ ] **Step 7: Mark the plan complete and commit**

Update every completed checkbox in this plan, then run:

```powershell
git add Proposal Proposal.Tests docs\superpowers\plans\2026-06-10-game-ui-finalization.md
git commit -m "chore: finalize game UI verification"
```

Expected: clean worktree, all checks green, and browser screenshots demonstrate the approved direction on desktop and mobile.
