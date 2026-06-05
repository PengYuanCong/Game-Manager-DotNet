# 部署與資安檢查

這份文件是網站上 Render 試跑、Azure 前檢查、Supabase 移植時的安全清單。

## 不可提交到 Git

不要把以下內容放進 repo、Razor、JavaScript、截圖或 README 範例實值中：

- OpenRouter API key
- YouTube API key
- Supabase database password
- Supabase `service_role` key
- SQL Server 帳號密碼
- `.env` / `.env.*`
- `appsettings.Development.json`
- `appsettings.Production.json`
- Visual Studio `.vs/`
- `bin/`、`obj/`、`build-check/`

## 必要環境變數

Render 或 Azure App Service 只放在平台 Secret / Environment Variables：

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
Database__Provider=Supabase
ConnectionStrings__DefaultConnection=<server-side Supabase PostgreSQL connection string>
APP_ADMIN_USERS=<comma-separated-admin-usernames>
OpenRouter__ApiKey=<OpenRouter API Key>
YouTubeSettings__ApiKey=<YouTube API Key>
```

本機仍可不設定 `Database__Provider`，程式會安全地預設為 SQL Server。

## Supabase 原則

- MVC 後端使用 server-side PostgreSQL 連線字串。
- 不在前端使用 Supabase database password 或 `service_role` key。
- 目前 schema 已啟用 RLS，但沒有建立公開 policy。
- 若未來要讓瀏覽器直接呼叫 Supabase REST/JS，需要重新設計 Supabase Auth 與 RLS policy。
- 連線字串建議使用 SSL，例如 `SSL Mode=Require`。

## Cookie 與 HTTP

程式目前應維持：

- Production cookie `SecurePolicy=Always`
- Production 啟用 forwarded headers，讓 Render/Azure 反向代理的
  `X-Forwarded-Proto` 在 `UseHttpsRedirection()` 前生效
- Cookie `HttpOnly=true`
- Cookie `SameSite=Lax`
- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `X-Frame-Options: DENY`
- POST 表單使用 Anti-Forgery Token
- CRUD 功能只允許 Admin

部署時不要把 Kestrel 容器直接裸露到公網；應放在 Render/Azure App
Service 等受控反向代理後面。

## 上線前驗證

```powershell
powershell -ExecutionPolicy Bypass -File Tools\CloudReadinessCheck.ps1
powershell -ExecutionPolicy Bypass -File Tools\SafeBuild.ps1 -Output build-check\release-check
dotnet list Proposal\Proposal.csproj package --vulnerable --include-transitive
rg -uuu "sk-|service_role|OPENROUTER|SUPABASE|Password=|ApiKey" .
```

`rg` 可能會掃到環境變數名稱或文件中的 placeholder。只要不是實際 key、實際密碼或可用 connection string，就不是洩漏。

`Tools\CloudReadinessCheck.ps1` 只做建置、Release publish、設定檔檢查、CSRF 覆蓋檢查、高可信度 secret scan 與 NuGet 漏洞檢查；它不會寫資料庫，也不會執行 Supabase migration `--apply`。

本機部署前 smoke 可用臨時站台檢查，腳本會啟動、檢查、再自動關閉站台：

```powershell
powershell -ExecutionPolicy Bypass -File Tools\RunLocalSmoke.ps1 -Port 5226
```

要在本機先驗 cloud preview gate 的包裝流程，可跑：

```powershell
powershell -ExecutionPolicy Bypass -File Tools\RunLocalSmoke.ps1 -Port 5226 -Gate CloudPreview
```

Render 或 Azure 有預覽網址後，先跑不帶帳密的 HTTP smoke：

```powershell
powershell -ExecutionPolicy Bypass -File Tools\HttpSmokeCheck.ps1 -BaseUrl https://your-preview-url
```

這個檢查會驗證公開頁與未登入導頁不會 500、不會出現未處理例外或敏感診斷文字，並確認基本安全 header 有送出。登入後完整功能仍要用測試帳號做人工或 E2E 驗證。

資料庫 secret 設好後，建議使用 cloud preview gate，一次檢查公開頁、未登入導頁、安全 header、`/healthz` 與 `/readyz`：

```powershell
powershell -ExecutionPolicy Bypass -File Tools\RunCloudPreviewSmoke.ps1 -Target Render -BaseUrl https://your-preview-url
```

Azure 預覽也用同一支：

```powershell
powershell -ExecutionPolicy Bypass -File Tools\RunCloudPreviewSmoke.ps1 -Target Azure -BaseUrl https://your-preview-url
```

Render 的 `healthCheckPath` 使用 `/healthz`，只確認容器活著；`/readyz` 會實際對目前 provider 執行 `select 1`，用來確認 Supabase/SQL Server 連線可用。

最後要判斷整個目標是否真的完成，跑總驗收：

```powershell
powershell -ExecutionPolicy Bypass -File Tools\DeploymentCompletionAudit.ps1 -RunRenderPreviewSmoke
```

這支腳本會把本機 readiness、local smoke、Supabase apply 證據與 Render preview smoke 串起來；若缺外部環境，會輸出 `DEPLOYMENT_COMPLETION_STATUS=INCOMPLETE_EXTERNAL_BLOCKERS`。
每次執行都會同步寫出不含機密的 `Reports\deployment-completion-last.json`，方便之後確認目前卡在哪個外部條件。

## Supabase 移轉流程

1. 備份 SQL Server `LOL`。
2. 在 Supabase staging 執行
   `Tools\RunSupabaseStagingMigration.ps1 -InitializeSchema -ConfirmStaging`，
   或以 SQL Editor 手動執行 `database/supabase/0001_schema.sql`。
3. dry-run `Tools\RunSupabaseStagingMigration.ps1` 確認資料筆數、target schema 與 RLS 狀態。
4. 只對 staging 執行 `Tools\RunSupabaseStagingMigration.ps1 -Apply -ConfirmStaging`。
5. 只有空資料庫 demo 才執行 `0002_seed_aram_starter_data.sql`。
6. 設定 `Database__Provider=Supabase` 後測試登入、AI 推薦、英雄、海克斯、裝備、公式計算、個人資料。
7. Render 試跑穩定後，再準備 Azure App Service。

## 回退方案

- SQL Server 是預設 provider。移除 `Database__Provider=Supabase` 即可回到 SQL Server 路徑。
- Supabase migration runner 預設 dry-run，不加 `-Apply -ConfirmStaging` 不會寫資料。
- Render/Azure 的 `ConnectionStrings__DefaultConnection` 應只在平台 secret 中修改，不要提交到 Git。
