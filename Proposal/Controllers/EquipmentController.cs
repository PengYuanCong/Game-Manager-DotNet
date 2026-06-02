using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proposal.Models;
using Proposal.Services;

namespace Proposal.Controllers;

[Authorize]
public class EquipmentController : Controller
{
    private const long MaxExcelImportBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExcelExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xlsx",
        ".xls"
    };

    private readonly IEquipmentRepository _equipmentRepository;
    private readonly ICalculatorDataRepository _calculatorDataRepository;
    private readonly IUserActivityLogService _activityLogService;

    public EquipmentController(
        IEquipmentRepository equipmentRepository,
        ICalculatorDataRepository calculatorDataRepository,
        IUserActivityLogService activityLogService)
    {
        _equipmentRepository = equipmentRepository;
        _calculatorDataRepository = calculatorDataRepository;
        _activityLogService = activityLogService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? searchString,
        string? statFilter,
        CancellationToken cancellationToken)
    {
        var equipmentList = await _equipmentRepository.ListAsync(
            CurrentUsername(),
            searchString,
            statFilter,
            cancellationToken);

        ViewData["CurrentFilter"] = searchString;
        ViewData["CurrentStatFilter"] = statFilter;
        return View(equipmentList);
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Equipment model, CancellationToken cancellationToken)
    {
        await _equipmentRepository.CreateAsync(CurrentUsername(), model, cancellationToken);
        return RedirectToAction("Index");
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var equipment = await _equipmentRepository.GetByIdAsync(CurrentUsername(), id, cancellationToken);
        return equipment is null ? RedirectToAction("Index") : View(equipment);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Equipment model, CancellationToken cancellationToken)
    {
        await _equipmentRepository.UpdateAsync(CurrentUsername(), model, cancellationToken);
        return RedirectToAction("Index");
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await _equipmentRepository.DeleteAsync(CurrentUsername(), id, cancellationToken);
        return RedirectToAction("Index");
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(CancellationToken cancellationToken)
    {
        var equipments = await _equipmentRepository.ListAsync(CurrentUsername(), cancellationToken: cancellationToken);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("我的裝備清單");
        WriteEquipmentHeader(worksheet);

        var headerRange = worksheet.Range(1, 1, 1, EquipmentExcelHeaders.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

        var currentRow = 2;
        foreach (var equipment in equipments)
        {
            WriteEquipmentRow(worksheet, currentRow, equipment);
            currentRow++;
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var content = stream.ToArray();
        var fileName = $"裝備清單_{DateTime.Now:yyyyMMdd}.xlsx";

        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportExcel(IFormFile excelFile, CancellationToken cancellationToken)
    {
        if (excelFile == null || excelFile.Length <= 0)
        {
            TempData["Error"] = "請選擇要上傳的 Excel 檔案！";
            return RedirectToAction("Index");
        }

        if (excelFile.Length > MaxExcelImportBytes)
        {
            TempData["Error"] = "Excel 檔案超過 5 MB，請先縮小檔案後再匯入。";
            return RedirectToAction("Index");
        }

        var extension = Path.GetExtension(excelFile.FileName);
        if (!AllowedExcelExtensions.Contains(extension))
        {
            TempData["Error"] = "只允許匯入 .xlsx 或 .xls 檔案。";
            return RedirectToAction("Index");
        }

        var equipments = new List<Equipment>();

        using (var stream = new MemoryStream())
        {
            await excelFile.CopyToAsync(stream, cancellationToken);
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1);
            var lastRow = worksheet.LastRowUsed();
            if (lastRow is null)
            {
                TempData["Error"] = "Excel 檔案沒有可匯入的資料。";
                return RedirectToAction("Index");
            }

            for (var row = 2; row <= lastRow.RowNumber(); row++)
            {
                var equipment = ReadEquipmentFromWorksheet(worksheet, row);
                if (!string.IsNullOrWhiteSpace(equipment.Name))
                {
                    equipments.Add(equipment);
                }
            }
        }

        await _equipmentRepository.CreateManyAsync(CurrentUsername(), equipments, cancellationToken);
        TempData["Success"] = "Excel 資料匯入成功！";
        return RedirectToAction("Index");
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportLeagueItemsFromDataDragon(CancellationToken cancellationToken)
    {
        try
        {
            var importedCount = await ImportLeagueItemsAsync(cancellationToken);
            TempData["Success"] = $"英雄聯盟裝備同步完成，處理 {importedCount} 筆裝備。";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            TempData["Error"] = $"英雄聯盟裝備匯入失敗：{ex.Message}";
        }

        return RedirectToAction("Index");
    }

    [HttpGet]
    public async Task<IActionResult> GetEquipmentDetails(int id, CancellationToken cancellationToken)
    {
        var equipment = await _equipmentRepository.GetByIdAsync(CurrentUsername(), id, cancellationToken);
        return Json(equipment);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLoadout(
        string loadoutName,
        List<int> equipmentIds,
        CancellationToken cancellationToken)
    {
        var normalizedEquipmentIds = NormalizeEquipmentIds(equipmentIds);
        if (string.IsNullOrWhiteSpace(loadoutName) || normalizedEquipmentIds.Count == 0)
        {
            return Json(new { success = false, message = "請輸入組合名稱並至少選擇一件裝備！" });
        }

        var username = CurrentUsername();
        var selectedNames = await _equipmentRepository.SaveLoadoutAsync(
            username,
            loadoutName,
            normalizedEquipmentIds,
            cancellationToken);

        await _activityLogService.AddAsync(
            username,
            "loadout",
            $"儲存裝備組合：{loadoutName}",
            selectedNames.Count == 0 ? $"共 {normalizedEquipmentIds.Count} 件裝備" : string.Join("、", selectedNames),
            Url.Action("Index", "Equipment"),
            cancellationToken);

        return Json(new { success = true, message = "組合儲存成功！" });
    }

    [HttpGet]
    public async Task<IActionResult> GetLoadouts(CancellationToken cancellationToken)
    {
        var loadouts = await _equipmentRepository.GetLoadoutsAsync(CurrentUsername(), cancellationToken);
        return Json(new { success = true, loadouts });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateLoadout(
        int loadoutId,
        string loadoutName,
        List<int> equipmentIds,
        CancellationToken cancellationToken)
    {
        var normalizedEquipmentIds = NormalizeEquipmentIds(equipmentIds);
        if (loadoutId <= 0 || string.IsNullOrWhiteSpace(loadoutName) || normalizedEquipmentIds.Count == 0)
        {
            return Json(new { success = false, message = "請選擇組合、輸入名稱，並至少保留一件裝備。" });
        }

        var username = CurrentUsername();
        var selectedNames = await _equipmentRepository.UpdateLoadoutAsync(
            username,
            loadoutId,
            loadoutName,
            normalizedEquipmentIds,
            cancellationToken);

        if (selectedNames is null)
        {
            return Json(new { success = false, message = "找不到要更新的組合。" });
        }

        await _activityLogService.AddAsync(
            username,
            "loadout",
            $"更新裝備組合：{loadoutName}",
            selectedNames.Count == 0 ? $"共 {normalizedEquipmentIds.Count} 件裝備" : string.Join("、", selectedNames),
            Url.Action("Index", "Equipment"),
            cancellationToken);

        return Json(new { success = true, message = "組合已更新。" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLoadout(int loadoutId, CancellationToken cancellationToken)
    {
        if (loadoutId <= 0)
        {
            return Json(new { success = false, message = "找不到要刪除的組合。" });
        }

        var username = CurrentUsername();
        var deletedName = await _equipmentRepository.DeleteLoadoutAsync(username, loadoutId, cancellationToken);
        if (deletedName is null)
        {
            return Json(new { success = false, message = "找不到要刪除的組合。" });
        }

        await _activityLogService.AddAsync(
            username,
            "loadout",
            $"刪除裝備組合：{deletedName}",
            "使用者手動刪除裝備組合",
            Url.Action("Index", "Equipment"),
            cancellationToken);

        return Json(new { success = true, message = "組合已刪除。" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteInvalidLoadouts(CancellationToken cancellationToken)
    {
        var username = CurrentUsername();
        var deletedCount = await _equipmentRepository.DeleteInvalidLoadoutsAsync(username, cancellationToken);

        if (deletedCount > 0)
        {
            await _activityLogService.AddAsync(
                username,
                "loadout",
                $"清理失效裝備組合：{deletedCount} 筆",
                "組合內裝備已不存在，因此移除失效資料。",
                Url.Action("Index", "Equipment"),
                cancellationToken);
        }

        return Json(new
        {
            success = true,
            message = deletedCount == 0 ? "目前沒有失效組合。" : $"已清理 {deletedCount} 筆失效組合。"
        });
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return RedirectToAction("Index");
        }

        var searchResults = await _equipmentRepository.ListAsync(
            CurrentUsername(),
            query,
            cancellationToken: cancellationToken);

        ViewData["SearchTerm"] = query;
        return View(searchResults);
    }

    [HttpGet]
    public async Task<IActionResult> GetLoadoutStats(int loadoutId, CancellationToken cancellationToken)
    {
        var stats = await _calculatorDataRepository.GetLoadoutStatsAsync(
            CurrentUsername(),
            loadoutId,
            cancellationToken);

        return stats is null ? NotFound() : Json(stats);
    }

    private async Task<int> ImportLeagueItemsAsync(CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Proposal ARAM item importer");

        var versionsJson = await httpClient.GetStringAsync(
            "https://ddragon.leagueoflegends.com/api/versions.json",
            cancellationToken);
        var versions = JsonSerializer.Deserialize<List<string>>(versionsJson);
        var latestVersion = versions?.FirstOrDefault()
            ?? throw new InvalidOperationException("無法取得 Data Dragon 版本。");

        var itemJson = await httpClient.GetStringAsync(
            $"https://ddragon.leagueoflegends.com/cdn/{latestVersion}/data/zh_TW/item.json",
            cancellationToken);

        using var document = JsonDocument.Parse(itemJson);
        var items = document.RootElement.GetProperty("data");
        var importedCount = 0;
        var username = CurrentUsername();

        foreach (var itemProperty in items.EnumerateObject())
        {
            var item = itemProperty.Value;
            if (!ShouldImportLeagueItem(item))
            {
                continue;
            }

            var equipment = CreateEquipmentFromDataDragon(itemProperty.Name, item, latestVersion);
            if (string.IsNullOrWhiteSpace(equipment.Name))
            {
                continue;
            }

            await _equipmentRepository.UpsertAsync(username, equipment, cancellationToken);
            importedCount++;
        }

        return importedCount;
    }

    private string CurrentUsername()
    {
        return User.Identity?.Name ?? "anonymous";
    }

    private static List<int> NormalizeEquipmentIds(IEnumerable<int>? equipmentIds)
    {
        return (equipmentIds ?? Enumerable.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .Take(6)
            .ToList();
    }

    private static bool ShouldImportLeagueItem(JsonElement item)
    {
        if (!item.TryGetProperty("gold", out var gold)
            || !gold.TryGetProperty("purchasable", out var purchasable)
            || !purchasable.GetBoolean()
            || !gold.TryGetProperty("total", out var totalGold)
            || totalGold.GetInt32() <= 0)
        {
            return false;
        }

        if (item.TryGetProperty("maps", out var maps)
            && maps.TryGetProperty("12", out var aramMap)
            && !aramMap.GetBoolean())
        {
            return false;
        }

        if (!item.TryGetProperty("tags", out var tags))
        {
            return true;
        }

        return !tags.EnumerateArray().Any(tag =>
            string.Equals(tag.GetString(), "Consumable", StringComparison.OrdinalIgnoreCase)
            || string.Equals(tag.GetString(), "Trinket", StringComparison.OrdinalIgnoreCase));
    }

    private static Equipment CreateEquipmentFromDataDragon(string itemId, JsonElement item, string latestVersion)
    {
        var stats = item.TryGetProperty("stats", out var statsValue)
            ? statsValue
            : default;
        var gold = item.GetProperty("gold");
        var tags = item.TryGetProperty("tags", out var tagsValue)
            ? string.Join("; ", tagsValue.EnumerateArray().Select(tag => tag.GetString()).Where(tag => !string.IsNullOrWhiteSpace(tag)))
            : string.Empty;
        var statText = CleanStatsBlock(item);

        var equipment = new Equipment
        {
            DataDragonId = itemId,
            ItemImageUrl = $"https://ddragon.leagueoflegends.com/cdn/{latestVersion}/img/item/{itemId}.png",
            Name = item.GetProperty("name").GetString()?.Trim() ?? string.Empty,
            HP = GetItemStat(stats, "FlatHPPoolMod"),
            Mana = GetItemStat(stats, "FlatMPPoolMod"),
            Attack = GetItemStat(stats, "FlatPhysicalDamageMod"),
            MagicAttack = GetItemStat(stats, "FlatMagicDamageMod"),
            PhysicalDefense = GetItemStat(stats, "FlatArmorMod"),
            MagicDefense = GetItemStat(stats, "FlatSpellBlockMod"),
            HealthRegen = GetItemStatPercent(stats, "PercentBaseHPRegenMod", "PercentHPRegenMod", "FlatHPRegenMod"),
            ManaRegen = GetItemStatPercent(stats, "PercentBaseMPRegenMod", "PercentMPRegenMod", "FlatMPRegenMod"),
            AbilityHaste = GetItemStatDecimal(stats, "AbilityHaste", "rPercentCooldownMod"),
            AttackSpeed = GetItemStatPercent(stats, "PercentAttackSpeedMod", "FlatAttackSpeedMod"),
            CriticalStrikeChance = GetItemStatPercent(stats, "FlatCritChanceMod", "PercentCritChanceMod"),
            MoveSpeed = GetItemStat(stats, "FlatMovementSpeedMod"),
            MoveSpeedPercent = GetItemStatPercent(stats, "PercentMovementSpeedMod"),
            Lethality = GetItemStatDecimal(stats, "FlatArmorPenetrationMod"),
            ArmorPenetrationPercent = GetItemStatPercent(stats, "PercentArmorPenetrationMod", "PercentBonusArmorPenetrationMod"),
            MagicPenetration = GetItemStatDecimal(stats, "FlatMagicPenetrationMod"),
            MagicPenetrationPercent = GetItemStatPercent(stats, "PercentMagicPenetrationMod"),
            LifeSteal = GetItemStatPercent(stats, "PercentLifeStealMod"),
            Omnivamp = GetItemStatPercent(stats, "PercentOmnivampMod", "PercentSpellVampMod"),
            HealAndShieldPower = GetItemStatPercent(stats, "HealAndShieldPower", "PercentHealAndShieldPowerMod"),
            Tenacity = GetItemStatPercent(stats, "Tenacity", "PercentTenacityMod"),
            Price = gold.TryGetProperty("total", out var totalGold) ? totalGold.GetInt32() : 0,
            ItemTags = tags,
            ItemDescription = $"{CleanDescription(item)}\nData Dragon {latestVersion}".Trim()
        };

        ApplyDescriptionStats(equipment, statText);
        return equipment;
    }

    private static readonly string[] EquipmentExcelHeaders =
    [
        "裝備名稱",
        "HP",
        "法力",
        "物理攻擊",
        "魔法攻擊",
        "物理防禦",
        "魔法防禦",
        "回血%",
        "回魔%",
        "技能急速",
        "攻速%",
        "爆擊率%",
        "跑速",
        "跑速%",
        "物理致命",
        "物理穿透%",
        "固定魔穿",
        "魔法穿透%",
        "生命偷取%",
        "全能吸血%",
        "治療護盾強度%",
        "韌性%",
        "價格",
        "DataDragonId",
        "圖片網址",
        "標籤",
        "描述"
    ];

    private static int GetItemStat(JsonElement stats, params string[] keys)
    {
        return (int)Math.Round(GetItemStatDecimal(stats, keys), MidpointRounding.AwayFromZero);
    }

    private static decimal GetItemStatDecimal(JsonElement stats, params string[] keys)
    {
        if (stats.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        foreach (var key in keys)
        {
            if (stats.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                return value.GetDecimal();
            }
        }

        return 0;
    }

    private static decimal GetItemStatPercent(JsonElement stats, params string[] keys)
    {
        var value = GetItemStatDecimal(stats, keys);
        return Math.Abs(value) <= 1 ? value * 100 : value;
    }

    private static void ApplyDescriptionStats(Equipment equipment, string statText)
    {
        if (string.IsNullOrWhiteSpace(statText))
        {
            return;
        }

        var hp = FindFlatStat(statText, "生命");
        if (hp > 0) equipment.HP = DecimalToInt(hp.Value);

        var mana = FindFlatStat(statText, "魔力");
        if (mana > 0) equipment.Mana = DecimalToInt(mana.Value);

        var attack = FindFlatStat(statText, "物理攻擊");
        if (attack > 0) equipment.Attack = DecimalToInt(attack.Value);

        var magicAttack = FindFlatStat(statText, "魔法攻擊");
        if (magicAttack > 0) equipment.MagicAttack = DecimalToInt(magicAttack.Value);

        var armor = FindFlatStat(statText, "物理防禦");
        if (armor > 0) equipment.PhysicalDefense = DecimalToInt(armor.Value);

        var magicResist = FindFlatStat(statText, "魔法防禦");
        if (magicResist > 0) equipment.MagicDefense = DecimalToInt(magicResist.Value);

        var healthRegen = FindPercentStat(statText, "基礎生命回復");
        if (healthRegen > 0) equipment.HealthRegen = healthRegen.Value;

        var manaRegen = FindPercentStat(statText, "基礎魔力回復");
        if (manaRegen > 0) equipment.ManaRegen = manaRegen.Value;

        var abilityHaste = FindFlatStat(statText, "技能加速");
        if (abilityHaste > 0) equipment.AbilityHaste = abilityHaste.Value;

        var attackSpeed = FindPercentStat(statText, "攻擊速度");
        if (attackSpeed > 0) equipment.AttackSpeed = attackSpeed.Value;

        var crit = FindPercentStat(statText, "暴擊率");
        if (crit > 0) equipment.CriticalStrikeChance = crit.Value;

        var moveSpeed = FindFlatStat(statText, "跑速");
        if (moveSpeed > 0) equipment.MoveSpeed = DecimalToInt(moveSpeed.Value);

        var moveSpeedPercent = FindPercentStat(statText, "跑速");
        if (moveSpeedPercent > 0) equipment.MoveSpeedPercent = moveSpeedPercent.Value;

        var lethality = FindFlatStat(statText, "物理致命");
        if (lethality > 0) equipment.Lethality = lethality.Value;

        var armorPen = FindPercentStat(statText, "物理穿透");
        if (armorPen > 0) equipment.ArmorPenetrationPercent = armorPen.Value;

        var magicPen = FindFlatStat(statText, "魔法穿透");
        if (magicPen > 0) equipment.MagicPenetration = magicPen.Value;

        var magicPenPercent = FindPercentStat(statText, "魔法穿透");
        if (magicPenPercent > 0) equipment.MagicPenetrationPercent = magicPenPercent.Value;

        var lifeSteal = FindPercentStat(statText, "普攻吸血");
        if (lifeSteal > 0) equipment.LifeSteal = lifeSteal.Value;

        var omnivamp = FindPercentStat(statText, "全能吸血");
        if (omnivamp > 0) equipment.Omnivamp = omnivamp.Value;

        var healAndShield = FindPercentStat(statText, "治療與護盾量");
        if (healAndShield > 0) equipment.HealAndShieldPower = healAndShield.Value;

        var tenacity = FindPercentStat(statText, "韌性");
        if (tenacity > 0) equipment.Tenacity = tenacity.Value;
    }

    private static decimal? FindFlatStat(string text, string label)
    {
        return FindStat(text, $@"(?<value>\d+(?:\.\d+)?)\s*{Regex.Escape(label)}");
    }

    private static decimal? FindPercentStat(string text, string label)
    {
        return FindStat(text, $@"(?<value>\d+(?:\.\d+)?)\s*%\s*{Regex.Escape(label)}");
    }

    private static decimal? FindStat(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        return decimal.TryParse(
            match.Groups["value"].Value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static int DecimalToInt(decimal value)
    {
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static string CleanStatsBlock(JsonElement item)
    {
        if (!item.TryGetProperty("description", out var descriptionElement))
        {
            return string.Empty;
        }

        var description = descriptionElement.GetString() ?? string.Empty;
        var match = Regex.Match(description, "<stats>(.*?)</stats>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? HtmlToText(match.Groups[1].Value) : string.Empty;
    }

    private static string CleanDescription(JsonElement item)
    {
        if (!item.TryGetProperty("description", out var descriptionElement))
        {
            return string.Empty;
        }

        var description = descriptionElement.GetString() ?? string.Empty;
        return HtmlToText(description);
    }

    private static string HtmlToText(string html)
    {
        var text = Regex.Replace(html, "<br\\s*/?>", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<.*?>", " ");
        text = Regex.Replace(text, "\\s+", " ");
        return WebUtility.HtmlDecode(text).Trim();
    }

    private static void WriteEquipmentHeader(IXLWorksheet worksheet)
    {
        for (var i = 0; i < EquipmentExcelHeaders.Length; i++)
        {
            worksheet.Cell(1, i + 1).Value = EquipmentExcelHeaders[i];
        }
    }

    private static void WriteEquipmentRow(IXLWorksheet worksheet, int row, Equipment equipment)
    {
        worksheet.Cell(row, 1).Value = equipment.Name;
        worksheet.Cell(row, 2).Value = equipment.HP;
        worksheet.Cell(row, 3).Value = equipment.Mana;
        worksheet.Cell(row, 4).Value = equipment.Attack;
        worksheet.Cell(row, 5).Value = equipment.MagicAttack;
        worksheet.Cell(row, 6).Value = equipment.PhysicalDefense;
        worksheet.Cell(row, 7).Value = equipment.MagicDefense;
        worksheet.Cell(row, 8).Value = equipment.HealthRegen;
        worksheet.Cell(row, 9).Value = equipment.ManaRegen;
        worksheet.Cell(row, 10).Value = equipment.AbilityHaste;
        worksheet.Cell(row, 11).Value = equipment.AttackSpeed;
        worksheet.Cell(row, 12).Value = equipment.CriticalStrikeChance;
        worksheet.Cell(row, 13).Value = equipment.MoveSpeed;
        worksheet.Cell(row, 14).Value = equipment.MoveSpeedPercent;
        worksheet.Cell(row, 15).Value = equipment.Lethality;
        worksheet.Cell(row, 16).Value = equipment.ArmorPenetrationPercent;
        worksheet.Cell(row, 17).Value = equipment.MagicPenetration;
        worksheet.Cell(row, 18).Value = equipment.MagicPenetrationPercent;
        worksheet.Cell(row, 19).Value = equipment.LifeSteal;
        worksheet.Cell(row, 20).Value = equipment.Omnivamp;
        worksheet.Cell(row, 21).Value = equipment.HealAndShieldPower;
        worksheet.Cell(row, 22).Value = equipment.Tenacity;
        worksheet.Cell(row, 23).Value = equipment.Price;
        worksheet.Cell(row, 24).Value = equipment.DataDragonId;
        worksheet.Cell(row, 25).Value = equipment.ItemImageUrl;
        worksheet.Cell(row, 26).Value = equipment.ItemTags;
        worksheet.Cell(row, 27).Value = equipment.ItemDescription;
    }

    private static Equipment ReadEquipmentFromWorksheet(IXLWorksheet worksheet, int row)
    {
        var isLegacyFormat = worksheet.LastColumnUsed()?.ColumnNumber() <= 7
            || worksheet.Cell(1, 7).Value.ToString().Contains("價格", StringComparison.OrdinalIgnoreCase);
        if (isLegacyFormat)
        {
            return new Equipment
            {
                Name = worksheet.Cell(row, 1).Value.ToString(),
                HP = ReadIntCell(worksheet, row, 2),
                Attack = ReadIntCell(worksheet, row, 3),
                MagicAttack = ReadIntCell(worksheet, row, 4),
                PhysicalDefense = ReadIntCell(worksheet, row, 5),
                MagicDefense = ReadIntCell(worksheet, row, 6),
                Price = ReadIntCell(worksheet, row, 7)
            };
        }

        return new Equipment
        {
            Name = worksheet.Cell(row, 1).Value.ToString(),
            HP = ReadIntCell(worksheet, row, 2),
            Mana = ReadIntCell(worksheet, row, 3),
            Attack = ReadIntCell(worksheet, row, 4),
            MagicAttack = ReadIntCell(worksheet, row, 5),
            PhysicalDefense = ReadIntCell(worksheet, row, 6),
            MagicDefense = ReadIntCell(worksheet, row, 7),
            HealthRegen = ReadDecimalCell(worksheet, row, 8),
            ManaRegen = ReadDecimalCell(worksheet, row, 9),
            AbilityHaste = ReadDecimalCell(worksheet, row, 10),
            AttackSpeed = ReadDecimalCell(worksheet, row, 11),
            CriticalStrikeChance = ReadDecimalCell(worksheet, row, 12),
            MoveSpeed = ReadIntCell(worksheet, row, 13),
            MoveSpeedPercent = ReadDecimalCell(worksheet, row, 14),
            Lethality = ReadDecimalCell(worksheet, row, 15),
            ArmorPenetrationPercent = ReadDecimalCell(worksheet, row, 16),
            MagicPenetration = ReadDecimalCell(worksheet, row, 17),
            MagicPenetrationPercent = ReadDecimalCell(worksheet, row, 18),
            LifeSteal = ReadDecimalCell(worksheet, row, 19),
            Omnivamp = ReadDecimalCell(worksheet, row, 20),
            HealAndShieldPower = ReadDecimalCell(worksheet, row, 21),
            Tenacity = ReadDecimalCell(worksheet, row, 22),
            Price = ReadIntCell(worksheet, row, 23),
            DataDragonId = ReadStringCell(worksheet, row, 24),
            ItemImageUrl = ReadStringCell(worksheet, row, 25),
            ItemTags = ReadStringCell(worksheet, row, 26),
            ItemDescription = ReadStringCell(worksheet, row, 27)
        };
    }

    private static int ReadIntCell(IXLWorksheet worksheet, int row, int column)
    {
        return worksheet.Cell(row, column).IsEmpty() ? 0 : worksheet.Cell(row, column).GetValue<int>();
    }

    private static decimal ReadDecimalCell(IXLWorksheet worksheet, int row, int column)
    {
        return worksheet.Cell(row, column).IsEmpty() ? 0 : worksheet.Cell(row, column).GetValue<decimal>();
    }

    private static string? ReadStringCell(IXLWorksheet worksheet, int row, int column)
    {
        var value = worksheet.Cell(row, column).Value.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
