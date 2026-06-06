# Proposal 專案上下文

最後更新：2026-05-22

## 專案方向

這是一個以英雄聯盟 ARAM Mayhem 為核心的 MVC Side Project，重點是用本機 RAG 知識庫搭配 OpenRouter LLM，產生英雄、海克斯、裝備與對局情境推薦。

目前主要頁面：

- `AI 推薦`：使用 OpenRouter 模型，結合 SQL Server 中的英雄、海克斯、裝備知識產生推薦。
- `英雄`：管理每位英雄的定位、推薦海克斯、核心裝備與判斷標籤。
- `海克斯知識庫`：管理海克斯顏色、S-E 評級、套裝系列、效果文字與標籤。
- `裝備管理`：匯入英雄聯盟裝備，依價格與屬性篩選，支援六件裝備組合儲存。
- `公式計算`：計算裝備與傷害效益。
- `個人資料`：顯示使用者資料、最近活動、儲存裝備與推薦/公式紀錄。

## 資料庫與 API

- 目前資料庫使用 SQL Server，資料庫名稱為 `LOL`。
- OpenRouter API key 應放在 user-secrets 或環境變數，不要寫死在程式碼。
- RAG SQL 連線若失敗，AI 推薦流程應降級成空知識庫繼續執行，避免整頁炸掉。

## 權限規則

- 管理員由 `AdminUsers` 設定或 `APP_ADMIN_USERS` 環境變數控制。
- 預設管理員包含 `Peng` 與 `admin`。
- 一般使用者可瀏覽、搜尋、使用 AI 推薦、儲存裝備組合。
- 新增、編輯、刪除、匯入等 CRUD/資料維護功能應限制管理員使用。
- 帳號密碼已改成 PBKDF2-SHA256 雜湊儲存；舊明文密碼登入成功後會自動升級。

## UI 原則

- 網站主要使用深色遊戲工具風格，青色作為操作主色。
- 英雄、海克斯、裝備列表都應以卡片式排版呈現，不要退回純文字列表。
- 海克斯與裝備都需要快速篩選按鈕。
- 頁面切換時要避免先閃出未套 CSS 的畫面。
- 行事曆功能已判定不符合目前產品方向，已移除。
- 海克斯標籤篩選已改成左側箭頭抽屜，預設收合，展開時標籤由左往右彈出，避免頂部資訊過多。
- 海克斯頁改成「海克斯軍械庫 / Howling Abyss 控制台」視覺：上方保留搜尋控制台與卡片列表，不顯示玩家不需要的統計摘要。
- 海克斯屬性標籤要避免和套裝系列重複；`疊層`、`成長` 顯示與篩選時合併成 `堆疊暴龍`，`金幣` 類標籤合併成 `錢如雨下`，`鍛造器` 類合併成 `土豪賭客`。
- 海克斯標籤正規化集中在 `LolAramAugmentTagNormalizer`：讀取海克斯列表與 AI RAG 知識時會保留原標籤，並依套裝系列、效果文字補上新標籤，所以舊資料不需要重新匯入也能套用新篩選規則。
- 套裝效果浮動按鈕改成右側小型快捷盤，不再使用大型圓形軌道，避免遮住頁面內容。
- 全站導覽列有目前頁面高亮，背景維持深色遊戲 HUD 風格，避免大面積空洞圓形裝飾；AI、英雄、海克斯、裝備頁的主視覺要保持同一套遊戲工具感。

## 已知重要設計

- 裝備篩選切頁時，六件套裝選取狀態用 `localStorage` 保留，避免從物理致命切到物穿時歸零。
- 海克斯套裝名稱中，舊的 `下雪天` 應顯示為 `雪球系列`，但程式仍保留舊名稱相容。
- 海克斯浮動套裝按鈕用來查看九個套裝效果。
- 個人資料的最近活動只保留每位使用者最新五筆，舊資料自動汰換。
- AI 推薦結果可以收藏，收藏資料每位使用者最多保留 20 筆；產生新推薦時會抓最多 3 筆相似收藏案例放進 RAG prompt，讓相同或接近情境更貼近使用者過去認可的答案。
- 個人資料頁會顯示最新 6 筆 AI 推薦收藏，並提供一鍵帶入 AI 推薦頁。
- 部署與資安注意事項記錄在 `DEPLOYMENT_SECURITY.md`。專案是 ASP.NET Core MVC + SQL Server，不適合直接整包部署到 Netlify；正式公開建議使用可跑 .NET Web App 的平台，機密放環境變數。

## 後續可提升功能

- AI 推薦頁加入「快速提問」模式，讓不想填完整表單的玩家直接問一句話。
- 英雄頁加入角色/傷害型態篩選，例如 AP、AD、坦克、輔助、刺客、消耗。
- AI 推薦結果加入「為什麼推薦」與「替代選項」區塊。
- 裝備與海克斯可加入收藏或常用清單。
- 建立管理員資料品質檢查頁，列出缺圖、缺標籤、缺評級、缺英雄對應的資料。

## 2026-05-22 AI 推薦頁與切頁體感

- 取消 layout 先隱藏主要頁面再等 `app-ready` 的做法，改成短 CSS reveal，避免切換英雄、海克斯、裝備、AI 推薦頁時先空白一段時間。
- AI 推薦頁改成遊戲工具面板感：對局階段使用下拉選單，尚未產生推薦時右側顯示推薦控制台與英雄定位、海克斯互動、裝備路線三個判斷區塊。
- 後續發現頁面 CSS 放在 view 底部會造成換頁時短暫看到未排版 HTML；layout 已新增 `Styles` section，主要頁與管理/詳細頁的 `<style>` 改由 head 輸出，避免 FOUC。

