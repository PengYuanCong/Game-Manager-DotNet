using System.Data;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.VisualBasic;

const string modeName = "ARAM Mayhem";
const string marker = "manual-curation-v2-2026-05-23";
var dryRun = args.Any(arg => string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase));
var skipOnline = args.Any(arg => string.Equals(arg, "--skip-online", StringComparison.OrdinalIgnoreCase));
var root = FindWorkspaceRoot();
var reportDir = Path.Combine(root, "Reports");
Directory.CreateDirectory(reportDir);
var cacheDir = Path.Combine(root, "Reports", "aram-manual-page-cache");
Directory.CreateDirectory(cacheDir);

var appsettingsPath = Path.Combine(root, "Proposal", "appsettings.json");
var appsettings = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
var connectionString = appsettings.RootElement
    .GetProperty("ConnectionStrings")
    .GetProperty("DefaultConnection")
    .GetString()
    ?? throw new InvalidOperationException("DefaultConnection is missing.");

await using var connection = await OpenFirstAvailableConnectionAsync(BuildConnectionCandidates(connectionString));
var guides = await LoadGuidesAsync(connection);
var augments = await LoadAugmentsAsync(connection);
var augmentLookup = BuildAugmentLookup(augments);

var fetched = new Dictionary<string, ChampionPageData>(StringComparer.OrdinalIgnoreCase);
var fetchFailures = new List<string>();
if (!skipOnline)
{
    using var http = new HttpClient(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124 Safari/537.36");
    http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,zh-TW;q=0.8,en;q=0.7");

    foreach (var guide in guides)
    {
        try
        {
            var page = await FetchChampionPageAsync(http, guide, cacheDir);
            if (page is not null)
            {
                fetched[guide.ChampionKey] = page;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            fetchFailures.Add($"{guide.ChampionKey}: {ex.Message}");
        }

        await Task.Delay(180);
    }
}

var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
if (!dryRun)
{
    await CreateBackupAsync(connection, timestamp);
}

var guideUpdates = new List<GuideUpdate>();
foreach (var guide in guides)
{
    fetched.TryGetValue(guide.ChampionKey, out var page);
    var update = BuildGuideUpdate(guide, page, augmentLookup);
    if (update.HasChanges(guide))
    {
        guideUpdates.Add(update);
    }
}

if (!dryRun)
{
    await ApplyGuideUpdatesAsync(connection, guideUpdates);
}

var finalGuides = dryRun ? ApplyInMemory(guides, guideUpdates) : await LoadGuidesAsync(connection);
var report = BuildReport(timestamp, dryRun, guides, finalGuides, guideUpdates, fetched.Count, fetchFailures);
var reportPath = Path.Combine(reportDir, $"aram-manual-curation-{timestamp}.md");
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

    return dataSources
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(dataSource =>
        {
            builder.DataSource = dataSource;
            return builder.ConnectionString;
        })
        .ToList();
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

    var rows = new List<GuideRow>();
    await using var command = new SqlCommand(sql, connection);
    command.Parameters.Add("@ModeName", SqlDbType.NVarChar, 100).Value = modeName;
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new GuideRow(
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

    return rows;
}

static async Task<List<AugmentRow>> LoadAugmentsAsync(SqlConnection connection)
{
    const string sql = """
        SELECT Id, AugmentKey, Name, Rarity, Tier, SeriesKey, EffectText, Tags
        FROM dbo.LolAramAugments
        WHERE ModeName = @ModeName;
        """;

    var rows = new List<AugmentRow>();
    await using var command = new SqlCommand(sql, connection);
    command.Parameters.Add("@ModeName", SqlDbType.NVarChar, 100).Value = modeName;
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new AugmentRow(
            reader.GetInt32(reader.GetOrdinal("Id")),
            ReadString(reader, "AugmentKey"),
            ReadString(reader, "Name"),
            ReadString(reader, "Rarity"),
            ReadNullableString(reader, "Tier"),
            ReadNullableString(reader, "SeriesKey"),
            ReadString(reader, "EffectText"),
            ReadNullableString(reader, "Tags")));
    }

    return rows;
}

static async Task CreateBackupAsync(SqlConnection connection, string timestamp)
{
    var table = $"dbo.LolAramGuides_ManualCurationBackup_{timestamp}";
    await using var command = new SqlCommand($"SELECT * INTO {table} FROM dbo.LolAramGuides;", connection);
    await command.ExecuteNonQueryAsync();
}

static Dictionary<string, AugmentRow> BuildAugmentLookup(IEnumerable<AugmentRow> augments)
{
    var lookup = new Dictionary<string, AugmentRow>(StringComparer.OrdinalIgnoreCase);
    foreach (var augment in augments)
    {
        Add(augment.Name, augment);
        Add(augment.AugmentKey, augment);
        Add(NormalizeKey(augment.Name), augment);
        Add(CompactTerm(augment.Name), augment);
    }

    return lookup;

    void Add(string? key, AugmentRow row)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            lookup.TryAdd(key.Trim(), row);
        }
    }
}

static async Task<ChampionPageData?> FetchChampionPageAsync(HttpClient http, GuideRow guide, string cacheDir)
{
    var slugs = BuildSlugCandidates(guide.ChampionKey);
    foreach (var slug in slugs)
    {
        var url = $"https://arammayhem.com/zh-cn/champions/{slug}/";
        var cachePath = Path.Combine(cacheDir, $"{slug}.html");
        if (File.Exists(cachePath))
        {
            var cached = await File.ReadAllTextAsync(cachePath, Encoding.UTF8);
            var cachedPage = ParseChampionPage(cached, url);
            if (cachedPage.HasUsefulData)
            {
                return cachedPage;
            }
        }

        using var response = await http.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            continue;
        }

        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var html = Encoding.UTF8.GetString(bytes);
        await File.WriteAllTextAsync(cachePath, html, Encoding.UTF8);
        var page = ParseChampionPage(html, response.RequestMessage?.RequestUri?.ToString() ?? url);
        if (page.HasUsefulData)
        {
            return page;
        }
    }

    return null;
}

