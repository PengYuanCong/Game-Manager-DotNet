using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

const string modeName = "ARAM Mayhem";
const string marker = "人工彙整(2026-05-23)";
var dryRun = args.Any(arg => string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase));
var root = FindWorkspaceRoot();
var appsettingsPath = Path.Combine(root, "Proposal", "appsettings.json");
var reportDir = Path.Combine(root, "Reports");
Directory.CreateDirectory(reportDir);

var appsettings = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
var connectionString = appsettings.RootElement
    .GetProperty("ConnectionStrings")
    .GetProperty("DefaultConnection")
    .GetString()
    ?? throw new InvalidOperationException("DefaultConnection is missing.");

await using var connection = await OpenFirstAvailableConnectionAsync(BuildConnectionCandidates(connectionString));

var augments = await LoadAugmentsAsync(connection);
var guides = await LoadGuidesAsync(connection);
var augmentLookup = BuildAugmentLookup(augments);

var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
if (!dryRun)
{
    await CreateBackupAsync(connection, timestamp);
}

var augmentUpdates = new List<AugmentUpdate>();
foreach (var augment in augments)
{
    var generated = BuildAugmentJudgement(augment);
    var notes = MergeCuratorText(augment.Notes, generated.Notes, 1200);
    var synergyNotes = MergeCuratorText(augment.SynergyNotes, generated.SynergyNotes, 1200);
    var tags = MergeTags(augment.Tags, generated.Tags);

    if (notes != augment.Notes || synergyNotes != augment.SynergyNotes || tags != augment.Tags)
    {
        augmentUpdates.Add(new AugmentUpdate(augment.Id, notes, synergyNotes, tags));
    }
}

var guideUpdates = new List<GuideUpdate>();
foreach (var guide in guides)
{
    var generated = BuildGuideJudgement(guide, augmentLookup);
    var notes = MergeCuratorText(guide.Notes, generated.Notes, 1200);
    var augmentsText = string.IsNullOrWhiteSpace(guide.Augments) || IsPlaceholder(guide.Augments)
        ? generated.Augments
        : guide.Augments;

    if (notes != guide.Notes || augmentsText != guide.Augments)
    {
        guideUpdates.Add(new GuideUpdate(guide.Id, notes, augmentsText));
    }
}

if (!dryRun)
{
    await ApplyAugmentUpdatesAsync(connection, augmentUpdates);
    await ApplyGuideUpdatesAsync(connection, guideUpdates);
}
else if (augmentUpdates.Count is > 0 and <= 10)
{
    Console.OutputEncoding = Encoding.UTF8;
    foreach (var update in augmentUpdates)
    {
        var current = augments.FirstOrDefault(row => row.Id == update.Id);
        Console.WriteLine($"Pending augment normalization: {current?.Name} | tags: {current?.Tags} -> {update.Tags} | notesEqual={current?.Notes == update.Notes} | synergyEqual={current?.SynergyNotes == update.SynergyNotes}");
    }
}

var afterAugments = dryRun ? ApplyAugmentsInMemory(augments, augmentUpdates) : await LoadAugmentsAsync(connection);
var afterGuides = dryRun ? ApplyGuidesInMemory(guides, guideUpdates) : await LoadGuidesAsync(connection);

var report = BuildReport(timestamp, dryRun, augments, guides, afterAugments, afterGuides, augmentUpdates.Count, guideUpdates.Count);
var reportPath = Path.Combine(reportDir, $"aram-rag-curation-{timestamp}.md");
await File.WriteAllTextAsync(reportPath, report, Encoding.UTF8);

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine(report);
Console.WriteLine($"Report: {reportPath}");

static string FindWorkspaceRoot()
{
    var current = Directory.GetCurrentDirectory();
    while (!string.IsNullOrWhiteSpace(current))
    {
        if (Directory.Exists(Path.Combine(current, "Proposal")) && File.Exists(Path.Combine(current, "Proposal", "appsettings.json")))
        {
            return current;
        }

        current = Directory.GetParent(current)?.FullName ?? string.Empty;
    }

    throw new DirectoryNotFoundException("Cannot find workspace root.");
}

