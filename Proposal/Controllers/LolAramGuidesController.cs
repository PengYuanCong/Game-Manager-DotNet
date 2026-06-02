using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Npgsql;
using Proposal.Models;
using Proposal.Services;

namespace Proposal.Controllers
{
    [Authorize]
    public class LolAramGuidesController : Controller
    {
        private readonly ILolAramGuideRepository _repository;
        private readonly ILolAramAugmentRepository _augmentRepository;
        private readonly IOpGgAramMayhemChampionAugmentScraper _opGgChampionScraper;
        private readonly ILogger<LolAramGuidesController> _logger;

        public LolAramGuidesController(
            ILolAramGuideRepository repository,
            ILolAramAugmentRepository augmentRepository,
            IOpGgAramMayhemChampionAugmentScraper opGgChampionScraper,
            ILogger<LolAramGuidesController> logger)
        {
            _repository = repository;
            _augmentRepository = augmentRepository;
            _opGgChampionScraper = opGgChampionScraper;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? searchString, string? pick, CancellationToken cancellationToken)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["PickMode"] = pick;
            var guides = await _repository.SearchAsync(searchString, cancellationToken);
            ViewData["AugmentCatalog"] = await BuildAugmentCatalogAsync(cancellationToken);
            return View(guides);
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
        {
            var guide = await _repository.FindAsync(id, cancellationToken);
            if (guide == null)
            {
                return RedirectToAction(nameof(Index));
            }

            ViewData["AugmentCatalog"] = await BuildAugmentCatalogAsync(cancellationToken);
            return View(guide);
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            return View(new LolAramGuide());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(LolAramGuide guide, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return View(guide);
            }

            try
            {
                await _repository.CreateAsync(guide, cancellationToken);
                TempData["Success"] = "知識已新增。";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex) when (IsUniqueConstraintError(ex))
            {
                ModelState.AddModelError(string.Empty, "這個英雄 Key 和模式已經存在。");
                return View(guide);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create ARAM guide.");
                ModelState.AddModelError(string.Empty, $"新增失敗：{ex.Message}");
                return View(guide);
            }
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
        {
            var guide = await _repository.FindAsync(id, cancellationToken);
            if (guide == null)
            {
                return RedirectToAction(nameof(Index));
            }

            return View(guide);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(LolAramGuide guide, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return View(guide);
            }

            try
            {
                var updated = await _repository.UpdateAsync(guide, cancellationToken);
                if (!updated)
                {
                    return RedirectToAction(nameof(Index));
                }

                TempData["Success"] = "知識已更新，AI 推薦會依照新的內容判斷。";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex) when (IsUniqueConstraintError(ex))
            {
                ModelState.AddModelError(string.Empty, "這個英雄 Key 和模式已經存在。");
                return View(guide);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update ARAM guide {GuideId}.", guide.Id);
                ModelState.AddModelError(string.Empty, $"更新失敗：{ex.Message}");
                return View(guide);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            await _repository.DeleteAsync(id, cancellationToken);
            TempData["Success"] = "知識已刪除。";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ImportChampionAugmentsFromOpGg(
            string? sourceUrl,
            int startIndex,
            int maxHeroes,
            CancellationToken cancellationToken)
        {
            try
            {
                var normalizedStartIndex = Math.Max(startIndex, 1);
                var guides = await _opGgChampionScraper.ScrapeChampionAugmentGuidesAsync(
                    sourceUrl ?? string.Empty,
                    normalizedStartIndex,
                    maxHeroes,
                    cancellationToken);
                var result = await _repository.UpsertAugmentRecommendationsAsync(guides, cancellationToken);
                TempData["Success"] = $"OP.GG 英雄海克斯匯入完成：從第 {normalizedStartIndex} 位英雄開始讀取 {guides.Count} 位，新增 {result.InsertedCount} 筆，更新 {result.UpdatedCount} 筆，略過 {result.SkippedCount} 筆。";
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or HttpRequestException)
            {
                TempData["Error"] = $"OP.GG 英雄海克斯匯入失敗：{ex.Message}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import champion augment guides from OP.GG.");
                TempData["Error"] = $"OP.GG 英雄海克斯匯入發生未預期錯誤：{ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        private static bool IsUniqueConstraintError(Exception ex)
        {
            return ex is SqlException sqlException && sqlException.Number is 2601 or 2627
                || ex is PostgresException postgresException
                    && postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
        }

        private async Task<IReadOnlyDictionary<string, LolAramAugment>> BuildAugmentCatalogAsync(
            CancellationToken cancellationToken)
        {
            var augments = await _augmentRepository.SearchAsync(null, cancellationToken);
            var catalog = new Dictionary<string, LolAramAugment>(StringComparer.OrdinalIgnoreCase);

            foreach (var augment in augments)
            {
                AddCatalogEntry(catalog, augment.Name, augment);
                AddCatalogEntry(catalog, augment.AugmentKey, augment);
            }

            return catalog;
        }

        private static void AddCatalogEntry(
            IDictionary<string, LolAramAugment> catalog,
            string? key,
            LolAramAugment augment)
        {
            if (!string.IsNullOrWhiteSpace(key) && !catalog.ContainsKey(key.Trim()))
            {
                catalog.Add(key.Trim(), augment);
            }
        }
    }
}
