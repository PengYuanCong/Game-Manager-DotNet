# 彭彭遊戲基地 - ARAM AI 配裝推薦系統

這個專案原本是 ASP.NET Core MVC 的遊戲裝備管理系統，現在延伸成以 **英雄聯盟隨機單中大亂鬥 ARAM Mayhem** 為核心的 AI 推薦網站。系統會結合英雄資料、海克斯知識庫、裝備屬性與玩家收藏回饋，協助玩家在不同對局階段快速判斷「這一輪三選一該選什麼」以及後續裝備方向。

![彭彭遊戲基地首頁](docs/images/readme-home.png)

## 主要功能

- **AI 推薦**：輸入英雄、對局階段、當輪三選一海克斯與備註，透過本機 RAG 知識庫搭配 OpenRouter 產生推薦。
- **英雄知識庫**：管理 ARAM Mayhem 英雄定位、推薦海克斯、打法筆記與人工標註。
- **海克斯知識庫**：依稀有度、套裝系列與屬性標籤整理海克斯，支援篩選與管理員 CRUD。
- **裝備管理**：匯入英雄聯盟裝備資料，支援屬性篩選、套裝儲存、公式計算與裝備介紹。
- **玩家回饋**：收藏或採納 AI 推薦，讓後續相似情境能參考使用者認可的答案。
- **個人資料**：保存近期查詢、推薦紀錄與裝備組合，採汰舊換新方式避免資料無限制成長。
- **部署準備**：提供 Supabase PostgreSQL schema、資料搬移工具、Dockerfile、Render 設定與部署檢查腳本。

## 技術架構

- **Backend**：ASP.NET Core MVC (`net10.0`)
- **Database**：SQL Server / Supabase PostgreSQL，可透過 `Database__Provider` 切換
- **AI Provider**：OpenRouter Chat Completions
- **Frontend**：Razor Views、Bootstrap 5、Bootstrap Icons、客製暗色遊戲風格 UI
- **Data Tools**：Data Dragon / OP.GG 參考資料匯入工具、人工標註輔助工具
- **Deployment**：Docker、Render blueprint、Supabase migration scripts

## 快速開始

```powershell
dotnet restore Proposal.slnx
dotnet build Proposal.slnx
powershell -ExecutionPolicy Bypass -File StartDevServer.ps1 -Port 5214
```

開啟：

```text
http://localhost:5214
```

本機開發需要自行設定 `Proposal/appsettings.Development.json` 或 `dotnet user-secrets`。請不要把真實 API key、資料庫密碼或 Supabase secret key 提交到 Git。

## 必要設定

本機或雲端執行時常用環境變數：

```text
Database__Provider=SqlServer 或 Supabase
ConnectionStrings__DefaultConnection=<server-side database connection string>
OpenRouter__ApiKey=<OpenRouter API key>
APP_ADMIN_USERS=<admin account list>
YouTubeSettings__ApiKey=<optional YouTube API key>
```

資料搬移到 Supabase 時，請只在本機 shell 或雲端 secret manager 設定：

```text
MIGRATION_SQLSERVER_CONNECTION=<source SQL Server connection string>
MIGRATION_POSTGRES_CONNECTION=<target Supabase PostgreSQL connection string>
```

## 驗證與部署檢查

```powershell
dotnet build Proposal.slnx
powershell -ExecutionPolicy Bypass -File Tools\CloudReadinessCheck.ps1
powershell -ExecutionPolicy Bypass -File Tools\RunLocalSmoke.ps1 -Port 5226
```

Supabase schema 檢查：

```powershell
powershell -ExecutionPolicy Bypass -File Tools\SupabaseContractCheck.ps1
```

Supabase staging 搬移預設為 dry run，不會寫入資料：

```powershell
powershell -ExecutionPolicy Bypass -File Tools\RunSupabaseStagingMigration.ps1
```

確認 staging 環境與連線字串無誤後，才執行正式寫入：

```powershell
powershell -ExecutionPolicy Bypass -File Tools\RunSupabaseStagingMigration.ps1 -Apply -ConfirmStaging
```

## 資安原則

- 不提交 `appsettings.json`、`appsettings.Development.json`、`.env`、log、報告快取或任何真實密鑰。
- Supabase database password 與 `service_role` key 只能放在後端 secret manager。
- 前端不得暴露 OpenRouter API key、資料庫連線字串或管理員設定。
- 部署前請先跑 `Tools\CloudReadinessCheck.ps1`，它會檢查 secret scan、antiforgery、RLS schema、Docker/Render/Azure 基本設定。

## 專案狀態

目前已完成 ARAM AI 推薦、英雄/海克斯/裝備資料管理、Supabase schema 與部署檢查工具。正式上線前仍需要完成真實 Supabase staging migration、設定雲端 secret，以及使用 Render/Azure preview URL 跑完整 smoke test。