static IReadOnlyList<string> BuildConnectionCandidates(string baseConnectionString)
{
    var builder = new SqlConnectionStringBuilder(baseConnectionString)
    {
        Encrypt = SqlConnectionEncryptOption.Optional,
        TrustServerCertificate = true,
        Pooling = false
    };

    var dataSources = new[]
    {
        builder.DataSource,
        @".\SQLEXPRESS",
        @"localhost\SQLEXPRESS",
        @"(local)\SQLEXPRESS",
        @"np:\\.\pipe\MSSQL$SQLEXPRESS\sql\query",
        @".\SQLEXPRESS01",
        @"localhost\SQLEXPRESS01",
        @"(local)\SQLEXPRESS01",
        @"np:\\.\pipe\MSSQL$SQLEXPRESS01\sql\query"
    };

    var results = new List<string>();
    foreach (var dataSource in dataSources.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
    {
        builder.DataSource = dataSource;
        results.Add(builder.ConnectionString);
    }

    return results;
}

static async Task<SqlConnection> OpenFirstAvailableConnectionAsync(IReadOnlyList<string> candidates)
{
    var failures = new List<string>();
    foreach (var candidate in candidates)
    {
        var connection = new SqlConnection(candidate);
        try
        {
            await connection.OpenAsync();
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine($"Connected to SQL Server using Data Source={new SqlConnectionStringBuilder(candidate).DataSource}");
            return connection;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            failures.Add($"{new SqlConnectionStringBuilder(candidate).DataSource}: {ex.Message.Split(Environment.NewLine)[0]}");
            await connection.DisposeAsync();
        }
    }

    throw new InvalidOperationException("Unable to connect to SQL Server. Tried: " + string.Join(" | ", failures));
}

static async Task<List<AugmentRow>> LoadAugmentsAsync(SqlConnection connection)
{
    const string sql = """
        SELECT Id, AugmentKey, Name, ModeName, Rarity, Tier, SeriesKey, EffectText, Tags, SynergyNotes, PatchVersion, SourceUrl, Notes
        FROM dbo.LolAramAugments
        WHERE ModeName = @ModeName
        ORDER BY Name;
        """;

    var results = new List<AugmentRow>();
    await using var command = new SqlCommand(sql, connection);
    command.Parameters.Add("@ModeName", SqlDbType.NVarChar, 100).Value = modeName;
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        results.Add(new AugmentRow(
            reader.GetInt32(reader.GetOrdinal("Id")),
            ReadString(reader, "AugmentKey"),
            ReadString(reader, "Name"),
            ReadString(reader, "ModeName"),
            ReadString(reader, "Rarity"),
            ReadNullableString(reader, "Tier"),
            NormalizeSeriesKey(ReadNullableString(reader, "SeriesKey")),
            ReadString(reader, "EffectText"),
            ReadNullableString(reader, "Tags"),
            ReadNullableString(reader, "SynergyNotes"),
            ReadString(reader, "PatchVersion"),
            ReadNullableString(reader, "SourceUrl"),
            ReadNullableString(reader, "Notes")));
    }

    return results;
}

static async Task<List<GuideRow>> LoadGuidesAsync(SqlConnection connection)
{
    const string sql = """
        SELECT Id, ChampionKey, ChampionName, LocalizedName, ModeName, PatchVersion, RoleSummary, CoreItems,
               SituationalItems, Augments, SummonerSpells, SkillOrder, PlaystyleTips, PositioningTips,
               Weaknesses, SourceUrl, Notes
        FROM dbo.LolAramGuides
        WHERE ModeName = @ModeName
        ORDER BY ChampionKey;
        """;

    var results = new List<GuideRow>();
    await using var command = new SqlCommand(sql, connection);
    command.Parameters.Add("@ModeName", SqlDbType.NVarChar, 100).Value = modeName;
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        results.Add(new GuideRow(
            reader.GetInt32(reader.GetOrdinal("Id")),
            ReadString(reader, "ChampionKey"),
            ReadString(reader, "ChampionName"),
            ReadNullableString(reader, "LocalizedName"),
            ReadString(reader, "ModeName"),
            ReadString(reader, "PatchVersion"),
            ReadString(reader, "RoleSummary"),
            ReadString(reader, "CoreItems"),
            ReadNullableString(reader, "SituationalItems"),
            ReadNullableString(reader, "Augments"),
            ReadNullableString(reader, "SummonerSpells"),
            ReadNullableString(reader, "SkillOrder"),
            ReadNullableString(reader, "PlaystyleTips"),
            ReadNullableString(reader, "PositioningTips"),
            ReadNullableString(reader, "Weaknesses"),
            ReadNullableString(reader, "SourceUrl"),
            ReadNullableString(reader, "Notes")));
    }

    return results;
}

static async Task CreateBackupAsync(SqlConnection connection, string timestamp)
{
    var guideTable = $"dbo.LolAramGuides_CurationBackup_{timestamp}";
    var augmentTable = $"dbo.LolAramAugments_CurationBackup_{timestamp}";

    var sql = $"""
        SELECT * INTO {guideTable} FROM dbo.LolAramGuides;
        SELECT * INTO {augmentTable} FROM dbo.LolAramAugments;
        """;

    await using var command = new SqlCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
}

static Dictionary<string, AugmentRow> BuildAugmentLookup(IEnumerable<AugmentRow> augments)
{
    var lookup = new Dictionary<string, AugmentRow>(StringComparer.OrdinalIgnoreCase);
    foreach (var augment in augments)
    {
        Add(augment.Name, augment);
        Add(augment.AugmentKey, augment);
        Add(NormalizeName(augment.Name), augment);
    }

    return lookup;

    void Add(string? key, AugmentRow augment)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            lookup.TryAdd(key.Trim(), augment);
        }
    }
}