## 2026-05-23 ARAM RAG 人工判斷資料彙整

- 新增 `Tools/AramRagCuration` 維護工具，用公開來源與既有 OP.GG 匯入資料推導英雄/海克斯人工判斷欄位。
- 工具流程：讀取 SQL Server `LOL`、dry-run 產生覆蓋率報告、apply 前建立 `LolAramGuides_CurationBackup_*` 與 `LolAramAugments_CurationBackup_*` 備份表。
- 已套用至資料庫：`LolAramAugments` 的 `Notes`、`SynergyNotes`、`Tags`，以及 `LolAramGuides` 的 `Notes` 和缺漏 `Augments`。
- 最終覆蓋率：海克斯 `Notes/SynergyNotes/Tags` 195/195，英雄 `Notes/Augments` 172/172。主要報告：`Reports/aram-rag-curation-20260523040148.md`，最後 dry-run 報告 `Reports/aram-rag-curation-20260523040158.md` 顯示 0 筆待更新。
- 驗證來源採保守摘要：Riot 官方規則、OP.GG 已匯入候選、ARAMOnly 效果文字、arammayhem.net / ARAM Mayhem Wiki / Mobalytics 統計與社群高層策略，不複製整篇文章。

## 2026-05-23 ARAM 英雄人工標註 V2

- 使用 `Tools/AramManualCuration` 重新校正 `LolAramGuides`。核心原則改為「英雄本體輸出型態優先」，不再因單一海克斯標籤把 AD / 鬥士 / 射手英雄硬轉成 AP 燃燒裝。
- 工具採低頻公開資料讀取，快取在 `Reports/aram-manual-page-cache`，並在 apply 前建立 `dbo.LolAramGuides_ManualCurationBackup_*` 備份表。
- 已套用到 SQL Server `LOL`：172/172 英雄的 `RoleSummary`、`CoreItems`、`SituationalItems`、`Notes` 都有有效內容；套用後 dry-run 顯示 `Guides updated: 0`。
- 驗證指標：`Physical-leaning guides with AP/AP-burn core items: 0`。抽樣：Aatrox 核心為狂妄/焚天/死亡之舞；Akshan 核心為蒐集者/無盡之刃/多明尼克的問候；Brand 才保留 AP 燃燒；Seraphine 轉為護盾治療輔助裝。
- 英雄詳情頁 `Proposal/Views/LolAramGuides/Details.cshtml` 已改成遊戲攻略式 UI：英雄鎖定區、定位摘要、核心裝/情境裝、海克斯徽章、打法筆記；空白或「待人工補齊」不再當主要欄位顯示，Admin 才看到編輯入口。
- 本次驗證：`dotnet build Tools\AramManualCuration\AramManualCuration.csproj` 0 警告 0 錯誤；`dotnet build Proposal\Proposal.csproj --no-restore -p:UseAppHost=false -o build-check\proposal-verify` 0 警告 0 錯誤。一般 build 曾因本機伺服器鎖住 `Proposal.dll` 失敗；停止後正常 build 成功。重啟 localhost 被系統用量審核擋下，未繞過。

## 2026-05-23 AP guide mismatch fix and auth redesign

- Fixed 24 high-confidence `LolAramGuides` rows where AP champions were incorrectly labeled as AD fighter, lethality, or crit builds. The Swain row now uses AP drain/control notes and core items `惡意；蘭德里的折磨；瑞萊的冰晶節杖`.
- Backup table before SQL updates: `dbo.LolAramGuides_ApMismatchBackup_20260523` with 172 rows.
- Update script: `Tools/Sql/fix-aram-guide-ap-mismatches-20260523.sql`.
- Audit report: `Reports/aram-guide-ap-mismatch-audit-20260523.md`.
- Verification query after cleanup: high-confidence AP rows with AD fighter/lethality/crit core patterns = 0.
- Redesigned `Proposal/Views/Account/Login.cshtml` and `Proposal/Views/Account/Register.cshtml` using the frontend-design skill direction: dark League-style auth pages, cyan/gold energy frame, CSS poro visual, and unchanged form field names/actions.

## 2026-05-24 visual refresh follow-up

- Centered the CSS poro in the auth hero panels and added a remote League Wiki poro image as a decorative background fallback/reference. The image is referenced by URL instead of vendored into the repository.
- Redesigned `Proposal/Views/Calculator/Index.cshtml` into a game-style "formula workshop" with a left console, right input/result panels, and retained formula/loadout/chart behavior.
- Redesigned `Proposal/Views/Media/Highlights.cshtml` into a game-style "highlight theater" with a search queue, 16:9 preview stage, and improved local video drag/drop controls.
- Verified build with `dotnet build Proposal\Proposal.csproj --no-restore -p:UseAppHost=false -o build-check\proposal-visual-refresh`; inline JS syntax for Calculator and Highlights passed.

## 2026-05-24 dev server and loadout management

