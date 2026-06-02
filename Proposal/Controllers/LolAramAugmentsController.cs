using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Npgsql;
using Proposal.Models;
using Proposal.Services;
using System.Text;
using System.Text.Json;

namespace Proposal.Controllers
{
    [Authorize]
    public class LolAramAugmentsController : Controller
    {
        private readonly ILolAramAugmentRepository _repository;
        private readonly IOpGgAramMayhemAugmentScraper _opGgScraper;
        private readonly ILogger<LolAramAugmentsController> _logger;

        public LolAramAugmentsController(
            ILolAramAugmentRepository repository,
            IOpGgAramMayhemAugmentScraper opGgScraper,
            ILogger<LolAramAugmentsController> logger)
        {
            _repository = repository;
            _opGgScraper = opGgScraper;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string? searchString,
            string? rarity,
            string? seriesKey,
            string? tag,
            string? pick,
            string? stage,
            int? slot,
            CancellationToken cancellationToken)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentRarity"] = NormalizeRarityFilter(rarity);
            ViewData["CurrentSeries"] = NormalizeSeriesFilter(seriesKey);
            ViewData["CurrentTag"] = NormalizeTagFilter(tag);
            ViewData["PickMode"] = pick;
            ViewData["PickStage"] = stage;
            ViewData["PickSlot"] = slot;