static AugmentJudgement BuildAugmentJudgement(AugmentRow augment)
{
    var tags = InferTags(augment);
    var archetypes = BuildArchetypes(tags, augment);
    var itemPairs = BuildItemPairs(tags, augment);
    var timing = augment.Rarity.Trim().ToLowerInvariant() switch
    {
        "silver" => "前期用來補核心屬性或先定方向",
        "gold" => "中期若能貼合英雄技能組即可優先拿",
        "prismatic" => "稜彩階通常以能直接改變玩法或補足勝利條件者優先",
        _ => "依英雄定位與當前裝備判斷"
    };
    var seriesText = string.IsNullOrWhiteSpace(augment.SeriesKey)
        ? "沒有套裝時，以效果文字和英雄技能組互動為主"
        : $"屬於「{DisplaySeries(augment.SeriesKey)}」套裝，若後續能湊 2/3/4 件，優先級會上升";
    var tierText = string.IsNullOrWhiteSpace(augment.Tier) ? "未評級" : $"{augment.Tier} 級";

    var notes = $"{marker}: {augment.Name} 是{DisplayRarity(augment.Rarity)}、{tierText}海克斯，適合{archetypes}。{timing}；{seriesText}。搭配裝備方向：{itemPairs}。";
    var synergy = $"{marker}: 優先給能穩定觸發「{string.Join("、", tags.Take(4))}」的英雄；若隊伍已需要{BuildTeamNeed(tags)}，可提高評價，反之與英雄主要輸出模式不合時降級。";
    return new AugmentJudgement(notes, synergy, tags);
}

static GuideJudgement BuildGuideJudgement(GuideRow guide, IReadOnlyDictionary<string, AugmentRow> augmentLookup)
{
    var picks = ParseRecommendedAugments(guide.Augments, augmentLookup);
    var tagScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var pick in picks)
    {
        foreach (var tag in InferTags(pick.Augment))
        {
            tagScores[tag] = tagScores.GetValueOrDefault(tag) + TierWeight(pick.Tier) + RarityWeight(pick.Augment.Rarity);
        }
    }

    AddTextSignals(tagScores, guide.RoleSummary);
    AddTextSignals(tagScores, guide.CoreItems);
    AddTextSignals(tagScores, guide.PlaystyleTips);

    var role = BuildRoleLabel(tagScores, guide);
    var priorities = picks.Count > 0
        ? string.Join("、", picks.Take(6).Select(pick => $"{pick.Name}{FormatPickMeta(pick)}"))
        : BuildFallbackAugmentDirection(tagScores);
    var topTags = tagScores.OrderByDescending(pair => pair.Value).Select(pair => pair.Key).Take(5).ToList();
    var itemAdvice = BuildChampionItemAdvice(topTags, guide);
    var avoid = BuildAvoidAdvice(topTags);
    var notes = $"{marker}: {DisplayChampionName(guide)} 目前判斷偏「{role}」。海克斯優先看：{priorities}。裝備搭配方向：{itemAdvice}。不建議硬拿與主要傷害/功能不合的選項，尤其是{avoid}。";
    var augmentText = $"人工推薦方向：{BuildFallbackAugmentDirection(tagScores)}。白銀補基礎屬性，黃金看核心互動，稜彩優先選能放大主要玩法或完成套裝者。";
    return new GuideJudgement(notes, augmentText);
}