- Added `StartDevServer.cmd` and `StartDevServer.ps1` at repo root. Use `StartDevServer.cmd` as the primary local launcher; it avoids PowerShell `Start-Process` hanging on `Path/PATH` duplicate environment keys. `StartDevServer.ps1 -NoRun` verifies normalization and should report only `Path`.
- `Program.cs` now clears default logging providers and adds Console/Debug only. This prevents local Kestrel requests from failing when Windows EventLog denies writes to `.NET Runtime`.
- Formula/calculator select dropdown option colors were fixed globally in `_Layout.cshtml` and `Shared/_Layout.cshtml`, plus the calculator-specific `.rift-select option`, so native dropdowns no longer show white text on a light option background.
- Equipment loadout management was added: `GetLoadouts`, `UpdateLoadout`, `DeleteLoadout`, and `DeleteInvalidLoadouts` endpoints; equipment page now has a `管理組合` modal for loading, updating, deleting, and cleaning invalid loadouts.
- Cleaned the current `Peng` invalid loadouts from SQL Server `LOL`: `隨便亂配`, `test`, and `精靈set` all referenced deleted equipment ids and were removed. Remaining `Peng` loadouts: 0.
- Verification: `dotnet build Proposal\Proposal.csproj --no-restore -p:UseAppHost=false -o build-check\proposal-final-check` passed with 0 warnings/errors, and `http://localhost:5214/Account/Login` returned HTTP 200.

## 2026-05-24 deployment and Supabase direction

- Verified the local SQL Server login problem is not worth treating as the production path. `sqlcmd` can connect with NTLM, but `Microsoft.Data.SqlClient + Integrated Security=True` fails with SSPI errors even on local TCP and shared-memory variants.
- Production direction is now website hosting plus Supabase PostgreSQL. Supabase cannot be used by only changing `DefaultConnection`, because the app currently uses many `Microsoft.Data.SqlClient` calls and SQL Server T-SQL.
- Added Docker deployment groundwork: root `Dockerfile`, `.dockerignore`, and `render.yaml`.
- Added `Database:Provider` config, defaulting to `SqlServer`. `Program.cs` now logs only a sanitized generic connection summary and no longer parses every connection string with `SqlConnectionStringBuilder`, so a future PostgreSQL string will not crash startup logging.
- Rewrote `DEPLOYMENT_SECURITY.md` as a clean checklist for production secrets, Supabase keys, cookie/HTTP security, and pre-deploy scanning.
- Added `SUPABASE_MIGRATION_PLAN.md`. The recommended migration order is Account/User, AI recommendation and feedback, ARAM guides/augments, then Equipment/Calculator.

## 2026-05-24 account repository extraction

- Started the Supabase migration path by extracting login/register persistence out of `AccountController`.
- Added `IUserAccountRepository` plus `SqlUserAccountRepository`. The controller no longer imports `Microsoft.Data.SqlClient`; SQL Server-specific user lookup, user creation, password update, and password-column schema widening now live in the repository.
- Added `IPasswordHashService` plus `Pbkdf2PasswordHashService`. PBKDF2-SHA256 password hashing and legacy plaintext verification/rehash are now centralized and reusable for the future PostgreSQL repository.
- Registered `IPasswordHashService` and `IUserAccountRepository` in `Program.cs`.
- `AccountController` now only handles form validation, admin-role lookup, auth claims, cookie sign-in, and redirects.
- Verification: `dotnet build Proposal\Proposal.csproj --no-restore -p:UseAppHost=false -o build-check\proposal-account-repository` passed with 0 errors. Existing nullable warnings remain.

## 2026-05-24 user profile calculation history extraction

- Continued the Supabase migration path after an interrupted turn. The interruption happened before verification, not because a command hung.
- Added `ICalculationHistoryRepository` and `SqlCalculationHistoryRepository` for reading and writing `CalculationHistory`.
- `UserController` no longer imports `Microsoft.Data.SqlClient`; profile calculation history now comes from `ICalculationHistoryRepository`.
- `UserController` now shows a generic `資料服務正常` status instead of exposing the SQL Server `DataSource` on the profile page.
- `CalculatorController.SaveRecord` now writes formula history through `ICalculationHistoryRepository`; the duplicate `EnsureCalculationHistoryTable` method was removed from the controller.
- Verification: `dotnet build Proposal\Proposal.csproj --no-restore -p:UseAppHost=false -o build-check\proposal-user-history-repository` passed with 0 errors. Existing nullable warnings remain.

## 2026-05-25 calculator data repository extraction

- Continued the Supabase migration path by extracting Calculator page equipment/loadout reads out of `CalculatorController`.
- Added `ICalculatorDataRepository` and `SqlCalculatorDataRepository`.
- Added `LoadoutStats` model for loadout aggregate stats returned by calculator APIs.
- `CalculatorController.Index` now loads equipment and valid loadout options through `ICalculatorDataRepository`.
- `CalculatorController.GetLoadoutStats` now uses `ICalculatorDataRepository` and no longer opens SQL Server connections directly.
- Registered `ICalculatorDataRepository` in `Program.cs`.
- Verification: `dotnet build Proposal\Proposal.csproj --no-restore -p:UseAppHost=false -o build-check\proposal-calculator-repository` passed with 0 errors. Existing nullable warnings remain.

## 2026-05-25 Codex websocket stability note

- User reported `stream disconnected before completion: websocket closed by server before response.completed` appearing before assistant replies.
- Local checks showed `api.openai.com:443` TCP connectivity succeeds.
- Child shell process contains proxy env vars `HTTP_PROXY`, `HTTPS_PROXY`, `ALL_PROXY`, `GIT_HTTP_PROXY`, and `GIT_HTTPS_PROXY` pointing to `http://127.0.0.1:9`; they are process-level only, not User/Machine environment variables.
- Since child processes cannot change the parent Codex App websocket environment, mitigation is to restart Codex if the error repeats, avoid VPN/proxy/TUN instability when possible, and keep tool outputs short.
- Added `Tools/SafeBuild.ps1` to run builds with output redirected to `build-check/safe-build.log`, returning only status/warning count unless the build fails.

