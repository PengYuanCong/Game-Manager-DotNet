# 作品集最終收尾設計

## 目標

將目前已可部署的 ARAM AI 推薦系統整理成能被面試官快速理解、能由自動化流程重複驗證，且不需要真實 AI 金鑰也能檢查核心品質的作品集。

本次不增加新的產品頁面，也不擴張遊戲資料來源。收尾只處理四件事：

1. 核心行為的自動化測試。
2. 可離線驗證的 AI 推薦評測資料集。
3. GitHub Actions 持續整合與資安檢查。
4. README 的品質證據與面試展示腳本。

## 測試設計

新增 `Proposal.Tests` xUnit 專案，直接參考 MVC 主專案。測試分成四組：

- `AiRecommendationCacheKeyTests`：驗證輸入正規化、範圍隔離與固定 SHA-256 格式。
- `Pbkdf2PasswordHashServiceTests`：驗證雜湊不可逆、正確與錯誤密碼、舊密碼升級及損壞雜湊。
- `LolAramAugmentTagNormalizerTests`：驗證繁簡中文別名、套裝名稱、重複標籤與效果文字推論。
- `MvcSecurityConventionTests`：以反射驗證需登入的 Controller、管理員 CRUD 與所有 `HttpPost` action 的 antiforgery 標記。

既有 `Tools/RunLocalSmoke.ps1` 繼續負責 HTTP 整合驗證，不為了測試而建立另一套資料庫或把正式服務改成測試專用架構。

## AI 評測資料集

新增 `evaluation/aram-recommendation-cases.json`，收錄 30 組代表性情境，涵蓋：

- AP、AD、坦克、鬥士、輔助與混合輸出英雄。
- 開局、7 等、11 等與 15 等的海克斯選擇。
- 燃燒、爆竹、堆疊暴龍、治療、護盾、技能急速與控制等玩法。
- 大亂鬥禁用或條件式內容，例如守護天使不得推薦、虛弱僅能在「燒起來了」條件下出現。
- 繁體中文裝備名稱、必備概念、可接受裝備池與禁止項目。

資料集不呼叫 OpenRouter，也不宣稱衡量模型的即時正確率。它是可版本控制的離線品質契約，先確保案例結構完整、規則不互相矛盾，之後可再由模型輸出批次對照。

`Tools/ValidateAiEvaluationDataset.ps1` 會驗證案例數量、唯一 ID、必要欄位、階段值、三選一數量、禁止內容及繁體中文命名底線，並輸出明確的通過或失敗摘要。

## CI 與資安

新增 `.github/workflows/ci.yml`，在 push 與 pull request 時執行：

1. `dotnet restore`
2. Release build
3. xUnit 測試
4. AI 評測資料集驗證
5. Supabase schema contract
6. Architecture dependency check
7. Cloud readiness 靜態檢查
8. NuGet 直接與間接相依套件弱點掃描

CI 不持有 OpenRouter、Supabase 或 Render 密鑰，也不連線正式資料庫。所有需要密鑰或寫入資料庫的流程保留在受控部署環境。

## 文件與展示

README 新增：

- CI 徽章。
- 自動化品質檢查摘要。
- AI 評測資料集的用途與限制。
- 三分鐘面試展示順序。

面試展示依序呈現問題、實際推薦流程、RAG 資料、玩家採納回饋、資料庫與部署安全，避免逐頁介紹所有 CRUD。

## 完成條件

只有以下證據全部成立才完成：

- Solution Release build 成功。
- 所有 xUnit 測試通過。
- 30 組 AI 評測案例通過驗證工具。
- Cloud readiness、Supabase contract、architecture check 與 NuGet vulnerability scan 通過。
- Local smoke 與可用的 cloud smoke 通過。
- 變更提交並推送到遠端 `main`，遠端 SHA 與本機 HEAD 一致。