static List<AugmentPick> ParseRecommendedAugments(string? text, IReadOnlyDictionary<string, AugmentRow> lookup)
{
    var results = new List<AugmentPick>();
    if (string.IsNullOrWhiteSpace(text))
    {
        return results;
    }

    var normalized = text
        .Replace("OP.GG 推薦海克斯（依頁面順序）：", string.Empty)
        .Replace("OP.GG recommended champions:", string.Empty)
        .Replace("（", "(")
        .Replace("）", ")");

    foreach (var raw in normalized.Split(new[] { '；', ';', '、' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var item = raw.Trim();
        if (item.Length == 0)
        {
            continue;
        }

        var metaMatch = Regex.Match(item, @"^(?<name>.+?)\s*\((?<tier>[^/)]*)/?(?<rarity>[^)]*)\)");
        var name = metaMatch.Success ? metaMatch.Groups["name"].Value.Trim() : item;
        var tier = metaMatch.Success ? metaMatch.Groups["tier"].Value.Trim() : string.Empty;
        var rarity = metaMatch.Success ? metaMatch.Groups["rarity"].Value.Trim() : string.Empty;
        var key = NormalizeName(name);
        if (lookup.TryGetValue(name, out var augment) || lookup.TryGetValue(key, out augment))
        {
            results.Add(new AugmentPick(name, tier, rarity, augment));
        }
    }

    return results;
}

static List<string> InferTags(AugmentRow augment)
{
    var tags = SplitTags(augment.Tags).ToList();
    foreach (var seriesTag in SplitTags(augment.SeriesKey))
    {
        AddTag(tags, seriesTag);
    }

    var text = $"{augment.Name} {augment.AugmentKey} {augment.EffectText}".ToLowerInvariant();

    AddIf(tags, "burn", text, "burn", "燃燒", "灼燒", "immolate", "liandry", "blackfire", "malignance", "sunfire");
    AddIf(tags, "firecracker", text, "firecracker", "爆竹");
    AddIf(tags, "snowday", text, "snowball", "雪球", "snowday");
    AddIf(tags, "stackosaurus", text, "stack", "層數", "堆疊", "成長", "stackosaurus");
    AddIf(tags, "self_destruct", text, "self-destruct", "自爆", "復活倒數", "炸彈");
    AddIf(tags, "low_health_ally", text, "heal", "shield", "治療", "護盾", "友軍", "low health");
    AddIf(tags, "archmage", text, "ability haste", "技能急速", "冷卻", "spell", "技能");
    AddIf(tags, "automation", text, "auto-cast", "自動施放", "automation");
    AddIf(tags, "high_roller", text, "anvil", "鍛造器", "屬性加成", "high roller");
    AddIf(tags, "coinrain", text, "coin", "gold", "金幣", "錢幣");
    AddIf(tags, "magic_damage", text, "magic damage", "魔法傷害", "ability power", "ap");
    AddIf(tags, "physical_damage", text, "physical damage", "物理傷害", "attack damage", "ad");
    AddIf(tags, "true_damage", text, "true damage", "真實傷害");
    AddIf(tags, "critical", text, "critical", "crit", "暴擊");
    AddIf(tags, "attack_speed", text, "attack speed", "攻速");
    AddIf(tags, "on_hit", text, "on-hit", "命中", "普攻");
    AddIf(tags, "ability_haste", text, "ability haste", "技能急速", "cooldown", "冷卻");
    AddIf(tags, "movement_speed", text, "movement speed", "move speed", "移動速度", "跑速");
    AddIf(tags, "crowd_control", text, "immobil", "ground", "stun", "slow", "定身", "緩速", "暈眩", "控場");
    AddIf(tags, "missile", text, "missile", "飛彈", "導彈");
    AddIf(tags, "multi_hit", text, "each time", "每第", "再次", "多段");
    AddIf(tags, "tank", text, "health", "armor", "magic resist", "生命值", "護甲", "魔法防禦", "坦");
    AddIf(tags, "team_support", text, "ally", "allied", "team", "友軍", "隊友", "全隊");
    AddIf(tags, "economy", text, "gold", "anvil", "金幣", "鍛造器", "錢幣");

    return tags.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
}

static void AddTextSignals(Dictionary<string, int> scores, string? text)
{
    if (string.IsNullOrWhiteSpace(text))
    {
        return;
    }

    var lower = text.ToLowerInvariant();
    ScoreIf("magic_damage", 2, "ap", "魔法", "法術", "法強", "黑焰", "蘭德里", "惡意", "魔穿");
    ScoreIf("physical_damage", 2, "ad", "物理", "攻擊", "致命", "物穿");
    ScoreIf("critical", 2, "暴擊", "crit");
    ScoreIf("attack_speed", 2, "攻速", "attack speed");
    ScoreIf("tank", 2, "坦", "生命", "護甲", "魔防", "日炎", "心之鋼");
    ScoreIf("team_support", 2, "輔助", "隊友", "護盾", "治療");
    ScoreIf("burn", 2, "燃燒", "灼燒", "黑焰", "蘭德里", "日炎");
    ScoreIf("ability_haste", 1, "技能急速", "冷卻");

    void ScoreIf(string tag, int value, params string[] needles)
    {
        if (needles.Any(needle => lower.Contains(needle, StringComparison.OrdinalIgnoreCase)))
        {
            scores[tag] = scores.GetValueOrDefault(tag) + value;
        }
    }
}

static string BuildRoleLabel(Dictionary<string, int> scores, GuideRow guide)
{
    var top = scores.OrderByDescending(pair => pair.Value).Select(pair => pair.Key).Take(4).ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (top.Contains("team_support") || top.Contains("low_health_ally")) return "護盾/治療團隊輔助";
    if (top.Contains("tank") && (top.Contains("physical_damage") || top.Contains("on_hit"))) return "坦鬥士/前排輸出";
    if (top.Contains("tank")) return "前排坦克/開戰";
    if (top.Contains("critical") || top.Contains("attack_speed") || top.Contains("on_hit")) return "普攻暴擊/攻速輸出";
    if (top.Contains("physical_damage")) return "物理技能/刺客收割";
    if (top.Contains("burn")) return "燃燒消耗/AP持續傷害";
    if (top.Contains("magic_damage") || top.Contains("ability_haste")) return "AP技能消耗/法師";
    return IsPlaceholder(guide.RoleSummary) ? "依推薦海克斯彈性出裝" : guide.RoleSummary;
}

static string BuildFallbackAugmentDirection(Dictionary<string, int> scores)
{
    var top = scores.OrderByDescending(pair => pair.Value).Select(pair => pair.Key).Take(4).ToList();
    if (top.Count == 0)
    {
        return "以 S/A 級通用海克斯、保命與主要傷害屬性為優先";
    }

    return string.Join("、", top.Select(DisplayTag));
}

static string BuildChampionItemAdvice(IReadOnlyList<string> tags, GuideRow guide)
{
    if (!IsPlaceholder(guide.CoreItems) && !string.IsNullOrWhiteSpace(guide.CoreItems))
    {
        return $"優先沿用核心裝「{guide.CoreItems}」，再依海克斯補強缺口";
    }

    if (tags.Any(tag => tag is "team_support" or "low_health_ally")) return "月石/流水/救贖/香爐等護盾治療裝，必要時補生存";
    if (tags.Any(tag => tag is "burn" or "magic_damage")) return "黑焰火炬、蘭德里的折磨、惡意、魔穿與技能急速";
    if (tags.Any(tag => tag is "critical" or "attack_speed" or "on_hit")) return "攻速、暴擊、命中特效與射程/保命裝";
    if (tags.Any(tag => tag is "physical_damage")) return "物理致命、物穿、技能急速或收割裝";
    if (tags.Any(tag => tag is "tank" or "self_destruct")) return "生命、雙抗、日炎/荊棘/魔防與可持續作戰裝";
    return "依英雄主要傷害屬性補足輸出，再用防裝或保命裝避免被秒";
}

static string BuildAvoidAdvice(IReadOnlyList<string> tags)
{
    if (tags.Contains("team_support")) return "純暴擊或純刺客海克斯";
    if (tags.Contains("critical") || tags.Contains("attack_speed")) return "純 AP 或只強化技能的海克斯";
    if (tags.Contains("magic_damage")) return "只強化普攻暴擊但沒有觸發手段的海克斯";
    if (tags.Contains("tank")) return "需要長時間後排輸出的玻璃大砲選項";
    return "與英雄主輸出方式不一致的低評級選項";
}

static string BuildArchetypes(IReadOnlyList<string> tags, AugmentRow augment)
{
    var parts = new List<string>();
    if (tags.Any(tag => tag is "burn" or "magic_damage" or "archmage")) parts.Add("AP法師/技能消耗角");
    if (tags.Any(tag => tag is "critical" or "attack_speed" or "on_hit" or "physical_damage")) parts.Add("普攻、暴擊或物理輸出角");
    if (tags.Any(tag => tag is "tank" or "self_destruct")) parts.Add("坦克、前排或需要進場換血的英雄");
    if (tags.Any(tag => tag is "team_support" or "low_health_ally")) parts.Add("治療、護盾與團隊增益英雄");
    if (tags.Any(tag => tag is "snowday" or "movement_speed" or "crowd_control")) parts.Add("開戰、追擊或雪球命中率高的英雄");
    if (tags.Any(tag => tag is "coinrain" or "high_roller" or "economy")) parts.Add("容易參與擊殺、能快速把經濟轉成裝備優勢的英雄");
    return parts.Count == 0 ? "能穩定觸發效果且不偏離主玩法的英雄" : string.Join("、", parts.Distinct());
}

static string BuildItemPairs(IReadOnlyList<string> tags, AugmentRow augment)
{
    var parts = new List<string>();
    if (tags.Contains("burn")) parts.Add("黑焰火炬/蘭德里的折磨/惡意/日炎或火甲系");
    if (tags.Contains("magic_damage")) parts.Add("AP、魔穿、技能急速");
    if (tags.Contains("physical_damage")) parts.Add("物攻、物穿、致命或技能急速");
    if (tags.Contains("critical")) parts.Add("暴擊裝與攻速裝");
    if (tags.Contains("attack_speed") || tags.Contains("on_hit")) parts.Add("攻速與命中特效");
    if (tags.Contains("tank")) parts.Add("生命、護甲、魔防與持續作戰裝");
    if (tags.Contains("team_support") || tags.Contains("low_health_ally")) parts.Add("護盾治療與團隊輔助裝");
    if (tags.Contains("economy")) parts.Add("能把額外經濟快速轉為核心裝的路線");
    return parts.Count == 0 ? "依英雄主屬性補足核心裝" : string.Join("；", parts.Distinct());
}

static string BuildTeamNeed(IReadOnlyList<string> tags)
{
    if (tags.Contains("team_support") || tags.Contains("low_health_ally")) return "保排、續戰或保護低血量隊友";
    if (tags.Contains("tank")) return "前排、承傷或開戰";
    if (tags.Contains("burn") || tags.Contains("magic_damage")) return "持續消耗與魔法傷害";
    if (tags.Contains("critical") || tags.Contains("physical_damage")) return "物理收割與後排輸出";
    if (tags.Contains("snowday") || tags.Contains("movement_speed")) return "開戰與追擊";
    return "穩定觸發與戰鬥節奏";
}

static IEnumerable<string> SplitTags(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        yield break;
    }

    foreach (var item in value.Split(new[] { ';', '；', ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var normalized = NormalizeTag(item);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            yield return normalized;
        }
    }
}

static string? NormalizeTag(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    var text = value.Trim().ToLowerInvariant();
    return text switch
    {
        "疊層" or "成長" or "堆疊暴龍" or "stacking_dino" => "stackosaurus",
        "嗚咿嗚咿" or "治療" or "護盾" when text.Contains("嗚") => "low_health_ally",
        "雪球" or "下雪天" or "snowday" => "snowday",
        "爆竹系列" or "爆竹" => "firecracker",
        "自爆" or "自爆系列" => "self_destruct",
        "大法師" or "archmage" => "archmage",
        "自動化" or "全自動" or "automation" => "automation",
        "土豪賭客" or "high_roller" => "high_roller",
        "錢如雨下" or "coinrain" or "金錢" => "coinrain",
        "魔法傷害" => "magic_damage",
        "物理傷害" => "physical_damage",
        "真實傷害" => "true_damage",
        "技能急速" => "ability_haste",
        "跑速" => "movement_speed",
        "生命 / 坦度" or "坦度" or "生命" => "tank",
        "團隊增益" => "team_support",
        "普攻" => "on_hit",
        "暴擊" => "critical",
        "攻速" => "attack_speed",
        _ => text.Replace(" ", "_")
    };
}

static string? NormalizeSeriesKey(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    var text = value.Trim().ToLowerInvariant();
    return text switch
    {
        "stacking_dino" or "stackosaurus_rex" or "stackosaurus" or "堆疊暴龍" => "stackosaurus",
        "snowday" or "snow_day" or "下雪天" or "雪球" => "snowday",
        "low_health_ally" or "wee_woo" or "嗚咿嗚咿" => "low_health_ally",
        "self_destruct" or "自爆" => "self_destruct",
        "archmage" or "大法師" => "archmage",
        "automation" or "自動化" => "automation",
        "high_roller" or "土豪賭客" => "high_roller",
        "coinrain" or "make_it_rain" or "錢如雨下" => "coinrain",
        "firecracker" or "爆竹" => "firecracker",
        _ => text
    };
}

static void AddIf(List<string> tags, string tag, string text, params string[] needles)
{
    if (needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase)))
    {
        AddTag(tags, tag);
    }
}