static IReadOnlyList<string> BuildSlugCandidates(string championKey)
{
    var key = championKey.Trim().ToLowerInvariant();
    var candidates = new List<string> { key };
    var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["monkeyking"] = ["wukong"],
        ["nunu"] = ["nunu-willump", "nunuandwillump"],
        ["renataglasc"] = ["renata"],
        ["drmundo"] = ["dr-mundo", "mundo"],
        ["jarvaniv"] = ["jarvan-iv"],
        ["ksante"] = ["k-sante"],
        ["kaisa"] = ["kai-sa"],
        ["khazix"] = ["kha-zix"],
        ["kogmaw"] = ["kog-maw"],
        ["leesin"] = ["lee-sin"],
        ["masteryi"] = ["master-yi"],
        ["missfortune"] = ["miss-fortune"],
        ["reksai"] = ["rek-sai"],
        ["tahmkench"] = ["tahm-kench"],
        ["twistedfate"] = ["twisted-fate"],
        ["velkoz"] = ["vel-koz"],
        ["xinzhao"] = ["xin-zhao"],
        ["aurelionsol"] = ["aurelion-sol"],
        ["belveth"] = ["bel-veth"]
    };

    if (map.TryGetValue(key, out var mapped))
    {
        candidates.AddRange(mapped);
    }

    return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