## 2026-05-25 equipment repository extraction

- Continued the Supabase migration path by extracting `EquipmentController` persistence into `IEquipmentRepository` and `SqlEquipmentRepository`.
- `EquipmentController` no longer imports `Microsoft.Data.SqlClient` and no longer opens SQL Server connections directly.
- Equipment list/search/filter, CRUD, Excel import/export data reads, Data Dragon upsert, loadout save/list/update/delete/cleanup now flow through `IEquipmentRepository`.
- `EquipmentController.GetLoadoutStats` now reuses `ICalculatorDataRepository.GetLoadoutStatsAsync`, keeping calculator/loadout stat aggregation in one service.
- Registered `IEquipmentRepository` in `Program.cs`.
- Verification: `Tools/SafeBuild.ps1 -Output build-check\equipment-repository-refactor` passed with 0 errors. Existing nullable/old calculator warnings remain.

## 2026-05-26 AI cache security refactor

- Added `ISqlConnectionFactory` and `SqlConnectionFactory` to centralize SQL Server connection creation without logging or exposing secrets.
- `SqlAiRecommendationCache` and `SqlAiRecommendationFavoriteService` now use the factory instead of reading `DefaultConnection` directly.
- `AiRecommendationCacheKey.Create` now returns the full SHA-256 hex string instead of a 16-character prefix, reducing cache/favorite key collision risk while still fitting existing NVARCHAR(64) columns.
- AI recommendation cache now skips oversized JSON payloads and prunes old cache rows per user after save, keeping at most 80 cache entries per user.
- AI recommendation favorites now validate serialized input/recommendation payload size before saving, preventing unexpectedly huge content from stressing the database.
- Text fields saved into AI cache/favorites are trimmed to their database column limits before parameter binding.
- Verification: `Tools/SafeBuild.ps1 -Output build-check\ai-cache-security-refactor` passed with 0 errors. Existing nullable/old calculator warnings remain.

## 2026-05-26 user activity log security refactor

- `SqlUserActivityLogService` now uses `ISqlConnectionFactory` instead of reading `DefaultConnection` directly.
- Replaced `AddWithValue` with typed SQL parameters for username, category, title, detail, link URL, and limit.
- Kept profile activity retention intentionally small at 5 logs per user, matching the earlier UX decision to show only a small recent history.
- Activity `LinkUrl` is now stored only when it is a local relative URL beginning with `/`; external or protocol-relative URLs are dropped before database save.
- Text fields are trimmed to table column limits before binding.
- Verification: `Tools/SafeBuild.ps1 -Output build-check\activity-log-security-refactor` passed with 0 errors. Existing nullable/old calculator warnings remain.

## 2026-05-26 ARAM repository security refactor

- `SqlLolAramGuideRepository` and `SqlLolAramAugmentRepository` now use `ISqlConnectionFactory` instead of reading `DefaultConnection` directly.
- Removed repository-local connection string readers from both ARAM repositories.
- Search parameters are trimmed before binding, and create/update/import parameters are trimmed to match their database column sizes.
- Nullable text fields now use a shared `DbValueOrNull(value, maxLength)` pattern so oversized manual/imported data is safely bounded before SQL execution.
- Existing typed SQL parameters and parameterized queries were preserved; controller behavior was not changed.
- Verification: `Tools/SafeBuild.ps1 -Output build-check\aram-repository-security-refactor` passed with 0 errors. Existing nullable/old calculator warnings remain.

## 2026-05-26 RAG knowledge service security refactor

- `SqlLolAramKnowledgeBaseService` now uses `ISqlConnectionFactory` instead of reading `DefaultConnection` directly.
- Removed the service-local `GetConnectionString` method.
- Added length limits for user search input, champion prompt context, augment/item prompt context, and final RAG prompt context.
- Champion and augment knowledge still use the same matching/recommendation logic; only prompt/context size is bounded before returning to the AI recommendation pipeline.
- Tag detection now trims combined user/champion text before scanning, preventing oversized prompt text from flowing into tag matching.
- Verification: `Tools/SafeBuild.ps1 -Output build-check\rag-knowledge-service-security-refactor` passed with 0 errors. Existing nullable/old calculator warnings remain.

## 2026-05-26 Supabase schema staging artifacts

- Added `database/supabase/0001_schema.sql` for the first PostgreSQL/Supabase schema pass.
- Added `database/supabase/0002_seed_aram_starter_data.sql` for minimal ARAM Mayhem starter data in a fresh staging database.
- Added `database/supabase/README.md` with migration order, server-side secret handling, RLS expectations, and naming notes.
- Added `SUPABASE_MIGRATION_STATUS.md` as the clean current-status companion to the older migration plan.
- Schema covers app users, activity logs, calculation history, equipment, equipment loadouts, AI recommendation cache/favorites, ARAM guides, augment series, augments, items, and synergy rules.
- Supabase schema intentionally uses snake_case and `jsonb`; future PostgreSQL repositories should map current C# models to this schema.
- RLS is enabled in the schema but no public policies are added yet. Current MVC deployment should keep Supabase credentials on the server only.
- Runtime remains SQL Server for now: `render.yaml` and `deployment.env.example` still use `Database__Provider=SqlServer` until PostgreSQL repositories and data migration are verified.