static void AddTag(List<string> tags, string? tag)
{
    tag = NormalizeTag(tag);
    if (!string.IsNullOrWhiteSpace(tag) && !tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
    {
        tags.Add(tag);
    }
}

static string MergeTags(string? original, IReadOnlyList<string> generated)
{
    var merged = new List<string>();
    foreach (var tag in SplitTags(original))
    {
        AddTag(merged, tag);
    }

    foreach (var tag in generated)
    {
        AddTag(merged, tag);
    }

    return string.Join("; ", merged.Take(10));
}

static string MergeCuratorText(string? original, string generated, int maxLength)
{
    generated = TrimTo(generated, maxLength);
    if (string.IsNullOrWhiteSpace(original) || IsPlaceholder(original))
    {
        return generated;
    }

    var existing = original.Trim();
    var markerIndex = existing.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    if (markerIndex >= 0)
    {
        var prefix = existing[..markerIndex].Trim().TrimEnd('|').Trim();
        return string.IsNullOrWhiteSpace(prefix)
            ? generated
            : TrimTo($"{prefix} | {generated}", maxLength);
    }

    var legacyMarkerIndex = existing.IndexOf("人工彙整", StringComparison.OrdinalIgnoreCase);
    if (legacyMarkerIndex >= 0)
    {
        var prefix = existing[..legacyMarkerIndex].Trim().TrimEnd('|').Trim();
        return string.IsNullOrWhiteSpace(prefix)
            ? generated
            : TrimTo($"{prefix} | {generated}", maxLength);
    }

    return TrimTo($"{existing} | {generated}", maxLength);
}

static bool IsPlaceholder(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return true;
    }

    var text = value.Trim();
    return text.Contains("待人工", StringComparison.OrdinalIgnoreCase)
        || text.Contains("尚未", StringComparison.OrdinalIgnoreCase)
        || text.Contains("補齊", StringComparison.OrdinalIgnoreCase)
        || text.Equals("manual", StringComparison.OrdinalIgnoreCase);
}

