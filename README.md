# ⚔️ Game-Manager-DotNet

一個專為 遊戲 玩家設計的綜合管理系統，基於 ASP.NET Core MVC 開發。

## 🌟 核心功能
- **裝備管理庫**：支援單件裝備的 CRUD 操作，並整合 **ClosedXML** 實現 Excel 資料匯入與導出。
- **組合計算機**：自定義「裝備組合 (Loadouts)」，系統自動執行資料庫層級的屬性加總，一鍵計算 EHP 與 CP 值。
- **影音整合中心**：串接 **YouTube Data API v3**，支援關鍵字搜尋、影片預覽及本地影片拖曳上傳播放。
- **資安實踐**：使用 **Secret Manager** 管理 API 金鑰，確保開發環境安全性。

## 🛠️ 技術棧
- **Framework**: ASP.NET Core 8.0 MVC
- **Database**: SQL Server (Entity Framework / ADO.NET)
- **Frontend**: Bootstrap 5, JavaScript (Async/Await), Bootstrap Icons
- **Tools**: NuGet (ClosedXML, Microsoft.Data.SqlClient)

## 📋 快速開始
1. Clone 此專案。
2. 於 `appsettings.json` 設定您的 SQL Server 連線字串。
3. 透過 `dotnet user-secrets` 設定您的 YouTube API Key。
4. 執行 `Update-Database` 完成資料庫遷移。