## 2026-05-26 remaining SQL repository connection cleanup

- `SqlUserAccountRepository`, `SqlCalculationHistoryRepository`, `SqlCalculatorDataRepository`, and `SqlEquipmentRepository` now receive `ISqlConnectionFactory`.
- Removed repository-local `GetConnectionString()` usage from those four repositories.
- All remaining connection creation in those repositories now flows through `_connectionFactory.Create()`.
- Fixed three broken fallback strings in `SqlCalculationHistoryRepository` after a mechanical encoding-sensitive rewrite exposed the old mojibake as invalid C#.
- Verification: `Tools/SafeBuild.ps1 -Output build-check\sql-factory-repository-cleanup` passed with 0 errors. Existing nullable/old calculator warnings remain.

## 2026-05-26 PostgreSQL core repository start

- Added NuGet package `Npgsql` 10.0.2 to `Proposal/Proposal.csproj`.
- Added `IPostgresConnectionFactory` and `PostgresConnectionFactory` for server-side Supabase/PostgreSQL connections.
- Added PostgreSQL implementations for:
  - `IUserAccountRepository` via `PostgresUserAccountRepository`.
  - `ICalculationHistoryRepository` via `PostgresCalculationHistoryRepository`.
  - `IUserActivityLogService` via `PostgresUserActivityLogService`.
  - `IAiRecommendationCache` via `PostgresAiRecommendationCache`.
  - `IAiRecommendationFavoriteService` via `PostgresAiRecommendationFavoriteService`.
- PostgreSQL implementations use parameterized Npgsql commands, bounded string lengths, local-only activity links, and `jsonb` for AI payloads.
- These PostgreSQL services are not wired as the active runtime provider yet because ARAM guide/augment/knowledge and equipment/calculator repositories still need PostgreSQL versions.
- Verification: `Tools/SafeBuild.ps1 -Output build-check\postgres-ai-repositories` passed with 0 errors. `dotnet list Proposal\Proposal.csproj package --vulnerable --include-transitive` reported no vulnerable packages from configured sources after running outside the sandbox because the sandbox proxy pointed at `127.0.0.1:9`.

## 2026-05-26 PostgreSQL provider wiring

- Added PostgreSQL implementations for equipment, loadouts, and calculator data:
  - `PostgresEquipmentRepository`
  - `PostgresCalculatorDataRepository`
- Added PostgreSQL implementations for the ARAM/RAG core:
  - `PostgresLolAramGuideRepository`
  - `PostgresLolAramAugmentRepository`
  - `PostgresLolAramKnowledgeBaseService`
- `Program.cs` now switches service registrations by `Database:Provider`.
  SQL Server remains the default; `Postgres`, `PostgreSQL`, and `Supabase`
  select the PostgreSQL implementations.
- ARAM guide/augment admin duplicate-key handling now accepts both SQL Server
  and PostgreSQL unique-constraint exceptions.
- PostgreSQL RAG lookup matches champion guides by champion key, English name,
  localized name, or clicked input text, then combines matched augment, item,
  series, and synergy-rule context for OpenRouter prompts.
- Verification: `Tools/SafeBuild.ps1 -Output build-check\postgres-core-services`
  and `Tools/SafeBuild.ps1 -Output build-check\postgres-provider-switch` both
  passed with 0 errors. Existing nullable/old calculator warnings remain.

## 2026-05-26 Supabase data migrator

- Added `Tools/SupabaseDataMigrator`, a conservative SQL Server to
  Supabase/PostgreSQL staging copy tool.
- The tool is dry-run by default and only writes with `--apply`.
- It copies allowlisted tables only, logs table names/counts instead of
  connection strings, and supports `--tables` to narrow scope.
- Updated `database/supabase/README.md` to run schema first, then dry-run/apply
  the migrator, and only use starter seed data for intentionally empty/demo
  staging databases.
- Updated `render.yaml` and `deployment.env.example` to use
  `Database__Provider=Supabase` for cloud staging.
- Verification: `dotnet build Tools\SupabaseDataMigrator\SupabaseDataMigrator.csproj --nologo`
  passed with 0 warnings and 0 errors. Running the tool without connection
  strings prints usage and exits before any database write. `Tools/SafeBuild.ps1
  -Output build-check\supabase-migrator-and-provider` passed with 0 warnings and
  0 errors for the MVC app.

## 2026-05-26 deployment security refresh

- Rewrote `DEPLOYMENT_SECURITY.md` into a readable Traditional Chinese
  deployment/security checklist for Render, Azure, Supabase, secrets, cookies,
  HTTP headers, migration order, verification commands, and rollback.
- Replaced a fake password in `Tools/SqlConnectionProbe` with a placeholder to
  avoid secret-scan false positives.
- Verification:
  - `dotnet list Proposal\Proposal.csproj package --vulnerable --include-transitive`
    completed against NuGet outside the sandbox and reported no vulnerable
    packages.
  - `dotnet build Tools\SqlConnectionProbe\SqlConnectionProbe.csproj --nologo`
    passed with 0 warnings and 0 errors.
  - `Tools\SafeBuild.ps1 -Output build-check\post-security-doc-refresh` passed
    with 0 warnings and 0 errors.
  - Secret scan found only expected variable names/documented scanner text and
    the local `storedPassword` variable, not concrete API keys or passwords.

## 2026-05-26 release warning cleanup

- Cleaned low-risk release warnings in `MediaController`, `YouTubeSettings`,
  `VideoItem`, `Loadout`, `CalculationHistory`, `Equipment`, and the legacy
  calculator POST action.