static string TrimTo(string value, int maxLength)
{
    value = Regex.Replace(value, @"\s+", " ").Trim();
    return value.Length <= maxLength ? value : value[..(maxLength - 1)];
}

static int TierWeight(string? tier)
{
    return tier?.Trim().ToUpperInvariant() switch
    {
        "S" or "S+" => 5,
        "A" or "A+" => 4,
        "B" => 3,
        "C" => 2,
        _ => 1
    };
}

static int RarityWeight(string? rarity)
{
    return rarity?.Trim().ToLowerInvariant() switch
    {
        "prismatic" => 3,
        "gold" => 2,
        _ => 1
    };
}

static string FormatPickMeta(AugmentPick pick)
{
    var tier = string.IsNullOrWhiteSpace(pick.Tier) ? pick.Augment.Tier : pick.Tier;
    var rarity = string.IsNullOrWhiteSpace(pick.Rarity) || pick.Rarity.Contains("未知") ? DisplayRarity(pick.Augment.Rarity) : pick.Rarity;
    return string.IsNullOrWhiteSpace(tier) ? $"({rarity})" : $"({tier}/{rarity})";
}

static string DisplayChampionName(GuideRow guide)
{
    return string.IsNullOrWhiteSpace(guide.LocalizedName) ? guide.ChampionName : $"{guide.LocalizedName}({guide.ChampionName})";
}