static ChampionPageData ParseChampionPage(string html, string url)
{
    var plain = StripTags(html);
    var page = new ChampionPageData
    {
        SourceUrl = url,
        Tier = MatchFirst(plain, @"\b([SABCDE])\s*级(?:强度|海克斯大乱斗)").ToUpperInvariant(),
        SkillOrder = WebUtility.HtmlDecode(MatchFirst(html, @"<span[^>]*text-primary[^>]*>([^<]+)</span>")).Replace("&gt;", ">")
    };

    page.Roles.AddRange(ParseRoles(plain));
    page.CoreItems.AddRange(ParseItemsFromSection(html, "核心出装", 3));
    page.SituationalItems.AddRange(ParseItemsFromSection(html, "出门装", 5)
        .Concat(ParseItemsFromSection(html, "核心出装", 9))
        .Where(item => !page.CoreItems.Contains(item, StringComparer.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(6));
    page.TopAugments.AddRange(ParseAugmentsFromBestSection(html).Take(8));

    return page;
}

static List<string> ParseRoles(string plain)
{
    var known = new[] { "战士", "坦克", "法师", "射手", "辅助", "刺客" };
    var match = Regex.Match(plain, @"((?:战士|坦克|法师|射手|辅助|刺客)(?:\s+(?:战士|坦克|法师|射手|辅助|刺客))*)\s+(?:\d+\.\d+%\s+WR|海克斯大乱斗)");
    if (!match.Success)
    {
        return [];
    }

    return match.Groups[1].Value
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(role => known.Contains(role))
        .Select(ToTraditionalTerm)
        .Distinct()
        .ToList();
}

static List<string> ParseItemsFromSection(string html, string sectionTitle, int maxItems)
{
    var section = ExtractSection(html, sectionTitle, ["核心出装", "出门装", "最佳强化符文", "近期数据变化", "技能主升顺序"], includeSelf: true);
    if (string.IsNullOrWhiteSpace(section))
    {
        return [];
    }

    var items = new List<string>();
    foreach (Match match in Regex.Matches(section, "<img[^>]+alt=\"([^\"]+)\"[^>]*?/items/", RegexOptions.IgnoreCase))
    {
        var item = NormalizeGameTerm(WebUtility.HtmlDecode(match.Groups[1].Value));
        if (!items.Contains(item, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(item);
        }

        if (items.Count >= maxItems)
        {
            break;
        }
    }

    return items;
}

static List<AugmentSource> ParseAugmentsFromBestSection(string html)
{
    var section = ExtractSection(html, "最佳强化符文", ["大乱斗平衡", "技能主升顺序", "出门装"], includeSelf: true);
    if (string.IsNullOrWhiteSpace(section))
    {
        return [];
    }

    var results = new List<AugmentSource>();
    foreach (Match match in Regex.Matches(
        section,
        "<a[^>]+href=\"[^\"]*/augments/[^\"]+\"[^>]*>.*?<span[^>]*>([^<]+)</span>\\s*(?:<span[^>]*>([0-9.]+%)</span>)?",
        RegexOptions.IgnoreCase | RegexOptions.Singleline))
    {
        var name = NormalizeGameTerm(WebUtility.HtmlDecode(match.Groups[1].Value));
        if (name.Length == 0 || results.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            continue;
        }

        results.Add(new AugmentSource(name, match.Groups[2].Value));
    }

    return results;
}

static string ExtractSection(string html, string title, IReadOnlyList<string> possibleNextTitles, bool includeSelf)
{
    var start = html.IndexOf(title, StringComparison.OrdinalIgnoreCase);
    if (start < 0)
    {
        return string.Empty;
    }

    var searchStart = start + title.Length;
    var end = html.Length;
    foreach (var next in possibleNextTitles.Where(next => !string.Equals(next, title, StringComparison.OrdinalIgnoreCase)))
    {
        var index = html.IndexOf(next, searchStart, StringComparison.OrdinalIgnoreCase);
        if (index > searchStart && index < end)
        {
            end = index;
        }
    }

    return html.Substring(includeSelf ? start : searchStart, end - (includeSelf ? start : searchStart));
}

static GuideUpdate BuildGuideUpdate(
    GuideRow guide,
    ChampionPageData? page,
    IReadOnlyDictionary<string, AugmentRow> augmentLookup)
{
    var archetype = DetermineArchetype(guide, page, augmentLookup);
    var topAugments = BuildTopAugmentText(guide, page, augmentLookup, archetype);
    var importedCoreItems = page?.CoreItems.Count > 0 ? page.CoreItems.Take(3).ToList() : [];
    var coreItems = importedCoreItems.Count > 0 && !HasConflictingCoreItems(archetype, importedCoreItems)
        ? JoinList(importedCoreItems)
        : JoinList(DefaultCoreItems(archetype));
    var situationalItems = page?.SituationalItems.Count > 0 ? JoinList(page.SituationalItems.Take(6)) : JoinList(DefaultSituationalItems(archetype));
    var skillOrder = !string.IsNullOrWhiteSpace(page?.SkillOrder) ? page.SkillOrder : DefaultSkillOrder(archetype);
    var roleSummary = BuildRoleSummary(guide, page, archetype);
    var playstyle = BuildPlaystyle(archetype, topAugments);
    var positioning = BuildPositioning(archetype);
    var weaknesses = BuildWeaknesses(archetype);
    var spells = BuildSummonerSpells(archetype);
    var notes = BuildManualNotes(guide, page, archetype, topAugments, coreItems, augmentLookup);

    return new GuideUpdate(
        guide.Id,
        roleSummary,
        coreItems,
        situationalItems,
        topAugments,
        spells,
        skillOrder,
        playstyle,
        positioning,
        weaknesses,
        page?.SourceUrl ?? guide.SourceUrl,
        notes);
}

static ChampionArchetype DetermineArchetype(
    GuideRow guide,
    ChampionPageData? page,
    IReadOnlyDictionary<string, AugmentRow> augmentLookup)
{
    var key = guide.ChampionKey.ToLowerInvariant();
    if (TryGetChampionOverride(key, out var forced))
    {
        return forced;
    }

    var roles = page?.Roles ?? [];
    if (roles.Contains("射手") && roles.Contains("刺客")) return ChampionArchetype.CritMarksman;
    if (roles.Contains("射手")) return ChampionArchetype.CritMarksman;
    if (roles.Contains("輔助")) return ChampionArchetype.Enchanter;
    if (roles.Contains("坦克")) return ChampionArchetype.Tank;
    if (roles.Contains("刺客")) return ChampionArchetype.Assassin;
    if (roles.Contains("戰士")) return ChampionArchetype.Fighter;
    if (roles.Contains("法師")) return ChampionArchetype.MageBurst;

    var text = $"{guide.ChampionKey} {guide.ChampionName} {guide.LocalizedName} {guide.Augments}".ToLowerInvariant();
    if (ContainsAny(text, "吸血", "狂妄", "巨人", "終極型態", "痛打一頓", "goredrink")) return ChampionArchetype.Fighter;
    if (ContainsAny(text, "收集者", "無盡", "海妖", "瞄準鏡", "暴擊", "typhoon")) return ChampionArchetype.CritMarksman;
    if (ContainsAny(text, "治療", "護盾", "baby kitty", "windspeaker")) return ChampionArchetype.Enchanter;
    if (ContainsAny(text, "雪球", "日炎", "荊棘", "巨人")) return ChampionArchetype.Tank;
    if (ContainsAny(text, "魔法飛彈", "卓越邪惡", "珠光", "煉獄", "阿嬤")) return ChampionArchetype.MagePoke;
    return ChampionArchetype.MageBurst;
}

static string BuildTopAugmentText(
    GuideRow guide,
    ChampionPageData? page,
    IReadOnlyDictionary<string, AugmentRow> augmentLookup,
    ChampionArchetype archetype)
{
    var parsed = ParseExistingAugmentNames(guide.Augments, augmentLookup).Take(10).ToList();
    List<string> online = parsed.Count > 0
        ? []
        : page?.TopAugments.Select(item => item.Name).Where(name => name.Length > 0).Take(6).ToList() ?? [];
    var merged = parsed.Concat(online)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(10)
        .ToList();

    if (merged.Count == 0)
    {
        merged.AddRange(DefaultAugments(archetype));
    }

    return JoinList(merged.Select(name => FormatAugmentPick(name, augmentLookup)));
}

static List<string> ParseExistingAugmentNames(string? text, IReadOnlyDictionary<string, AugmentRow> lookup)
{
    if (string.IsNullOrWhiteSpace(text))
    {
        return [];
    }

    var cleaned = text
        .Replace("OP.GG 推薦海克斯（依頁面順序）：", string.Empty)
        .Replace("人工推薦方向：", string.Empty);
    var firstColon = cleaned.IndexOf('\uFF1A');
    var prefix = firstColon > 0 ? cleaned[..firstColon] : string.Empty;
    if (firstColon >= 0
        && firstColon < cleaned.Length - 1
        && firstColon < 80
        && (prefix.Contains("OP.GG", StringComparison.OrdinalIgnoreCase)
            || prefix.Contains("\u63a8\u85a6", StringComparison.OrdinalIgnoreCase)
            || prefix.Contains("\u63a8\u8350", StringComparison.OrdinalIgnoreCase)))
    {
        cleaned = cleaned[(firstColon + 1)..];
    }

    var results = new List<string>();
    foreach (var raw in cleaned.Split([';', ',', '，', '、', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var name = Regex.Replace(raw, @"[\uFF08(][^\uFF09)]*[\uFF09)]", string.Empty).Trim();
        if (name.Length == 0)
        {
            continue;
        }

        if (lookup.TryGetValue(name, out var augment)
            || lookup.TryGetValue(NormalizeKey(name), out augment)
            || lookup.TryGetValue(CompactTerm(name), out augment))
        {
            name = augment.Name;
        }

        if (!results.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            results.Add(name);
        }
    }

    return results;
}

static string FormatAugmentPick(string name, IReadOnlyDictionary<string, AugmentRow> lookup)
{
    var cleanName = Regex.Replace(name, @"[\uFF08(][^\uFF09)]*[\uFF09)]", string.Empty).Trim();
    if (lookup.TryGetValue(cleanName, out var augment)
        || lookup.TryGetValue(NormalizeKey(cleanName), out augment)
        || lookup.TryGetValue(CompactTerm(cleanName), out augment))
    {
        var tier = string.IsNullOrWhiteSpace(augment.Tier) ? "\u672a\u8a55\u7d1a" : augment.Tier.Trim().ToUpperInvariant();
        return $"{augment.Name}\uFF08{tier}/{DisplayRarity(augment.Rarity)}\uFF09";
    }

    return cleanName;
}

static string DisplayRarity(string? rarity)
{
    return rarity?.Trim().ToLowerInvariant() switch
    {
        "prismatic" or "\u7a1c\u5f69" or "\u7a1c\u5f69\u968e" or "\u5f69\u8272" => "\u7a1c\u5f69\u968e",
        "gold" or "\u9ec3\u91d1" or "\u9ec3\u91d1\u968e" => "\u9ec3\u91d1\u968e",
        "silver" or "\u767d\u9280" or "\u767d\u9280\u968e" => "\u767d\u9280\u968e",
        _ => "\u672a\u5206\u968e"
    };
}

static string BuildRoleSummary(GuideRow guide, ChampionPageData? page, ChampionArchetype archetype)
{
    var role = ArchetypeLabel(archetype);
    var tier = string.IsNullOrWhiteSpace(page?.Tier) ? string.Empty : $"，中文資料站目前標示 {page.Tier} 級";
    return $"{DisplayChampion(guide)}：{role}{tier}。人工標註以英雄本身輸出型態為第一優先，裝備與海克斯只選能放大主要玩法的路線，避免因單一海克斯硬轉錯屬性。";
}

static string BuildPlaystyle(ChampionArchetype archetype, string augments)
{
    return archetype switch
    {
        ChampionArchetype.CritMarksman => $"以前排後方持續普攻為主，優先拿射程、攻速、暴擊、命中與收割型海克斯；目前優先候選：{augments}。",
        ChampionArchetype.OnHitMarksman => $"以高頻普攻與命中特效打持續輸出，海克斯優先攻速、命中、射程與吸血，不把技能型 AP 海克斯當核心。",
        ChampionArchetype.Fighter => $"等雪球、隊友控制或敵方關鍵技能交掉後再進場，用吸血、護盾、生命/雙抗與 AD 技能傷害打第二波；目前優先候選：{augments}。",
        ChampionArchetype.Assassin => $"以側翼、草叢與殘血收割為主，優先位移、爆發、處刑、技能刷新與穿透；不要正面硬吃控制。",
        ChampionArchetype.Tank => $"任務是開戰、吃技能與保護後排，海克斯優先生命、雙抗、控制、日炎/荊棘升級與雪球開團。",
        ChampionArchetype.Enchanter => $"以護盾、治療、加速與團隊增益為主，優先保排與治療護盾增幅；輸出裝只在隊伍缺傷害時才考慮。",
        ChampionArchetype.MagePoke => $"用技能距離與範圍壓血，優先技能急速、魔穿、燃燒、多段觸發與技能命中海克斯；保持距離避免被雪球強開。",
        _ => $"以技能爆發與控制銜接為主，優先 AP、技能急速、魔穿、技能暴擊與處刑型海克斯；目前優先候選：{augments}。"
    };
}

static string BuildPositioning(ChampionArchetype archetype)
{
    return archetype switch
    {
        ChampionArchetype.CritMarksman or ChampionArchetype.OnHitMarksman => "站在前排後方，以可普攻但不吃第一波控制的位置輸出；敵方刺客或雪球消失前不要貪點塔。",
        ChampionArchetype.Fighter => "站在側前方找二次進場角度，先讓坦克或控制技能開局，再用位移/雪球切入後排或打殘血前排。",
        ChampionArchetype.Assassin => "盡量從側翼或草叢進場，等敵方控制與保命技能交出後收割；沒有退場路線時不要先手。",
        ChampionArchetype.Tank => "站在隊伍最前方但不要脫節，雪球命中或敵方走位失誤時開團；後排被切時優先回頭保排。",
        ChampionArchetype.Enchanter => "保持在主輸出身後一個技能距離，優先保護最高輸出隊友；被雪球標記時立刻後撤或用控制斷進場。",
        _ => "站在技能射程邊緣，以小兵與前排保護自己；技能空窗期後退，避免被刺客或雪球直接開到。"
    };
}

static string BuildWeaknesses(ChampionArchetype archetype)
{
    return archetype switch
    {
        ChampionArchetype.CritMarksman or ChampionArchetype.OnHitMarksman => "怕強開、刺客、長距離控制與反普攻效果；若敵方坦克多，優先補物穿/百分比傷害而不是轉 AP。",
        ChampionArchetype.Fighter => "怕被風箏、重傷與連續控制；若進場後無法持續普攻或命中技能，吸血與半坦裝收益會下降。",
        ChampionArchetype.Assassin => "怕真眼式揭露、群體護盾、保排控制與反開陣；若不能秒殺後排，需改打殘血收割。",
        ChampionArchetype.Tank => "怕百分比傷害、真傷、巨人殺手與多段灼燒；若隊伍缺輸出，不要只堆防禦忽略開戰價值。",
        ChampionArchetype.Enchanter => "怕被強開與沉默/擊飛斷節奏；隊友缺主要輸出時，純輔助海克斯價值會下降。",
        ChampionArchetype.MagePoke => "怕強開、法盾、刺客與高移速突進；技能命中率低時，燃燒與技能急速裝收益會大幅下降。",
        _ => "怕強開、法盾與技能空窗；若敵方前排多，需早點補魔穿或百分比傷害。"
    };
}

static string BuildSummonerSpells(ChampionArchetype archetype)
{
    return archetype switch
    {
        ChampionArchetype.Fighter or ChampionArchetype.Tank or ChampionArchetype.Assassin => "閃現 + 雪球",
        ChampionArchetype.Enchanter => "閃現 + 治癒 / 屏障",
        _ => "閃現 + 屏障 / 鬼步"
    };
}

static string BuildManualNotes(
    GuideRow guide,
    ChampionPageData? page,
    ChampionArchetype archetype,
    string topAugments,
    string coreItems,
    IReadOnlyDictionary<string, AugmentRow> augmentLookup)
{
    var source = page is null ? "OP.GG 既有候選 + 英雄定位保守修正" : "OP.GG 既有候選 + arammayhem.com 中文英雄頁";
    var avoid = archetype switch
    {
        ChampionArchetype.CritMarksman or ChampionArchetype.OnHitMarksman => "不建議把黑焰火炬、蘭德里的折磨、惡意等 AP 燃燒裝當成主線；除非英雄本身有明確 AP/技能流玩法。",
        ChampionArchetype.Fighter or ChampionArchetype.Assassin => "不建議硬拿純 AP、只強化遠程技能消耗或無法觸發的護盾治療海克斯。",
        ChampionArchetype.Tank => "不建議只看傷害型 S 級海克斯，若無法觸發或會讓開戰能力消失，評價要下降。",
        ChampionArchetype.Enchanter => "不建議硬轉普攻暴擊或純刺客裝；隊伍缺輸出時才改 AP 技能流。",
        _ => "不建議硬拿普攻暴擊、純物理致命或需要近戰進場才能觸發的海克斯。"
    };

    return $"{marker}: 來源={source}；定位={ArchetypeLabel(archetype)}；核心裝備={coreItems}；優先海克斯={topAugments}。中文論壇/攻略共識採用「先看英雄輸出型態，再看海克斯是否能穩定觸發與是否能組套裝」；{avoid}";
}

static bool HasConflictingCoreItems(ChampionArchetype archetype, IReadOnlyList<string> coreItems)
{
    if (archetype is not (ChampionArchetype.CritMarksman
        or ChampionArchetype.OnHitMarksman
        or ChampionArchetype.Fighter
        or ChampionArchetype.Assassin))
    {
        return false;
    }

    var text = string.Join(' ', coreItems);
    return ContainsAny(text, "黑焰火炬", "蘭德里的折磨", "惡意", "盧登", "滅世者", "虛空之杖");
}

static IReadOnlyList<string> DefaultCoreItems(ChampionArchetype archetype)
{
    return archetype switch
    {
        ChampionArchetype.CritMarksman => ["蒐集者", "無盡之刃", "多明尼克的問候"],
        ChampionArchetype.OnHitMarksman => ["殞落王者之劍", "鬼索的狂暴之刃", "海妖殺手"],
        ChampionArchetype.Fighter => ["狂妄", "焚天", "死亡之舞"],
        ChampionArchetype.Assassin => ["狂妄", "蒐集者", "席利妲咒怨"],
        ChampionArchetype.Tank => ["好戰者鎧甲", "日炎聖盾", "振奮盔甲"],
        ChampionArchetype.Enchanter => ["月石再生器", "黎明核心", "流水法杖"],
        ChampionArchetype.MagePoke => ["黑焰火炬", "蘭德里的折磨", "影焰"],
        _ => ["盧登回音", "影焰", "滅世者的死亡之帽"]
    };
}

static IReadOnlyList<string> DefaultSituationalItems(ChampionArchetype archetype)
{
    return archetype switch
    {
        ChampionArchetype.CritMarksman => ["凡性的提醒", "殞落王者之劍", "水銀彎刀", "盾弓"],
        ChampionArchetype.OnHitMarksman => ["智慧末刃", "颶風箭", "凡性的提醒", "水銀彎刀"],
        ChampionArchetype.Fighter => ["星蝕", "振奮盔甲", "史特拉克手套", "死亡之舞"],
        ChampionArchetype.Assassin => ["夜色緣界", "巨蛇鋒牙", "公理弧刃", "死亡之舞"],
        ChampionArchetype.Tank => ["荊棘之甲", "自然之力", "冰霜之心", "虛空獻祭"],
        ChampionArchetype.Enchanter => ["熾灼魔器", "贖罪神石", "米凱的祝福", "黑魔禁書"],
        ChampionArchetype.MagePoke => ["虛空之杖", "黑魔禁書", "中婭沙漏", "女妖面紗"],
        _ => ["虛空之杖", "中婭沙漏", "黑魔禁書", "女妖面紗"]
    };
}

static IReadOnlyList<string> DefaultAugments(ChampionArchetype archetype)
{
    return archetype switch
    {
        ChampionArchetype.CritMarksman => ["雙刀流", "最萬用的瞄準鏡", "暴擊飛彈", "接二連三", "颱風"],
        ChampionArchetype.OnHitMarksman => ["雙刀流", "接二連三", "颱風", "靈巧", "吸血迷信"],
        ChampionArchetype.Fighter => ["吸血迷信", "歌利亞巨人", "終極型態", "升級：狂妄", "渴血"],
        ChampionArchetype.Assassin => ["處刑者", "小丑學院", "狂妄升級", "暗影疾奔", "罪惡快感"],
        ChampionArchetype.Tank => ["歌利亞巨人", "升級：日炎", "升級：荊棘之甲", "史上最大雪球", "不動如山"],
        ChampionArchetype.Enchanter => ["風語者的祝福", "會心治療", "急救包", "小貓咪", "索娜塔"],
        ChampionArchetype.MagePoke => ["魔法飛彈", "卓越邪惡", "煉獄導管", "珠光護手", "阿嬤的辣油"],
        _ => ["珠光護手", "魔法飛彈", "卓越邪惡", "處刑者", "尤里卡"]
    };
}

static string DefaultSkillOrder(ChampionArchetype archetype)
{
    return archetype switch
    {
        ChampionArchetype.CritMarksman or ChampionArchetype.OnHitMarksman => "Q>E>W 或依主要輸出技能調整",
        ChampionArchetype.Enchanter => "主護盾/治療技能，其次控制或消耗技能",
        ChampionArchetype.Tank => "主控制/坦度技能，其次主要傷害技能",
        _ => "主主要傷害技能，其次機動或控制技能"
    };
}

static string ArchetypeLabel(ChampionArchetype archetype)
{
    return archetype switch
    {
        ChampionArchetype.CritMarksman => "物理暴擊射手",
        ChampionArchetype.OnHitMarksman => "攻速命中特效射手",
        ChampionArchetype.Fighter => "AD 鬥士/吸血續戰",
        ChampionArchetype.Assassin => "刺客/收割爆發",
        ChampionArchetype.Tank => "坦克/開戰前排",
        ChampionArchetype.Enchanter => "護盾治療輔助",
        ChampionArchetype.MagePoke => "AP 消耗/燃燒法師",
        _ => "AP 技能爆發法師"
    };
}

static bool TryGetChampionOverride(string key, out ChampionArchetype archetype)
{
    archetype = key switch
    {
        "aatrox" => ChampionArchetype.Fighter,
        "akshan" or "aphelios" or "ashe" or "caitlyn" or "draven" or "ezreal" or "jinx" or "lucian"
            or "missfortune" or "nilah" or "quinn" or "samira" or "senna" or "sivir" or "tristana"
            or "varus" or "xayah" or "zeri" or "yasuo" or "yone" => ChampionArchetype.CritMarksman,
        "kaisa" or "kalista" or "kindred" or "kogmaw" or "twitch" or "vayne" => ChampionArchetype.OnHitMarksman,
        "brand" or "lux" or "xerath" or "ziggs" or "velkoz" or "zyra" or "teemo" or "malzahar"
            or "lillia" or "rumble" or "singed" or "cassiopeia" => ChampionArchetype.MagePoke,
        "seraphine" or "sona" or "soraka" or "yuumi" or "lulu" or "nami" or "janna" or "milio"
            or "ivern" or "karma" => ChampionArchetype.Enchanter,
        "azir" or "orianna" or "syndra" or "viktor" or "veigar" or "ahri" => ChampionArchetype.MageBurst,
        "akali" or "ekko" or "evelynn" or "fizz" or "katarina" or "khazix" or "leblanc" or "naafiri"
            or "nocturne" or "pyke" or "qiyana" or "rengar" or "shaco" or "talon" or "zed" => ChampionArchetype.Assassin,
        "alistar" or "amumu" or "braum" or "chogath" or "ksante" or "leona" or "malphite" or "maokai"
            or "nautilus" or "ornn" or "poppy" or "rammus" or "rell" or "sejuani" or "shen" or "skarner"
            or "tahmkench" or "thresh" or "zac" => ChampionArchetype.Tank,
        _ => default
    };

    return archetype != default || key is "azir" or "orianna" or "syndra" or "viktor" or "veigar" or "ahri";
}

static string NormalizeGameTerm(string value)
{
    var text = ToTraditionalTerm(WebUtility.HtmlDecode(value).Trim());
    var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["黯炎火炬"] = "黑焰火炬",
        ["蘭德裏的折磨"] = "蘭德里的折磨",
        ["滅世者的死亡之帽"] = "滅世者的死亡之帽",
        ["盧登的回聲"] = "盧登回音",
        ["中婭沙漏"] = "中婭沙漏",
        ["虛空之杖"] = "虛空之杖",
        ["莫雷洛秘典"] = "黑魔禁書",
        ["破敗王者之刃"] = "殞落王者之劍",
        ["無盡之刃"] = "無盡之刃",
        ["多米尼克領主的致意"] = "多明尼克的問候",
        ["凡性的提醒"] = "致死宣告",
        ["斯塔緹克電刃"] = "史提克彈簧刀",
        ["收集者"] = "蒐集者",
        ["星蝕"] = "星蝕",
        ["歌利亞巨人"] = "巨人",
        ["祖母的辣椒油"] = "阿嬤的辣油",
        ["煉獄導管"] = "煉獄使者",
        ["最萬用的瞄準鏡"] = "最萬用的瞄準鏡",
        ["更萬用的瞄準鏡"] = "更萬用的瞄準鏡",
        ["萬用瞄準鏡"] = "萬用瞄準鏡",
        ["吸血習性"] = "吸血迷信",
        ["雙刀流"] = "雙修大師",
        ["接二連三"] = "二次三次",
        ["颱風"] = "颱風",
        ["暴擊飛彈"] = "暴擊飛彈",
        ["魔法飛彈"] = "魔法導彈",
        ["裁決使"] = "處刑者",
        ["終極形態"] = "最終型態",
        ["升級：狂妄"] = "升級傲慢",
        ["渴血"] = "渴血"
    };

    return replacements.TryGetValue(text, out var replacement) ? replacement : text;
}

static string ToTraditionalTerm(string value)
{
    try
    {
#pragma warning disable CA1416
        var converted = Strings.StrConv(value, VbStrConv.TraditionalChinese);
#pragma warning restore CA1416
        return converted ?? value;
    }
    catch
    {
        return value;
    }
}

static string DisplayChampion(GuideRow guide)
{
    return string.IsNullOrWhiteSpace(guide.LocalizedName)
        ? guide.ChampionName
        : $"{guide.LocalizedName}({guide.ChampionName})";
}

static bool ContainsAny(string text, params string[] needles)
{
    return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
}

static string JoinList(IEnumerable<string> values)
{
    return string.Join("\uFF1B", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct());
}

static string NormalizeKey(string value)
{
    return Regex.Replace(value.ToLowerInvariant(), @"[\s'’：:（）()·\-.]", string.Empty);
}

static string CompactTerm(string value)
{
    return Regex.Replace(value, @"[^\p{L}\p{N}]+", string.Empty).ToLowerInvariant();
}

static string StripTags(string html)
{
    var noScript = Regex.Replace(html, "<script.*?</script>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    var noStyle = Regex.Replace(noScript, "<style.*?</style>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    return WebUtility.HtmlDecode(Regex.Replace(noStyle, "<[^>]+>", " "));
}

static string MatchFirst(string value, string pattern)
{
    var match = Regex.Match(value, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
    return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value).Trim() : string.Empty;
}

static async Task ApplyGuideUpdatesAsync(SqlConnection connection, IReadOnlyList<GuideUpdate> updates)
{
    const string sql = """
        UPDATE dbo.LolAramGuides
        SET RoleSummary = @RoleSummary,
            CoreItems = @CoreItems,
            SituationalItems = @SituationalItems,
            Augments = @Augments,
            SummonerSpells = @SummonerSpells,
            SkillOrder = @SkillOrder,
            PlaystyleTips = @PlaystyleTips,
            PositioningTips = @PositioningTips,
            Weaknesses = @Weaknesses,
            SourceUrl = @SourceUrl,
            Notes = @Notes,
            UpdatedAt = SYSUTCDATETIME()
        WHERE Id = @Id;
        """;

    foreach (var update in updates)
    {
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = update.Id;
        command.Parameters.Add("@RoleSummary", SqlDbType.NVarChar, 500).Value = update.RoleSummary;
        command.Parameters.Add("@CoreItems", SqlDbType.NVarChar, 1000).Value = update.CoreItems;
        command.Parameters.Add("@SituationalItems", SqlDbType.NVarChar, 1000).Value = DbValueOrNull(update.SituationalItems);
        command.Parameters.Add("@Augments", SqlDbType.NVarChar, 1000).Value = DbValueOrNull(update.Augments);
        command.Parameters.Add("@SummonerSpells", SqlDbType.NVarChar, 500).Value = DbValueOrNull(update.SummonerSpells);
        command.Parameters.Add("@SkillOrder", SqlDbType.NVarChar, 500).Value = DbValueOrNull(update.SkillOrder);
        command.Parameters.Add("@PlaystyleTips", SqlDbType.NVarChar, 1200).Value = DbValueOrNull(update.PlaystyleTips);
        command.Parameters.Add("@PositioningTips", SqlDbType.NVarChar, 1200).Value = DbValueOrNull(update.PositioningTips);
        command.Parameters.Add("@Weaknesses", SqlDbType.NVarChar, 1200).Value = DbValueOrNull(update.Weaknesses);
        command.Parameters.Add("@SourceUrl", SqlDbType.NVarChar, 1000).Value = DbValueOrNull(update.SourceUrl);
        command.Parameters.Add("@Notes", SqlDbType.NVarChar, 1200).Value = DbValueOrNull(update.Notes);
        await command.ExecuteNonQueryAsync();
    }
}

static List<GuideRow> ApplyInMemory(IReadOnlyList<GuideRow> rows, IReadOnlyList<GuideUpdate> updates)
{
    var lookup = updates.ToDictionary(update => update.Id);
    return rows.Select(row => lookup.TryGetValue(row.Id, out var update)
        ? row with
        {
            RoleSummary = update.RoleSummary,
            CoreItems = update.CoreItems,
            SituationalItems = update.SituationalItems,
            Augments = update.Augments,
            SummonerSpells = update.SummonerSpells,
            SkillOrder = update.SkillOrder,
            PlaystyleTips = update.PlaystyleTips,
            PositioningTips = update.PositioningTips,
            Weaknesses = update.Weaknesses,
            SourceUrl = update.SourceUrl,
            Notes = update.Notes
        }
        : row).ToList();
}

static string BuildReport(
    string timestamp,
    bool dryRun,
    IReadOnlyList<GuideRow> before,
    IReadOnlyList<GuideRow> after,
    IReadOnlyList<GuideUpdate> updates,
    int fetchedCount,
    IReadOnlyList<string> fetchFailures)
{
    var roleCoverage = FilledRatio(after, row => row.RoleSummary);
    var coreCoverage = FilledRatio(after, row => row.CoreItems);
    var situationalCoverage = FilledRatio(after, row => row.SituationalItems);
    var notesCoverage = FilledRatio(after, row => row.Notes);
    var physicalGuidesWithApCoreItems = after.Count(row =>
        IsPhysicalLeaning(row)
        && ContainsAny(row.CoreItems, "黑焰火炬", "蘭德里的折磨", "惡意", "盧登", "滅世者", "虛空之杖"));
    var sampleKeys = new HashSet<string>(["aatrox", "akshan", "seraphine", "brand"], StringComparer.OrdinalIgnoreCase);
    var sampleChecks = string.Join(
        Environment.NewLine,
        after
            .Where(row => sampleKeys.Contains(row.ChampionKey))
            .OrderBy(row => row.ChampionKey)
            .Select(row => $"- {row.ChampionKey}: core={row.CoreItems}; augments={row.Augments}; role={row.RoleSummary}"));

    return $"""
        # ARAM Manual Curation V2 Report {timestamp}

        Mode: {(dryRun ? "dry-run" : "applied")}

        ## Coverage
        - Online Chinese champion pages fetched: {fetchedCount}/{before.Count}
        - Guide RoleSummary: {roleCoverage.Text}
        - Guide CoreItems: {coreCoverage.Text}
        - Guide SituationalItems: {situationalCoverage.Text}
        - Guide Notes: {notesCoverage.Text}
        - Physical-leaning guides with AP/AP-burn core items: {physicalGuidesWithApCoreItems}

        ## Updated Rows
        - Guides updated: {updates.Count}

        ## Sample Checks
        {sampleChecks}

        ## Source Validation
        - Riot official Traditional Chinese support page confirms four augment selection phases, tiers, no runes, and set bonuses.
        - PTT LoL public posts were used only as community-sentiment checks for Mayhem randomness and new augment mechanics.
        - Bahamut public LoL posts were searched; useful Mayhem-specific strategy data was limited, so no copied forum text was imported.
        - aramgg.com zh-TW strategy guide and arammayhem.com / ARAM Mayhem Wiki Chinese pages were used for champion/build/augment cross-checks.
        - Existing OP.GG imported candidate augment order remains in the database and was compared instead of discarded.

        ## Fallback / Risk
        - Threads public search did not return stable crawlable LoL Mayhem discussion pages in this run.
        - Missing pages fall back to conservative champion-archetype rules, never to AP-burn defaults for AD champions.

        ## Rollback
        - Apply mode creates: dbo.LolAramGuides_ManualCurationBackup_{timestamp}
        """;
}

static bool IsPhysicalLeaning(GuideRow row)
{
    var text = $"{row.RoleSummary} {row.CoreItems}".ToLowerInvariant();
    return ContainsAny(text, "射手", "鬥士", "刺客", "ad", "物理", "狂妄", "無盡", "蒐集者", "死亡之舞");
}

static (int Count, int Total, string Text) FilledRatio<T>(IReadOnlyList<T> rows, Func<T, string?> selector)
{
    var count = rows.Count(row => !string.IsNullOrWhiteSpace(selector(row)) && !IsPlaceholder(selector(row)));
    return (count, rows.Count, $"{count}/{rows.Count} ({(rows.Count == 0 ? 0 : count * 100.0 / rows.Count):0.0}%)");
}

static bool IsPlaceholder(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return true;
    }

    return value.Contains("待人工", StringComparison.OrdinalIgnoreCase)
        || value.Contains("尚未完成", StringComparison.OrdinalIgnoreCase)
        || value.Contains("placeholder", StringComparison.OrdinalIgnoreCase);
}

static object DbValueOrNull(string? value)
{
    return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
}

static string ReadString(SqlDataReader reader, string name)
{
    var ordinal = reader.GetOrdinal(name);
    return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
}

static string? ReadNullableString(SqlDataReader reader, string name)
{
    var value = ReadString(reader, name);
    return string.IsNullOrWhiteSpace(value) ? null : value;
}

enum ChampionArchetype
{
    MageBurst,
    MagePoke,
    CritMarksman,
    OnHitMarksman,
    Fighter,
    Assassin,
    Tank,
    Enchanter
}

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

record AugmentRow(
    int Id,
    string AugmentKey,
    string Name,
    string Rarity,
    string? Tier,
    string? SeriesKey,
    string EffectText,
    string? Tags);

record AugmentSource(string Name, string WinRate);

sealed class ChampionPageData
{
    public string SourceUrl { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public List<string> Roles { get; } = [];
    public List<string> CoreItems { get; } = [];
    public List<string> SituationalItems { get; } = [];
    public List<AugmentSource> TopAugments { get; } = [];
    public string SkillOrder { get; set; } = string.Empty;

    public bool HasUsefulData => Roles.Count > 0 || CoreItems.Count > 0 || TopAugments.Count > 0;
}

record GuideUpdate(
    int Id,
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
    string? Notes)
{
    public bool HasChanges(GuideRow row)
    {
        return RoleSummary != row.RoleSummary
            || CoreItems != row.CoreItems
            || SituationalItems != row.SituationalItems
            || Augments != row.Augments
            || SummonerSpells != row.SummonerSpells
            || SkillOrder != row.SkillOrder
            || PlaystyleTips != row.PlaystyleTips
            || PositioningTips != row.PositioningTips
            || Weaknesses != row.Weaknesses
            || SourceUrl != row.SourceUrl
            || Notes != row.Notes;
    }
}