- `MediaController` now returns 503 when the YouTube API key is missing instead
  of building an outbound request with an empty key.
- Model string properties that are intended to be non-null now default to
  `string.Empty`.
- Verification:
  - `Tools\SafeBuild.ps1 -Output build-check\release-warning-cleanup-final`
    passed with 0 warnings and 0 errors.
  - `dotnet publish Proposal\Proposal.csproj --configuration Release --output
    build-check\publish-smoke --no-restore -p:UseAppHost=false --nologo`
    completed with 0 warnings and produced `build-check\publish-smoke`.

## 2026-05-26 migrator and cloud proxy hardening

- `Tools/SupabaseDataMigrator` now validates SQL Server source columns before
  copy:
  - Missing required columns mark that table as `STATUS=ERROR`.
  - Missing optional columns are reported as `OPTIONAL_COLUMNS_OMITTED` and the
    Supabase default/nullable target value is used.
  - SQL Server metadata queries now use typed parameters instead of
    `AddWithValue`.
- Production now enables ASP.NET Core forwarded headers before HTTPS redirect so
  Render/Azure reverse-proxy `X-Forwarded-Proto` is honored.
- `DEPLOYMENT_SECURITY.md` now notes that Kestrel should stay behind a managed
  Render/Azure reverse proxy rather than being exposed directly.
- Verification:
  - `dotnet build Tools\SupabaseDataMigrator\SupabaseDataMigrator.csproj --nologo`
    passed with 0 warnings and 0 errors.
  - `Tools\SafeBuild.ps1 -Output build-check\azure-forwarded-headers-final`
    passed with 0 warnings and 0 errors.
  - `dotnet publish Proposal\Proposal.csproj --configuration Release --output
    build-check\publish-smoke --no-restore -p:UseAppHost=false --nologo`
    completed with 0 warnings.
  - Running the migrator without connection strings still prints usage and exits
    before any database write.
  - Secret scan still found only expected environment variable names, scanner
    text, and the local `storedPassword` variable.

## 2026-05-26 final pre-cloud security pass

- Hardened account and admin behavior:
  - Production no longer falls back to built-in `Peng`/`admin` administrator
    names when `AdminUsers` or `APP_ADMIN_USERS` is missing.
  - New registrations require a 3-40 character ASCII username made of letters,
    digits, `_`, `-`, or `.`.
  - Logout now uses POST with antiforgery validation instead of a GET link.
- Hardened mutation endpoints:
  - All controller `[HttpPost]` actions now have `[ValidateAntiForgeryToken]`.
  - Equipment delete was changed from a GET link to a POST form.
  - Equipment loadout AJAX writes and calculator history writes now include the
    antiforgery token header.
- Hardened uploads:
  - Excel equipment import is limited to `.xlsx`/`.xls` files up to 5 MB.
  - Avatar upload is limited to JPEG files up to 2 MB, checks the JPEG magic
    header, and writes to a sanitized avatar filename.
- Verification:
  - `Tools\SafeBuild.ps1 -Output build-check\avatar-upload-hardening` passed
    with 0 warnings and 0 errors.
  - `dotnet publish Proposal\Proposal.csproj --configuration Release --output
    build-check\publish-final --no-restore -p:UseAppHost=false --nologo`
    completed successfully.
  - `dotnet list Proposal\Proposal.csproj package --vulnerable
    --include-transitive` completed against NuGet and reported no vulnerable
    packages.
  - `git diff --check` passed; only LF-to-CRLF warnings were reported.
  - Secret scan still found only expected environment variable names, scanner
    text, and the local `storedPassword` variable.
- Local smoke:
  - Starting the app with `Start-Process ... -UseNewEnvironment` failed because
    the isolated environment removed a path value required by NuGet
    (`Value cannot be null. (Parameter 'path1')`).
  - Starting the app again without `-UseNewEnvironment` succeeded on
    `http://localhost:5214`.
  - `GET /Account/Login` returned 200.
  - `GET /` returned 200.
  - `GET /Equipment` redirected to login when unauthenticated.
  - A login POST with an invalid smoke-test user and a valid antiforgery token
    returned 200 without a server exception.
  - Browser plugin verification could not run in this session because the local
    browser runtime failed to initialize with `failed to write kernel assets:
    系統找不到指定的路徑。`.
- Remaining external blockers:
  - Supabase/PostgreSQL `DefaultConnection` is not configured locally.
  - Docker is not installed locally, so Docker/Render image smoke cannot run on
    this machine.
  - Render/Azure smoke tests still require platform-side secrets and connection
    strings.

## 2026-05-27 cloud readiness automation

- Added `Tools\CloudReadinessCheck.ps1` as a repeatable pre-cloud gate.
- Added `Tools\HttpSmokeCheck.ps1` for Render/Azure preview URLs. It checks
  public pages and unauthenticated redirects for 500 responses and exception
  pages without needing credentials.
- Updated `Tools\SafeBuild.ps1` to write per-output log files instead of always
  using `build-check\safe-build.log`, preventing false build failures when two
  checks run at the same time.
- The script checks required deployment files, `render.yaml` secret handling,
  controller antiforgery coverage, high-confidence secret leaks, local cloud
  environment variables, Docker availability, MVC build, Supabase migrator
  build, Release publish, and NuGet package vulnerabilities.
- The script is conservative: it does not write to any database and never runs
  `Tools\SupabaseDataMigrator --apply`.