static string DisplayRarity(string? value)
{
    return value?.Trim().ToLowerInvariant() switch
    {
        "prismatic" => "稜彩",
        "gold" => "黃金",
        "silver" => "白銀",
        _ => "未知階級"
    };
}

static string DisplaySeries(string? value)
{
    return NormalizeSeriesKey(value) switch
    {
        "snowday" => "雪球",
        "self_destruct" => "自爆",
        "stackosaurus" => "堆疊暴龍",
        "low_health_ally" => "嗚咿嗚咿",
        "archmage" => "大法師",
        "automation" => "自動化",
        "high_roller" => "土豪賭客",
        "coinrain" => "錢如雨下",
        "firecracker" => "爆竹",
        _ => "無套裝/未知套裝"
    };
}

static string DisplayTag(string tag)
{
    return tag switch
    {
        "burn" => "燃燒",
        "firecracker" => "爆竹",
        "snowday" => "雪球",
        "self_destruct" => "自爆",
        "stackosaurus" => "堆疊暴龍",
        "low_health_ally" => "嗚咿嗚咿/治療護盾",
        "archmage" => "大法師/技能急速",
        "automation" => "自動化",
        "high_roller" => "土豪賭客",
        "coinrain" => "錢如雨下",
        "magic_damage" => "魔法傷害",
        "physical_damage" => "物理傷害",
        "true_damage" => "真實傷害",
        "critical" => "暴擊",
        "attack_speed" => "攻速",
        "on_hit" => "普攻/命中特效",
        "ability_haste" => "技能急速",
        "movement_speed" => "跑速",
        "crowd_control" => "控場",
        "missile" => "飛彈",
        "multi_hit" => "多段觸發",
        "tank" => "生命/坦度",
        "team_support" => "團隊增益",
        "economy" => "經濟",
        _ => tag
    };
}

static string NormalizeName(string value)
{
    return Regex.Replace(value.Trim().ToLowerInvariant(), @"[\s'’:\-!?.]+", string.Empty);
}

static async Task ApplyAugmentUpdatesAsync(SqlConnection connection, IReadOnlyList<AugmentUpdate> updates)
{
    const string sql = """
        UPDATE dbo.LolAramAugments
        SET Notes = @Notes,
            SynergyNotes = @SynergyNotes,
            Tags = @Tags,
            UpdatedAt = SYSUTCDATETIME()
        WHERE Id = @Id;
        """;

    foreach (var update in updates)
    {
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = update.Id;
        command.Parameters.Add("@Notes", SqlDbType.NVarChar, 1200).Value = update.Notes;
        command.Parameters.Add("@SynergyNotes", SqlDbType.NVarChar, 1200).Value = update.SynergyNotes;
        command.Parameters.Add("@Tags", SqlDbType.NVarChar, 1000).Value = update.Tags;
        await command.ExecuteNonQueryAsync();
    }
}

static async Task ApplyGuideUpdatesAsync(SqlConnection connection, IReadOnlyList<GuideUpdate> updates)
{
    const string sql = """
        UPDATE dbo.LolAramGuides
        SET Notes = @Notes,
            Augments = @Augments,
            UpdatedAt = SYSUTCDATETIME()
        WHERE Id = @Id;
        """;

    foreach (var update in updates)
    {
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = update.Id;
        command.Parameters.Add("@Notes", SqlDbType.NVarChar, 1200).Value = update.Notes;
        command.Parameters.Add("@Augments", SqlDbType.NVarChar, 1000).Value = update.Augments;
        await command.ExecuteNonQueryAsync();
    }
}