            var augments = await _repository.SearchAsync(searchString, cancellationToken);
            augments = ApplyFilters(
                augments,
                ViewData["CurrentRarity"]?.ToString(),
                ViewData["CurrentSeries"]?.ToString(),
                ViewData["CurrentTag"]?.ToString());
            return View(augments);
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
        {
            var augment = await _repository.FindAsync(id, cancellationToken);
            if (augment == null)
            {
                return RedirectToAction(nameof(Index));
            }

            return View(augment);
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            return View(new LolAramAugment());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(LolAramAugment augment, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return View(augment);
            }

            try
            {
                await _repository.CreateAsync(augment, cancellationToken);
                TempData["Success"] = "海克斯知識已新增。";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex) when (IsUniqueConstraintError(ex))
            {
                ModelState.AddModelError(string.Empty, "同一個海克斯 Key 和遊戲模式已經有資料了。");
                return View(augment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create ARAM augment.");
                ModelState.AddModelError(string.Empty, $"新增失敗：{ex.Message}");
                return View(augment);
            }
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
        {
            var augment = await _repository.FindAsync(id, cancellationToken);
            if (augment == null)
            {
                return RedirectToAction(nameof(Index));
            }

            return View(augment);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(LolAramAugment augment, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return View(augment);
            }

            try
            {
                var updated = await _repository.UpdateAsync(augment, cancellationToken);
                if (!updated)
                {
                    return RedirectToAction(nameof(Index));
                }

                TempData["Success"] = "海克斯知識已更新，相關 AI 推薦快取會重新產生。";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex) when (IsUniqueConstraintError(ex))
            {
                ModelState.AddModelError(string.Empty, "同一個海克斯 Key 和遊戲模式已經有資料了。");
                return View(augment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update ARAM augment {AugmentId}.", augment.Id);
                ModelState.AddModelError(string.Empty, $"更新失敗：{ex.Message}");
                return View(augment);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            await _repository.DeleteAsync(id, cancellationToken);
            TempData["Success"] = "海克斯知識已刪除。";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Import(IFormFile importFile, CancellationToken cancellationToken)
        {
            if (importFile == null || importFile.Length == 0)
            {
                TempData["Error"] = "請選擇要匯入的 JSON 或 CSV 檔案。";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                await using var stream = importFile.OpenReadStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var content = await reader.ReadToEndAsync(cancellationToken);
                var extension = Path.GetExtension(importFile.FileName).ToLowerInvariant();

                var augments = extension switch
                {
                    ".json" => ParseJsonAugments(content),
                    ".csv" => ParseCsvAugments(content),
                    _ => throw new FormatException("目前只支援 .json 或 .csv。")
                };

                var result = await _repository.UpsertManyAsync(augments, cancellationToken);
                TempData["Success"] = $"匯入完成：新增 {result.InsertedCount} 筆，更新 {result.UpdatedCount} 筆，略過 {result.SkippedCount} 筆。";
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                TempData["Error"] = $"匯入格式錯誤：{ex.Message}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import ARAM augments.");
                TempData["Error"] = $"匯入失敗：{ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ImportFromOpGg(string? sourceUrl, CancellationToken cancellationToken)
        {
            try
            {
                var augments = await _opGgScraper.ScrapeAugmentsAsync(sourceUrl ?? string.Empty, cancellationToken);
                var result = await _repository.UpsertManyAsync(augments, cancellationToken);
                TempData["Success"] = $"OP.GG 匯入完成：讀取 {augments.Count} 筆，新增 {result.InsertedCount} 筆，更新 {result.UpdatedCount} 筆，略過 {result.SkippedCount} 筆。";
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or HttpRequestException)
            {
                TempData["Error"] = $"OP.GG 匯入失敗：{ex.Message}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import ARAM augments from OP.GG.");
                TempData["Error"] = $"OP.GG 匯入發生未預期錯誤：{ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        private static bool IsUniqueConstraintError(Exception ex)
        {
            return ex is SqlException sqlException && sqlException.Number is 2601 or 2627
                || ex is PostgresException postgresException
                    && postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
        }

        private static IReadOnlyList<LolAramAugment> ApplyFilters(
            IReadOnlyList<LolAramAugment> augments,
            string? rarity,
            string? seriesKey,
            string? tag)
        {
            var query = augments.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(rarity))
            {
                query = query.Where(augment => NormalizeRarityFilter(augment.Rarity) == rarity);
            }

            if (!string.IsNullOrWhiteSpace(seriesKey))
            {
                query = query.Where(augment => NormalizeSeriesFilter(augment.SeriesKey) == seriesKey);
            }

            if (!string.IsNullOrWhiteSpace(tag))
            {
                query = query.Where(augment => SplitTagValues(augment.Tags).Any(value => NormalizeTagFilter(value) == tag));
            }

            return query
                .OrderBy(augment => TierRank(augment.Tier))
                .ThenBy(augment => RarityRank(augment.Rarity))
                .ThenBy(augment => augment.Name)
                .ToList();
        }

        private static int TierRank(string? tier)
        {
            return tier?.Trim().ToUpperInvariant() switch
            {
                "S" or "S+" or "S-" => 0,
                "A" or "A+" or "A-" => 1,
                "B" or "B+" or "B-" => 2,
                "C" or "C+" or "C-" => 3,
                "D" or "D+" or "D-" => 4,
                "E" or "E+" or "E-" => 5,
                _ => 6
            };
        }

        private static int RarityRank(string? rarity)
        {
            return NormalizeRarityFilter(rarity) switch
            {
                "prismatic" => 0,
                "gold" => 1,
                "silver" => 2,
                _ => 3
            };
        }

        private static string? NormalizeRarityFilter(string? value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "prismatic" or "稜彩" or "稜彩階" or "彩色" => "prismatic",
                "gold" or "黃金" or "黃金階" or "金色" => "gold",
                "silver" or "白銀" or "白銀階" or "白色" => "silver",
                _ => null
            };
        }

        private static string? NormalizeSeriesFilter(string? value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "firecracker" or "爆竹" or "爆竹系列" => "firecracker",
                "stackosaurus" or "stacking_dino" or "堆疊暴龍" or "堆疊暴龍系列" => "stackosaurus",
                "snowday" or "下雪天" or "下雪天系列" or "雪球" or "雪球系列" => "snowday",
                "self_destruct" or "divebomb" or "自爆" or "自爆系列" or "俯衝炸彈" or "俯衝炸彈系列" => "self_destruct",
                "low_health_ally" or "ping" or "嗚咿嗚咿" or "嗚咿嗚咿系列" or "喂喂喂喂" or "喂喂喂喂系列" => "low_health_ally",
                "archmage" or "大法師" or "大法師系列" => "archmage",
                "automation" or "auto_cast" or "自動化" or "自動化系列" or "完全自動化" or "完全自動化系列" => "automation",
                "high_roller" or "dice" or "土豪賭客" or "土豪賭客系列" or "擲骰狂人" or "擲骰狂人系列" => "high_roller",
                "coinrain" or "coin_rain" or "錢如雨下" or "錢如雨下系列" or "金幣雨" or "金幣雨系列" => "coinrain",
                _ => null
            };
        }

        private static string? NormalizeTagFilter(string? value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "burn" or "燃燒" or "灼燒" => "burn",
                "firecracker" or "爆竹" or "爆竹系列" => "firecracker",
                "self_destruct" or "divebomb" or "自爆" or "復活" or "revive" => "self_destruct",
                "stackosaurus" or "stacking_dino" or "scaling" or "成長" or "後期" or "stacking" or "stack" or "疊層" or "堆疊" or "堆疊暴龍" => "stackosaurus",
                "low_health_ally" or "ping" or "嗚咿嗚咿" or "喂喂喂喂" or "低血量友軍" => "low_health_ally",
                "auto_cast" or "automation" or "自動施放" or "自動化" => "automation",
                "high_roller" or "dice" or "anvil" or "鍛造器" or "土豪賭客" => "high_roller",
                "coinrain" or "coin_rain" or "gold" or "coin_drop" or "economy" or "金錢" or "金幣" or "經濟" or "錢如雨下" => "coinrain",
                "healing" or "heal" or "治療" or "回復" => "healing",
                "shield" or "護盾" => "shield",
                "magic_damage" or "magic" or "ap" or "魔法傷害" or "魔法" => "magic_damage",
                "physical_damage" or "physical" or "ad" or "物理傷害" or "物理" => "physical_damage",
                "true_damage" or "真實傷害" => "true_damage",
                "ability_haste" or "haste" or "技能急速" or "冷卻" => "ability_haste",
                "movement_speed" or "movespeed" or "跑速" or "移動速度" => "movement_speed",
                "crowd_control" or "cc" or "控制" => "crowd_control",
                "slow" or "緩速" => "slow",
                "missile" or "projectile" or "飛彈" or "導彈" => "missile",
                "multi_hit" or "multihit" or "多段" or "多段傷害" => "multi_hit",
                "tank" or "health" or "armor" or "magic_resist" or "坦度" or "生命" or "物防" or "魔防" => "tank",
                "team_support" or "support" or "輔助" or "隊友增益" => "team_support",
                _ => null
            };
        }

        private static IReadOnlyList<string> SplitTagValues(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? Array.Empty<string>()
                : value.Split(new[] { ';', ',', '，', '、', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static List<LolAramAugment> ParseJsonAugments(string content)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            var augments = JsonSerializer.Deserialize<List<LolAramAugment>>(content, options);
            if (augments == null || augments.Count == 0)
            {
                throw new FormatException("JSON 需要是一個海克斯陣列。");
            }

            FillImportDefaults(augments);
            return augments;
        }

        private static List<LolAramAugment> ParseCsvAugments(string content)
        {
            var rows = ParseCsvRows(content);
            if (rows.Count < 2)
            {
                throw new FormatException("CSV 至少需要標題列和一筆資料。");
            }

            var headers = rows[0];
            var augments = new List<LolAramAugment>();
            for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                if (row.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                string Field(params string[] names)
                {
                    foreach (var name in names)
                    {
                        var index = headers.FindIndex(header => string.Equals(header, name, StringComparison.OrdinalIgnoreCase));
                        if (index >= 0 && index < row.Count)
                        {
                            return row[index].Trim();
                        }
                    }

                    return string.Empty;
                }

                var name = Field("Name", "海克斯名稱", "AugmentName");
                var augment = new LolAramAugment
                {
                    AugmentKey = Field("AugmentKey", "Key", "海克斯Key"),
                    Name = name,
                    ModeName = Field("ModeName", "Mode", "遊戲模式"),
                    Rarity = Field("Rarity", "稀有度"),
                    Tier = Field("Tier", "評級"),
                    SeriesKey = Field("SeriesKey", "系列Key"),
                    EffectText = Field("EffectText", "Effect", "效果文字"),
                    Tags = Field("Tags", "效果標籤"),
                    SynergyNotes = Field("SynergyNotes", "搭配備註"),
                    PatchVersion = Field("PatchVersion", "版本"),
                    SourceUrl = Field("SourceUrl", "來源"),
                    Notes = Field("Notes", "人工備註")
                };

                augments.Add(augment);
            }

            FillImportDefaults(augments);
            return augments;
        }

        private static void FillImportDefaults(IEnumerable<LolAramAugment> augments)
        {
            foreach (var augment in augments)
            {
                if (string.IsNullOrWhiteSpace(augment.AugmentKey))
                {
                    augment.AugmentKey = CreateImportKey(augment.Name);
                }

                if (string.IsNullOrWhiteSpace(augment.ModeName))
                {
                    augment.ModeName = "ARAM Mayhem";
                }

                if (string.IsNullOrWhiteSpace(augment.Rarity))
                {
                    augment.Rarity = "gold";
                }

                if (string.IsNullOrWhiteSpace(augment.PatchVersion))
                {
                    augment.PatchVersion = "manual import";
                }
            }
        }

        private static string CreateImportKey(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().Replace(" ", "_").ToLowerInvariant();
        }

        private static List<List<string>> ParseCsvRows(string content)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < content.Length; i++)
            {
                var current = content[i];
                if (current == '"')
                {
                    if (inQuotes && i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (current == ',' && !inQuotes)
                {
                    row.Add(field.ToString());
                    field.Clear();
                    continue;
                }

                if ((current == '\r' || current == '\n') && !inQuotes)
                {
                    row.Add(field.ToString());
                    field.Clear();
                    if (row.Any(value => !string.IsNullOrWhiteSpace(value)))
                    {
                        rows.Add(row);
                    }

                    row = new List<string>();
                    if (current == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                    {
                        i++;
                    }

                    continue;
                }

                field.Append(current);
            }

            row.Add(field.ToString());
            if (row.Any(value => !string.IsNullOrWhiteSpace(value)))
            {
                rows.Add(row);
            }

            return rows;
        }
    }
}
