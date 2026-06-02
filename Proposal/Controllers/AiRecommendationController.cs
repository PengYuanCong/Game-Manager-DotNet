using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proposal.Models;
using Proposal.Services;

namespace Proposal.Controllers
{
    [Authorize]
    public class AiRecommendationController : Controller
    {
        private const string FixedGameTitle = "英雄聯盟 隨機單中大亂鬥";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly IAiRecommendationService _recommendationService;
        private readonly IAiRecommendationCache _recommendationCache;
        private readonly IAiRecommendationFavoriteService _favoriteService;
        private readonly IAiKnowledgeBaseService _knowledgeBaseService;
        private readonly IUserActivityLogService _activityLogService;
        private readonly ILogger<AiRecommendationController> _logger;

        public AiRecommendationController(
            IAiRecommendationService recommendationService,
            IAiRecommendationCache recommendationCache,
            IAiRecommendationFavoriteService favoriteService,
            IAiKnowledgeBaseService knowledgeBaseService,
            IUserActivityLogService activityLogService,
            ILogger<AiRecommendationController> logger)
        {
            _recommendationService = recommendationService;
            _recommendationCache = recommendationCache;
            _favoriteService = favoriteService;
            _knowledgeBaseService = knowledgeBaseService;
            _activityLogService = activityLogService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? favoriteId, CancellationToken cancellationToken)
        {
            if (favoriteId.HasValue)
            {
                var username = User.Identity?.Name ?? "anonymous";
                var favorite = await _favoriteService.GetAsync(username, favoriteId.Value, cancellationToken);
                if (favorite != null)
                {
                    var recommendation = JsonSerializer.Deserialize<GameRecommendation>(favorite.RecommendationJson, JsonOptions);
                    return View(new AiRecommendationPageViewModel
                    {
                        Input = favorite.ToInput(),
                        Recommendation = recommendation,
                        FavoriteId = favorite.Id,
                        WasLoadedFromFavorite = true,
                        WasKnowledgeUsed = true,
                        KnowledgeSourceLabel = "個人收藏案例",
                        KnowledgeSourceUrl = Url.Action("Profile", "User")
                    });
                }

                TempData["Error"] = "找不到這筆收藏推薦，可能已被汰舊換新。";
            }

            return View(new AiRecommendationPageViewModel
            {
                Input = new AiRecommendationInput { GameTitle = FixedGameTitle }
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(
            AiRecommendationPageViewModel model,
            CancellationToken cancellationToken)
        {
            NormalizeInput(model);
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var username = User.Identity?.Name ?? "anonymous";
                var knowledgeContext = await _knowledgeBaseService.GetContextAsync(model.Input, cancellationToken);
                await AddFeedbackMemoryAsync(username, model, knowledgeContext, cancellationToken);
                model.WasKnowledgeUsed = knowledgeContext.HasGuide;
                model.KnowledgeSourceLabel = knowledgeContext.SourceLabel;
                model.KnowledgeSourceUrl = knowledgeContext.SourceUrl;

                var cachedRecommendation = await _recommendationCache
                    .GetAsync(username, model.Input, knowledgeContext.CacheScope, cancellationToken);

                if (cachedRecommendation != null)
                {
                    model.Recommendation = cachedRecommendation;
                    model.WasLoadedFromCache = true;
                    await AddRecommendationActivityAsync(username, model, cancellationToken);
                    return View(model);
                }

                model.Recommendation = await _recommendationService
                    .CreateRecommendationAsync(model.Input, knowledgeContext, cancellationToken);

                await _recommendationCache.SaveAsync(
                    username,
                    model.Input,
                    knowledgeContext.CacheScope,
                    model.Recommendation,
                    cancellationToken);

                await AddRecommendationActivityAsync(username, model, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "AI recommendation failed because of invalid operation.");
                model.ErrorMessage = ex.Message;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "OpenRouter API connection failed.");
                model.ErrorMessage = $"OpenRouter API 連線失敗：{ex.Message}";
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("OpenRouter API request timed out.");
                model.ErrorMessage = "OpenRouter API 請求逾時，請稍後再試。";
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "AI response JSON parsing failed.");
                model.ErrorMessage = $"AI 回應 JSON 解析失敗：{ex.Message}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI recommendation failed unexpectedly.");
                model.ErrorMessage = $"產生推薦時發生未預期錯誤：{ex.Message}";
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Favorite(
            AiRecommendationPageViewModel model,
            string recommendationJson,
            CancellationToken cancellationToken)
        {
            NormalizeInput(model);
            if (string.IsNullOrWhiteSpace(recommendationJson))
            {
                TempData["Error"] = "沒有可收藏的推薦內容。";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var recommendation = JsonSerializer.Deserialize<GameRecommendation>(recommendationJson, JsonOptions);
                if (recommendation == null)
                {
                    TempData["Error"] = "推薦內容解析失敗，請重新產生一次。";
                    return RedirectToAction(nameof(Index));
                }

                var username = User.Identity?.Name ?? "anonymous";
                var favoriteId = await _favoriteService.SaveAsync(username, model.Input, recommendation, cancellationToken);

                await _activityLogService.AddAsync(
                    username,
                    "favorite",
                    $"收藏 AI 推薦：{model.Input.CoreChampion}",
                    string.IsNullOrWhiteSpace(recommendation.Summary) ? "收藏了一筆 AI 推薦案例。" : recommendation.Summary,
                    Url.Action("Index", "AiRecommendation", new { favoriteId }),
                    cancellationToken);

                TempData["Success"] = "已收藏這次 AI 推薦。未來遇到相似英雄、海克斯或裝備時，系統會優先把這筆案例放進 RAG 參考。";
                return RedirectToAction(nameof(Index), new { favoriteId });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Favorite recommendation JSON parsing failed.");
                TempData["Error"] = "收藏失敗：推薦內容 JSON 無法解析，請重新產生一次。";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save AI recommendation favorite.");
                TempData["Error"] = $"收藏失敗：{ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Adopt(
            AiRecommendationPageViewModel model,
            string recommendationJson,
            CancellationToken cancellationToken)
        {
            NormalizeInput(model);
            if (string.IsNullOrWhiteSpace(recommendationJson))
            {
                TempData["Error"] = "沒有可採納的 AI 推薦。";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var recommendation = JsonSerializer.Deserialize<GameRecommendation>(recommendationJson, JsonOptions);
                if (recommendation == null)
                {
                    TempData["Error"] = "推薦內容解析失敗，請重新產生一次。";
                    return RedirectToAction(nameof(Index));
                }

                var username = User.Identity?.Name ?? "anonymous";
                var favoriteId = await _favoriteService.AdoptAsync(username, model.Input, recommendation, cancellationToken);

                await _activityLogService.AddAsync(
                    username,
                    "adopt",
                    $"採納 AI 推薦：{model.Input.CoreChampion}",
                    string.IsNullOrWhiteSpace(recommendation.Summary) ? "採納了一套 ARAM Mayhem 推薦。" : recommendation.Summary,
                    Url.Action("Index", "AiRecommendation", new { favoriteId }),
                    cancellationToken);

                TempData["Success"] = "已採納這套推薦。之後相似英雄、海克斯與裝備情境會優先參考這筆玩家認可答案。";
                return RedirectToAction(nameof(Index), new { favoriteId });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Adopt recommendation JSON parsing failed.");
                TempData["Error"] = "採納失敗：推薦內容格式無法解析。";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to adopt AI recommendation.");
                TempData["Error"] = $"採納失敗：{ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        private async Task AddFeedbackMemoryAsync(
            string username,
            AiRecommendationPageViewModel model,
            AiKnowledgeContext knowledgeContext,
            CancellationToken cancellationToken)
        {
            var favorites = await _favoriteService.FindRelevantAsync(username, model.Input, 3, cancellationToken);
            var communityAccepted = await _favoriteService.FindCommunityAcceptedAsync(model.Input, 3, cancellationToken);
            if (favorites.Count == 0 && communityAccepted.Count == 0)
            {
                return;
            }

            model.WasFavoriteMemoryUsed = favorites.Count > 0 || communityAccepted.Count > 0;
            knowledgeContext.PromptContext = string.Join(
                "\n\n",
                new[]
                {
                    knowledgeContext.PromptContext,
                    favorites.Count == 0 ? null : BuildFavoriteMemoryPrompt(favorites),
                    communityAccepted.Count == 0 ? null : BuildCommunityAdoptionPrompt(communityAccepted)
                }
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
            knowledgeContext.CacheScope = $"{knowledgeContext.CacheScope}|favorites:{string.Join("-", favorites.Select(favorite => $"{favorite.Id}.{favorite.UpdatedAt.Ticks}"))}|adopted:{string.Join("-", communityAccepted.Select(favorite => $"{favorite.Id}.{favorite.AdoptedCount}.{(favorite.LastAdoptedAt ?? favorite.UpdatedAt).Ticks}"))}";
            knowledgeContext.HasGuide = true;

            if (string.IsNullOrWhiteSpace(knowledgeContext.SourceLabel))
            {
                knowledgeContext.SourceLabel = "個人收藏案例";
                knowledgeContext.SourceUrl = Url.Action("Profile", "User");
            }
        }

        private async Task AddRecommendationActivityAsync(
            string username,
            AiRecommendationPageViewModel model,
            CancellationToken cancellationToken)
        {
            if (model.Recommendation == null)
            {
                return;
            }

            var champion = string.IsNullOrWhiteSpace(model.Input.CoreChampion)
                ? model.Recommendation.CoreChampion
                : model.Input.CoreChampion;
            var summary = string.IsNullOrWhiteSpace(model.Recommendation.Summary)
                ? "產生了一次 ARAM Mayhem 推薦。"
                : model.Recommendation.Summary;

            await _activityLogService.AddAsync(
                username,
                "ai",
                $"AI 推薦：{champion}",
                summary,
                Url.Action("Index", "AiRecommendation"),
                cancellationToken);
        }

        private static string BuildFavoriteMemoryPrompt(IReadOnlyList<AiRecommendationFavorite> favorites)
        {
            var lines = new List<string>
            {
                "使用者過去收藏過以下相似推薦案例，代表玩家認可或想保留的偏好。請優先參考，但仍依目前輸入調整："
            };

            foreach (var favorite in favorites)
            {
                lines.Add($"""
                    - 收藏案例 #{favorite.Id}
                      英雄：{TextOrDefault(favorite.CoreChampion)}
                      階段：{TextOrDefault(favorite.CurrentStage)}
                      當時海克斯：{TextOrDefault(favorite.Augment)}
                      當時裝備：{TextOrDefault(favorite.AvailableItems)}
                      收藏摘要：{TextOrDefault(favorite.Summary)}
                      推薦裝備：{TextOrDefault(favorite.RecommendedItems)}
                      推薦海克斯：{TextOrDefault(favorite.RecommendedAugments)}
                    """);
            }

            return string.Join("\n", lines);
        }

        private static string BuildCommunityAdoptionPrompt(IReadOnlyList<AiRecommendationFavorite> favorites)
        {
            var lines = new List<string>
            {
                "Community accepted recommendation memory: prioritize these only when the current champion, augments, items, or play pattern are similar. Do not copy blindly; use them as weighted player feedback."
            };

            foreach (var favorite in favorites)
            {
                lines.Add($"""
                    - Accepted #{favorite.Id} ({favorite.AdoptedCount} adoptions)
                      Champion: {TextOrDefault(favorite.CoreChampion)}
                      Stage: {TextOrDefault(favorite.CurrentStage)}
                      Player augments/items: {TextOrDefault(favorite.Augment)} / {TextOrDefault(favorite.AvailableItems)}
                      Accepted summary: {TextOrDefault(favorite.Summary)}
                      Accepted items: {TextOrDefault(favorite.RecommendedItems)}
                      Accepted augments: {TextOrDefault(favorite.RecommendedAugments)}
                    """);
            }

            return string.Join("\n", lines);
        }

        private static void NormalizeInput(AiRecommendationPageViewModel model)
        {
            model.Input ??= new AiRecommendationInput();
            model.Input.GameTitle = FixedGameTitle;
        }

        private static string TextOrDefault(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "未提供" : value.Trim();
        }
    }
}