static List<AugmentRow> ApplyAugmentsInMemory(List<AugmentRow> rows, IReadOnlyList<AugmentUpdate> updates)
{
    var map = updates.ToDictionary(update => update.Id);
    return rows.Select(row => map.TryGetValue(row.Id, out var update)
        ? row with { Notes = update.Notes, SynergyNotes = update.SynergyNotes, Tags = update.Tags }
        : row).ToList();
}

static List<GuideRow> ApplyGuidesInMemory(List<GuideRow> rows, IReadOnlyList<GuideUpdate> updates)
{
    var map = updates.ToDictionary(update => update.Id);
    return rows.Select(row => map.TryGetValue(row.Id, out var update)
        ? row with { Notes = update.Notes, Augments = update.Augments }
        : row).ToList();
}

static string BuildReport(
    string timestamp,
    bool dryRun,
    IReadOnlyList<AugmentRow> beforeAugments,
    IReadOnlyList<GuideRow> beforeGuides,
    IReadOnlyList<AugmentRow> afterAugments,
    IReadOnlyList<GuideRow> afterGuides,
    int augmentUpdateCount,
    int guideUpdateCount)
{
    var beforeAugmentNotes = FilledRatio(beforeAugments, row => row.Notes);
    var afterAugmentNotes = FilledRatio(afterAugments, row => row.Notes);
    var afterAugmentSynergy = FilledRatio(afterAugments, row => row.SynergyNotes);
    var afterAugmentTags = FilledRatio(afterAugments, row => row.Tags);
    var beforeGuideNotes = FilledRatio(beforeGuides, row => row.Notes);
    var afterGuideNotes = FilledRatio(afterGuides, row => row.Notes);
    var afterGuideAugments = FilledRatio(afterGuides, row => row.Augments);

    return $"""
        # ARAM RAG Curation Report {timestamp}

        Mode: {(dryRun ? "dry-run" : "applied")}

        ## Coverage
        - Augment Notes: {beforeAugmentNotes.Text} -> {afterAugmentNotes.Text}
        - Augment SynergyNotes: {afterAugmentSynergy.Text}
        - Augment Tags: {afterAugmentTags.Text}
        - Guide Notes: {beforeGuideNotes.Text} -> {afterGuideNotes.Text}
        - Guide Augments: {afterGuideAugments.Text}

        ## Updated Rows
        - Augments updated: {augmentUpdateCount}
        - Guides updated: {guideUpdateCount}

        ## Source Validation
        - Riot official support page: ARAM Mayhem has four augment selection phases and Silver/Gold/Prismatic tiers.
        - ARAMOnly: used as public effect-description cross-check.
        - arammayhem.net: used as public tier/stat direction cross-check.
        - OP.GG imported recommendations already stored in guide rows were used as champion-specific candidate lists.
        - Community guide pages were used only for high-level strategy patterns, not copied as article text.

        ## Rollback
        - Database backup tables were created only in apply mode:
          - dbo.LolAramGuides_CurationBackup_{timestamp}
          - dbo.LolAramAugments_CurationBackup_{timestamp}
        """;
}

static (int Filled, int Total, string Text) FilledRatio<T>(IReadOnlyList<T> rows, Func<T, string?> selector)
{
    var total = rows.Count;
    var filled = rows.Count(row => !IsPlaceholder(selector(row)));
    var percentage = total == 0 ? 0 : Math.Round(filled * 100m / total, 1);
    return (filled, total, $"{filled}/{total} ({percentage}%)");
}

static string ReadString(SqlDataReader reader, string columnName)
{
    var ordinal = reader.GetOrdinal(columnName);
    return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
}

static string? ReadNullableString(SqlDataReader reader, string columnName)
{
    var ordinal = reader.GetOrdinal(columnName);
    return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}

record AugmentRow(
    int Id,
    string AugmentKey,
    string Name,
    string ModeName,
    string Rarity,
    string? Tier,
    string? SeriesKey,
    string EffectText,
    string? Tags,
    string? SynergyNotes,
    string PatchVersion,
    string? SourceUrl,
    string? Notes);

record GuideRow(
    int Id,
    string ChampionKey,
    string ChampionName,
    string? LocalizedName,
    string ModeName,
    string PatchVersion,
    string RoleSummary,
    string CoreItems,
    string? SituationalItems,
    string? Augments,
    string? SummonerSpells,
    string? SkillOrder,
    string? PlaystyleTips,
    string? PositioningTips,
    string? Weaknesses,
    string? SourceUrl,
    string? Notes);

record AugmentJudgement(string Notes, string SynergyNotes, IReadOnlyList<string> Tags);
record GuideJudgement(string Notes, string Augments);
record AugmentUpdate(int Id, string Notes, string SynergyNotes, string Tags);
record GuideUpdate(int Id, string Notes, string Augments);
record AugmentPick(string Name, string Tier, string Rarity, AugmentRow Augment);