- Updated `DEPLOYMENT_SECURITY.md`, `database/supabase/README.md`, and
  `deployment.env.example` to reference the readiness/smoke scripts and avoid
  putting concrete admin names in env examples.
- Verification:
  - `powershell -ExecutionPolicy Bypass -File Tools\CloudReadinessCheck.ps1`
    completed with 0 failures after the SafeBuild log fix.
  - It reported 7 expected warnings: cloud runtime env vars are not configured
    locally, migration env vars are not configured locally, and Docker is not
    installed locally.
  - A second run with `-SkipNetworkChecks` completed with 0 failures after the
    documentation/env example updates.
  - `powershell -ExecutionPolicy Bypass -File Tools\HttpSmokeCheck.ps1 -BaseUrl
    http://localhost:5214` passed with 0 failures. Protected pages redirected
    to login instead of throwing.
- Remaining external blockers:
  - Supabase/PostgreSQL `DefaultConnection` is still not configured locally.
  - Supabase migration apply cannot be verified until
    `MIGRATION_SQLSERVER_CONNECTION` and `MIGRATION_POSTGRES_CONNECTION` are set
    for a staging database.
  - Render Docker smoke still requires Docker locally or a Render environment
    with platform secrets configured.

## 2026-05-27 health and readiness endpoints

- Added `/healthz` and `/readyz` to the MVC app:
  - `/healthz` returns a lightweight 200 liveness response and does not touch
    the database.
  - `/readyz` opens the configured provider connection and runs `select 1`.
    It returns 200 when the database is reachable and 503 with only the provider
    name/error type when not ready.
- Updated `render.yaml` to use `healthCheckPath: /healthz` so Render checks the
  container liveness without depending on home page rendering or database state.
- Updated `Tools\HttpSmokeCheck.ps1` to always check `/healthz` and optionally
  check `/readyz` with `-CheckReadiness`.
- Updated `Tools\CloudReadinessCheck.ps1` to require
  `healthCheckPath: /healthz` in `render.yaml`.
- Updated `DEPLOYMENT_SECURITY.md` and `database/supabase/README.md` with
  `/healthz`, `/readyz`, and `HttpSmokeCheck -CheckReadiness` usage.
- Verification:
  - `Tools\SafeBuild.ps1 -Output build-check\health-readiness-final` passed
    with 0 warnings and 0 errors.
  - `Tools\CloudReadinessCheck.ps1` completed with 0 failures; remaining
    warnings are the expected missing local cloud/migration env vars and missing
    Docker command.
  - `git diff --check` passed; only LF-to-CRLF warnings were reported.
  - A fresh temporary run on `http://localhost:5220` returned
    `/healthz` 200 with `{"status":"ok","service":"proposal-aram-assistant"}`.
  - A fresh temporary run on `http://localhost:5221` returned `/readyz` 503 with
    `{"status":"not_ready","databaseProvider":"SqlServer","error":"SqlException"}`,
    confirming readiness fails closed without exposing a connection string.
  - A stale interrupted test process was still bound to port 5215 and returned
    old 404 behavior; it was not stopped automatically to avoid killing a
    potentially user-visible process.

## 2026-05-27 Supabase schema contract guard

- Added `Tools\SupabaseContractCheck.ps1`.
- The script parses `database\supabase\0001_schema.sql`, confirms the required
  Supabase tables exist, confirms RLS is enabled for those tables, and scans the
  `Proposal\Services\Postgres*.cs` repositories for missing table references and
  common column-contract drift.
- Integrated the contract guard into `Tools\CloudReadinessCheck.ps1`, so cloud
  preflight now fails before deploy if PostgreSQL repositories drift away from
  the Supabase schema.
- Updated `database\supabase\README.md` with direct usage.
- Verification:
  - `powershell -ExecutionPolicy Bypass -File Tools\SupabaseContractCheck.ps1`
    passed with 0 failures.
  - `Tools\CloudReadinessCheck.ps1 -SkipNetworkChecks` passed with 0 failures
    after the contract guard was integrated.
  - Full `Tools\CloudReadinessCheck.ps1` passed with 0 failures when run with
    network access for the NuGet vulnerability check; the first sandboxed run
    failed only because NuGet was redirected to `127.0.0.1:9`.
  - A fresh local app run on `http://localhost:5223` passed
    `Tools\HttpSmokeCheck.ps1` with 0 failures, then the temporary process was
    stopped.
- Note: this Windows PowerShell host returned null for
  `ProcessStartInfo.Environment` and `.EnvironmentVariables`; using the full
  dotnet path and redirecting output still avoided the Path/PATH startup issue
  for the local smoke test.

## 2026-06-01 Azure and architecture preflight guard

- Added `Tools\ArchitectureDependencyCheck.ps1`.
  - Verifies controllers/views/wwwroot do not directly open SQL Server or
    PostgreSQL connections/commands.
  - Verifies client-rendered files do not expose server-side secret names or
    connection strings.
  - Verifies production security markers in `Program.cs`: Secure/HttpOnly/Lax
    cookies, forwarded headers, HTTPS redirection, HSTS, security headers,
    `/healthz`, `/readyz`, and database provider switching.
- Added `Tools\AzurePreflightCheck.ps1`.
  - Verifies Docker runtime image, `ASPNETCORE_URLS=http://+:8080`,
    `ASPNETCORE_ENVIRONMENT=Production`, `EXPOSE 8080`, and app entrypoint.
  - Verifies `deployment.env.example` includes `WEBSITES_PORT=8080`,
    Supabase provider, required app settings, and no concrete secret values.
  - Verifies production has no default fallback admin users.
