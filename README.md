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

<img width="1915" height="956" alt="image" src="https://github.com/user-attachments/assets/f3faf4e9-d3a9-4d52-a1d8-bf1caa126357" />
<img width="1781" height="960" alt="image" src="https://github.com/user-attachments/assets/542ac653-b1b3-40d3-9f6b-fc8c4b885094" />
<img width="1905" height="961" alt="image" src="https://github.com/user-attachments/assets/fbab1fe1-cd2e-4cf0-a55f-8931ca230e75" />
<img width="1807" height="957" alt="image" src="https://github.com/user-attachments/assets/4fcd91c3-d508-46b6-a86a-1a698e00cf23" />
<img width="1897" height="955" alt="image" src="https://github.com/user-attachments/assets/f4607783-40e0-4d70-b67e-279373875d5b" />
<img width="1903" height="922" alt="image" src="https://github.com/user-attachments/assets/d846f8ac-657c-46f1-9822-0b84d4f6d0bc" />