- Integrated both checks into `Tools\CloudReadinessCheck.ps1`.
- Updated `deployment.env.example` with `WEBSITES_PORT=8080`.
- Updated `database\supabase\README.md` and `SUPABASE_MIGRATION_STATUS.md`
  with the new guards.
- Verification:
  - `Tools\AzurePreflightCheck.ps1` passed with 0 failures; only expected
    warnings for missing local Azure app settings.
  - `Tools\ArchitectureDependencyCheck.ps1` passed with 0 failures.
  - `Tools\CloudReadinessCheck.ps1 -SkipNetworkChecks` passed with 0 failures.
  - Full `Tools\CloudReadinessCheck.ps1` passed with 0 failures when run with
    network access for NuGet; the sandboxed run failed only because NuGet was
    redirected to `127.0.0.1:9`.
  - A fresh local app run on `http://localhost:5224` passed
    `Tools\HttpSmokeCheck.ps1` with 0 failures, then the temporary process was
    stopped.

## 2026-06-01 Supabase migrator apply safety

- Hardened `Tools\SupabaseDataMigrator`.
  - `--apply` now requires `--confirm-staging` or
    `MIGRATION_CONFIRM_STAGING=true`; otherwise it exits before opening
    database connections and writes nothing.
  - Dry-run and apply now validate target Supabase table existence, mapped
    target columns, and RLS enablement before copy.
  - Apply mode now wraps all PostgreSQL writes in a transaction and rolls back
    on copy failure.
  - Output now includes source row counts, target row counts before copy, and
    target row counts after copy.
- Updated `Tools\CloudReadinessCheck.ps1` to enforce migrator safety markers:
  `--confirm-staging`, `MIGRATION_CONFIRM_STAGING`,
  `ValidateTargetSchemaAsync`, `BeginTransactionAsync`, `RollbackAsync`, and
  `RlsEnabled`.
- Updated `Tools\SupabaseDataMigrator\README.md`,
  `database\supabase\README.md`, and `SUPABASE_MIGRATION_STATUS.md` with the
  safer apply command and preflight behavior.
- Verification:
  - Running the migrator with no connection strings prints usage and returns
    non-zero without writing.
  - Running with fake source/target and `--apply` but no staging confirmation
    returns `ERROR=Apply mode requires --confirm-staging or
    MIGRATION_CONFIRM_STAGING=true` and `RESULT=No data was written`.
  - `dotnet build Tools\SupabaseDataMigrator\SupabaseDataMigrator.csproj`
    passed with 0 warnings and 0 errors.
  - `Tools\CloudReadinessCheck.ps1 -SkipNetworkChecks` passed with 0 failures.
  - Full `Tools\CloudReadinessCheck.ps1` passed with 0 failures when run with
    network access for NuGet vulnerability checks.
  - A fresh local app run on `http://localhost:5225` passed
    `Tools\HttpSmokeCheck.ps1` with 0 failures, then the temporary process was
    stopped.

## 2026-06-01 preview smoke hardening

- `Tools\HttpSmokeCheck.ps1` now checks baseline response security headers by
  default: `X-Content-Type-Options`, `Referrer-Policy`, and `X-Frame-Options`.
- The smoke check also fails if a page renders common exception-page markers or
  sensitive diagnostic text such as connection string names.
- `/healthz` is parsed as JSON and must return `status=ok`.
- `/readyz` can be verified with `-CheckReadiness`; cloud previews should also
  pass `-ExpectedReadyProvider Supabase` after platform secrets are configured.
- Added `Tools\RunLocalSmoke.ps1` to start a temporary app instance, wait for
  `/healthz`, run the hardened `HttpSmokeCheck`, and stop the process in a
  `finally` block. This avoids relying on a manually running localhost server.

## 2026-06-01 Supabase staging migration runner

- Added `Tools\RunSupabaseStagingMigration.ps1` as the safe entrypoint for
  staging migration.
- The runner fails before opening database connections when
  `MIGRATION_SQLSERVER_CONNECTION` or `MIGRATION_POSTGRES_CONNECTION` is
  missing.
- The runner is dry-run by default. Apply mode requires `-Apply
  -ConfirmStaging` or `MIGRATION_CONFIRM_STAGING=true`, then delegates to the
  transaction-protected `Tools\SupabaseDataMigrator`.

## 2026-06-01 cloud preview smoke gate

- Added `Tools\RunCloudPreviewSmoke.ps1` for Render/Azure preview URLs.
- The script accepts `-Target Render|Azure`, `-BaseUrl`, and defaults to
  requiring `/readyz` with `databaseProvider=Supabase`.
- Cloud preview URLs must use HTTPS unless `-AllowHttp` is explicitly passed
  for local diagnostics. Missing URLs fail before any network call.

## 2026-06-01 deployment completion audit

- Added `Tools\DeploymentCompletionAudit.ps1` as the final evidence gate for
  this long-running goal.
- The audit runs cloud readiness, local smoke, checks Supabase migration apply
  evidence, and verifies whether Render preview smoke was run.
- The audit exits with `DEPLOYMENT_COMPLETION_STATUS=INCOMPLETE_EXTERNAL_BLOCKERS`
  when external staging secrets or preview URLs are missing; this is expected
  locally and prevents falsely marking the goal complete.
- Each audit run writes a secret-free JSON report to
  `Reports\deployment-completion-last.json`.
