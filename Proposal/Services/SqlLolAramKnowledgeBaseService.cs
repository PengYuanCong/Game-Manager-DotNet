using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Proposal.Models;

namespace Proposal.Services
{
    public class SqlLolAramKnowledgeBaseService : IAiKnowledgeBaseService
    {
        private const int MaxSearchInputLength = 1000;
        private const int MaxChampionPromptLength = 6000;
        private const int MaxAugmentPromptLength = 9000;
        private const int MaxKnowledgePromptLength = 12000;
        private const int MaxDetectedTagTextLength = 14000;

        private readonly ISqlConnectionFactory _connectionFactory;
        private readonly ILogger<SqlLolAramKnowledgeBaseService> _logger;

        public SqlLolAramKnowledgeBaseService(
            ISqlConnectionFactory connectionFactory,
            ILogger<SqlLolAramKnowledgeBaseService> logger)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        public async Task<AiKnowledgeContext> GetContextAsync(
            AiRecommendationInput input,
            CancellationToken cancellationToken = default)
        {
            var championKey = ResolveChampionKey(input.CoreChampion);

            await using var connection = _connectionFactory.Create();
            try
            {
                await connection.OpenAsync(cancellationToken);
                await LolAramGuideSchema.EnsureTableAsync(connection, cancellationToken);
                await LolAramGuideSchema.SeedBrandGuideAsync(connection, cancellationToken);
                await LolAramAugmentSchema.EnsureTablesAsync(connection, cancellationToken);
                await LolAramAugmentSchema.SeedStarterDataAsync(connection, cancellationToken);
                await LolAramItemSchema.EnsureTableAsync(connection, cancellationToken);
                await LolAramItemSchema.SeedStarterDataAsync(connection, cancellationToken);
            }
            catch (SqlException ex)
            {
                _logger.LogWarning(ex, "ARAM knowledge database is unavailable. Recommendation will continue without local RAG.");
                return CreateEmptyContext();
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "ARAM knowledge database could not be prepared. Recommendation will continue without local RAG.");
                return CreateEmptyContext();
            }

            var championGuide = string.IsNullOrWhiteSpace(championKey)
                ? null
                : await GetChampionGuideAsync(connection, championKey, cancellationToken);

            var augmentKnowledge = await GetAugmentKnowledgeAsync(
                connection,
                input,
                championGuide,
                cancellationToken);

            if (championGuide == null && !augmentKnowledge.HasData)
            {
                return CreateEmptyContext();
            }

            var prompt = new StringBuilder();
            var cacheScopes = new List<string>();
            var sourceLabels = new List<string>();
            string? sourceUrl = null;

            if (championGuide != null)
            {
                prompt.AppendLine(championGuide.PromptContext);
                cacheScopes.Add(championGuide.CacheScope);
                sourceLabels.Add(championGuide.SourceLabel);
                sourceUrl = championGuide.SourceUrl;
            }
            else
            {
                prompt.AppendLine("本機尚未找到這位英雄的人工攻略。");
            }

            if (augmentKnowledge.HasData)
            {
                prompt.AppendLine();
                prompt.AppendLine(augmentKnowledge.PromptContext);
                cacheScopes.Add(augmentKnowledge.CacheScope);
                sourceLabels.Add(augmentKnowledge.SourceLabel);
                sourceUrl ??= augmentKnowledge.SourceUrl;
            }

            return new AiKnowledgeContext
            {
                HasGuide = true,
                CacheScope = string.Join("|", cacheScopes.DefaultIfEmpty("no-guide")),
                SourceUrl = sourceUrl,
                SourceLabel = string.Join(" + ", sourceLabels.DefaultIfEmpty("本機知識")),
                PromptContext = TrimTo(prompt.ToString(), MaxKnowledgePromptLength)
            };
        }

        private async Task<ChampionKnowledge?> GetChampionGuideAsync(
            SqlConnection connection,
            string championKey,
            CancellationToken cancellationToken)
        {
            const string selectSql = @"
                SELECT TOP 1
                    ChampionKey,
                    ChampionName,
                    LocalizedName,
                    ModeName,
                    PatchVersion,
                    RoleSummary,
                    CoreItems,
                    SituationalItems,
                    Augments,
                    SummonerSpells,
                    SkillOrder,
                    PlaystyleTips,
                    PositioningTips,
                    Weaknesses,
                    SourceUrl,
                    Notes,
                    UpdatedAt
                FROM LolAramGuides
                WHERE ChampionKey = @ChampionKey
                ORDER BY UpdatedAt DESC";

            await using var command = new SqlCommand(selectSql, connection);
            command.Parameters.Add("@ChampionKey", SqlDbType.NVarChar, 100).Value = championKey;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var updatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"));
            var sourceUrl = ReadString(reader, "SourceUrl");
            var championName = ReadString(reader, "ChampionName");
            var localizedName = ReadString(reader, "LocalizedName");
            var patchVersion = ReadString(reader, "PatchVersion");

            var promptContext = $"""
                Local champion guide:
                Champion: {championName} ({localizedName})
                Mode: {ReadString(reader, "ModeName")}
                Patch / reference version: {patchVersion}
                Role summary: {ReadString(reader, "RoleSummary")}
                Core items: {ReadString(reader, "CoreItems")}
                Situational items: {ReadString(reader, "SituationalItems")}
                Augment direction: {ReadString(reader, "Augments")}
                Summoner spells: {ReadString(reader, "SummonerSpells")}
                Skill order: {ReadString(reader, "SkillOrder")}
                Playstyle tips: {ReadString(reader, "PlaystyleTips")}
                Positioning tips: {ReadString(reader, "PositioningTips")}
                Weaknesses and cautions: {ReadString(reader, "Weaknesses")}
                Curator notes: {ReadString(reader, "Notes")}
                Source reference: {sourceUrl}

                Use this champion guide as the source of truth for champion-specific advice.
                """;

            return new ChampionKnowledge
            {
                ChampionKey = championKey,
                ChampionName = championName,
                PatchVersion = patchVersion,
                CacheScope = $"champion:{championKey}:{patchVersion}:{updatedAt:yyyyMMddHHmmss}",
                SourceUrl = sourceUrl,
                SourceLabel = $"{localizedName} ARAM 知識 {patchVersion}",
                PromptContext = TrimTo(promptContext, MaxChampionPromptLength)
            };
        }

        private async Task<AugmentKnowledge> GetAugmentKnowledgeAsync(
            SqlConnection connection,
            AiRecommendationInput input,
            ChampionKnowledge? championGuide,
            CancellationToken cancellationToken)
        {
            var matchedAugments = await FindInputAugmentsAsync(connection, input.Augment, cancellationToken);
            var matchedItems = await FindInputItemsAsync(connection, input.AvailableItems, cancellationToken);
            var userTags = DetectUserTags(input, championGuide);
            var augmentTags = matchedAugments
                .SelectMany(augment => SplitTags(augment.Tags))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var tag in matchedItems.SelectMany(item => SplitTags(item.Tags)))
            {
                augmentTags.Add(tag);
            }

            foreach (var tag in userTags)
            {
                augmentTags.Add(tag);
            }

            var series = await FindSeriesAsync(
                connection,
                matchedAugments.Select(augment => augment.SeriesKey),
                cancellationToken);

            var rules = await FindSynergyRulesAsync(
                connection,
                matchedAugments,
                series,
                augmentTags,
                cancellationToken);

            if (matchedAugments.Count == 0 && matchedItems.Count == 0 && series.Count == 0 && rules.Count == 0)
            {
                return AugmentKnowledge.Empty;
            }

            var prompt = new StringBuilder();
            prompt.AppendLine("Local augment knowledge:");

            if (matchedAugments.Count == 0)
            {
                prompt.AppendLine("- 使用者輸入的海克斯目前沒有命中本機資料。");
            }
            else
            {
                foreach (var augment in matchedAugments)
                {
                    prompt.AppendLine($"""
                        Augment: {augment.Name}
                        Key: {augment.AugmentKey}
                        Rarity: {augment.Rarity}
                        Tier: {augment.Tier}
                        Series key: {TextOrNone(augment.SeriesKey)}
                        Tags: {TextOrNone(augment.Tags)}
                        Effect: {augment.EffectText}
                        Synergy notes: {TextOrNone(augment.SynergyNotes)}
                        Curator notes: {TextOrNone(augment.Notes)}
                        """);
                }
            }

            if (matchedItems.Count > 0)
            {
                prompt.AppendLine("Matched item knowledge:");
                foreach (var item in matchedItems)
                {
                    prompt.AppendLine($"""
                        Item: {item.Name}
                        Key: {item.ItemKey}
                        Aliases: {TextOrNone(item.Aliases)}
                        Tags: {TextOrNone(item.Tags)}
                        Effect: {item.EffectText}
                        Synergy notes: {TextOrNone(item.SynergyNotes)}
                        Curator notes: {TextOrNone(item.Notes)}
                        """);
                }
            }

            if (series.Count > 0)
            {
                prompt.AppendLine("Matched augment series:");
                foreach (var item in series)
                {
                    prompt.AppendLine($"""
                        Series: {item.SeriesName}
                        Key: {item.SeriesKey}
                        Tags: {TextOrNone(item.Tags)}
                        Description: {TextOrNone(item.Description)}
                        Set bonus / combo logic: {TextOrNone(item.SetBonusText)}
                        Notes: {TextOrNone(item.Notes)}
                        """);
                }
            }

            if (rules.Count > 0)
            {
                prompt.AppendLine("Matched synergy rules:");
                foreach (var rule in rules)
                {
                    prompt.AppendLine($"""
                        Rule: {rule.RuleName}
                        Priority: {rule.Priority}
                        Trigger tags: {TextOrNone(rule.TriggerTags)}
                        Condition: {rule.ConditionText}
                        Recommendation logic: {rule.RecommendationText}
                        Notes: {TextOrNone(rule.Notes)}
                        """);
                }
            }

            prompt.AppendLine("""
                When judging augments, combine direct series synergy and tag synergy.
                Series synergy means augments share the same SeriesKey.
                Tag synergy means effect words match across champion, item, and augment knowledge, such as burn items improving an augment that scales with burn triggers.
                Do not force a series if SeriesKey is empty; explain it as text/effect synergy instead.
                """);

            var newestTimestamp = matchedAugments
                .Select(item => item.UpdatedAt)
                .Concat(matchedItems.Select(item => item.UpdatedAt))
                .Concat(series.Select(item => item.UpdatedAt))
                .Concat(rules.Select(item => item.UpdatedAt))
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();

            var keys = matchedAugments.Count == 0
                ? "no-augment"
                : string.Join(",", matchedAugments.Select(item => item.AugmentKey));
            var itemKeys = matchedItems.Count == 0
                ? "no-items"
                : string.Join(",", matchedItems.Select(item => item.ItemKey));

            return new AugmentKnowledge
            {
                HasData = true,
                CacheScope = $"augment:{keys}:items:{itemKeys}:{newestTimestamp:yyyyMMddHHmmss}",
                SourceLabel = matchedAugments.Count == 0
                    ? $"海克斯搭配規則 + {matchedItems.Count} 筆裝備知識"
                    : $"{matchedAugments.Count} 筆海克斯知識 + {matchedItems.Count} 筆裝備知識",
                SourceUrl = matchedAugments.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.SourceUrl))?.SourceUrl,
                PromptContext = TrimTo(prompt.ToString(), MaxAugmentPromptLength)
            };
        }

        private async Task<List<LolAramAugment>> FindInputAugmentsAsync(
            SqlConnection connection,
            string? augmentInput,
            CancellationToken cancellationToken)
        {
            var augments = await ReadAllAugmentsAsync(connection, cancellationToken);
            if (string.IsNullOrWhiteSpace(augmentInput))
            {
                return new List<LolAramAugment>();
            }

            var normalizedInput = NormalizeSearchText(TrimTo(augmentInput, MaxSearchInputLength));
            return augments
                .Where(augment =>
                    normalizedInput.Contains(NormalizeSearchText(augment.Name), StringComparison.OrdinalIgnoreCase)
                    || normalizedInput.Contains(NormalizeSearchText(augment.AugmentKey), StringComparison.OrdinalIgnoreCase)
                    || NormalizeSearchText(augment.Name).Contains(normalizedInput, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList();
        }

        private async Task<List<LolAramItem>> FindInputItemsAsync(
            SqlConnection connection,
            string? itemInput,
            CancellationToken cancellationToken)
        {
            var items = await ReadAllItemsAsync(connection, cancellationToken);
            if (string.IsNullOrWhiteSpace(itemInput))
            {
                return new List<LolAramItem>();
            }

            var normalizedInput = NormalizeSearchText(TrimTo(itemInput, MaxSearchInputLength));
            return items
                .Where(item =>
                    normalizedInput.Contains(NormalizeSearchText(item.Name), StringComparison.OrdinalIgnoreCase)
                    || normalizedInput.Contains(NormalizeSearchText(item.ItemKey), StringComparison.OrdinalIgnoreCase)
                    || SplitAliases(item.Aliases).Any(alias => normalizedInput.Contains(NormalizeSearchText(alias), StringComparison.OrdinalIgnoreCase))
                    || NormalizeSearchText(item.Name).Contains(normalizedInput, StringComparison.OrdinalIgnoreCase))
                .Take(8)
                .ToList();
        }

        private static async Task<List<LolAramAugment>> ReadAllAugmentsAsync(
            SqlConnection connection,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT TOP 200
                    Id,
                    AugmentKey,
                    Name,
                    ModeName,
                    Rarity,
                    Tier,
                    SeriesKey,
                    EffectText,
                    Tags,
                    SynergyNotes,
                    PatchVersion,
                    SourceUrl,
                    Notes,
                    CreatedAt,
                    UpdatedAt
                FROM LolAramAugments
                ORDER BY UpdatedAt DESC";

            var results = new List<LolAramAugment>();
            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(ReadAugment(reader));
            }

            return results;
        }

        private static async Task<List<LolAramItem>> ReadAllItemsAsync(
            SqlConnection connection,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT TOP 200
                    Id,
                    ItemKey,
                    Name,
                    Aliases,
                    ModeName,
                    EffectText,
                    Tags,
                    SynergyNotes,
                    PatchVersion,
                    SourceUrl,
                    Notes,
                    CreatedAt,
                    UpdatedAt
                FROM LolAramItems
                ORDER BY UpdatedAt DESC";

            var results = new List<LolAramItem>();
            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(ReadItem(reader));
            }

            return results;
        }

        private static async Task<List<LolAramAugmentSeries>> FindSeriesAsync(
            SqlConnection connection,
            IEnumerable<string?> seriesKeys,
            CancellationToken cancellationToken)
        {
            var keys = seriesKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (keys.Count == 0)
            {
                return new List<LolAramAugmentSeries>();
            }

            var sql = $@"
                SELECT
                    Id,
                    SeriesKey,
                    SeriesName,
                    Description,
                    SetBonusText,
                    Tags,
                    PatchVersion,
                    SourceUrl,
                    Notes,
                    CreatedAt,
                    UpdatedAt
                FROM LolAramAugmentSeries
                WHERE SeriesKey IN ({string.Join(", ", keys.Select((_, index) => $"@Key{index}"))})";

            var results = new List<LolAramAugmentSeries>();
            await using var command = new SqlCommand(sql, connection);
            for (var i = 0; i < keys.Count; i++)
            {
                command.Parameters.Add($"@Key{i}", SqlDbType.NVarChar, 100).Value = keys[i];
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(ReadSeries(reader));
            }

            return results;
        }

        private static async Task<List<LolAramSynergyRule>> FindSynergyRulesAsync(
            SqlConnection connection,
            IReadOnlyList<LolAramAugment> augments,
            IReadOnlyList<LolAramAugmentSeries> series,
            IReadOnlySet<string> tags,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT TOP 100
                    Id,
                    RuleName,
                    BoostAugmentKey,
                    SeriesKey,
                    TriggerTags,
                    ChampionTags,
                    ItemTags,
                    ConditionText,
                    RecommendationText,
                    Priority,
                    PatchVersion,
                    Notes,
                    CreatedAt,
                    UpdatedAt
                FROM LolAramSynergyRules
                ORDER BY CASE Priority WHEN N'high' THEN 1 WHEN N'medium' THEN 2 ELSE 3 END, UpdatedAt DESC";

            var augmentKeys = augments
                .Select(augment => augment.AugmentKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var seriesKeys = series
                .Select(item => item.SeriesKey)
                .Concat(augments.Select(augment => augment.SeriesKey ?? string.Empty))
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var results = new List<LolAramSynergyRule>();
            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var rule = ReadRule(reader);
                var isMatch =
                    (!string.IsNullOrWhiteSpace(rule.BoostAugmentKey) && augmentKeys.Contains(rule.BoostAugmentKey))
                    || (!string.IsNullOrWhiteSpace(rule.SeriesKey) && seriesKeys.Contains(rule.SeriesKey))
                    || SplitTags(rule.TriggerTags).Any(tags.Contains)
                    || SplitTags(rule.ChampionTags).Any(tags.Contains)
                    || SplitTags(rule.ItemTags).Any(tags.Contains);

                if (isMatch)
                {
                    results.Add(rule);
                }
            }

            return results.Take(8).ToList();
        }

        private static HashSet<string> DetectUserTags(AiRecommendationInput input, ChampionKnowledge? championGuide)
        {
            var combinedText = TrimTo(string.Join(
                " ",
                input.CoreChampion,
                input.AvailableItems,
                input.Notes,
                input.Augment,
                championGuide?.ChampionName,
                championGuide?.PromptContext), MaxDetectedTagTextLength).ToLowerInvariant();

            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddTagWhenContains(tags, combinedText, "burn", "燃燒", "灼燒", "brand", "布蘭德", "blackfire", "黑焰", "liandry", "蘭德里", "面具", "火甲", "sunfire");
            AddTagWhenContains(tags, combinedText, "damage_over_time", "燃燒", "灼燒", "持續傷害", "burn", "liandry", "blackfire", "惡意", "malignance", "面具", "火甲", "sunfire");
            AddTagWhenContains(tags, combinedText, "missile", "飛彈", "導彈", "missile");
            AddTagWhenContains(tags, combinedText, "stackosaurus", "疊層", "層數", "stack", "堆疊暴龍", "疊角龍", "stackosaurus");
            AddTagWhenContains(tags, combinedText, "healing", "治療", "回復", "吸血", "healing");
            AddTagWhenContains(tags, combinedText, "assassin", "刺客", "突進", "秒殺", "assassin");
            return tags;
        }

        private static void AddTagWhenContains(
            ISet<string> tags,
            string text,
            string tag,
            params string[] needles)
        {
            if (needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            {
                tags.Add(tag);
            }
        }

        private static string ResolveChampionKey(string? coreChampion)
        {
            var value = TrimTo(coreChampion, 100).ToLowerInvariant();

            if (value.Contains("seraphine", StringComparison.OrdinalIgnoreCase)
                || value.Contains("瑟菈紛", StringComparison.OrdinalIgnoreCase)
                || value.Contains("瑟拉芬", StringComparison.OrdinalIgnoreCase))
            {
                return "seraphine";
            }

            if (value.Contains("brand", StringComparison.OrdinalIgnoreCase)
                || value.Contains("布蘭德", StringComparison.OrdinalIgnoreCase))
            {
                return "brand";
            }

            return value.Replace(" ", string.Empty);
        }

        private static AiKnowledgeContext CreateEmptyContext()
        {
            return new AiKnowledgeContext
            {
                HasGuide = false,
                CacheScope = "no-guide",
                PromptContext = "本機尚未找到符合這次請求的英雄或海克斯知識。"
            };
        }

        private static IEnumerable<string> SplitTags(string? tags)
        {
            return string.IsNullOrWhiteSpace(tags)
                ? Enumerable.Empty<string>()
                : tags.Split(new[] { ';', ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static IEnumerable<string> SplitAliases(string? aliases)
        {
            return string.IsNullOrWhiteSpace(aliases)
                ? Enumerable.Empty<string>()
                : aliases.Split(new[] { ';', ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static string NormalizeSearchText(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
        }

        private static string TextOrNone(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "無" : TrimTo(value, 1200);
        }

        private static string TrimTo(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private static string ReadString(SqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        private static LolAramAugment ReadAugment(SqlDataReader reader)
        {
            var augment = new LolAramAugment
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                AugmentKey = ReadString(reader, "AugmentKey"),
                Name = ReadString(reader, "Name"),
                ModeName = ReadString(reader, "ModeName"),
                Rarity = ReadString(reader, "Rarity"),
                Tier = ReadString(reader, "Tier"),
                SeriesKey = LolAramAugmentTagNormalizer.NormalizeSeriesKey(ReadString(reader, "SeriesKey")),
                EffectText = ReadString(reader, "EffectText"),
                Tags = ReadString(reader, "Tags"),
                SynergyNotes = ReadString(reader, "SynergyNotes"),
                PatchVersion = ReadString(reader, "PatchVersion"),
                SourceUrl = ReadString(reader, "SourceUrl"),
                Notes = ReadString(reader, "Notes"),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
            };

            augment.Tags = LolAramAugmentTagNormalizer.NormalizeTags(augment.Tags, augment.EffectText, augment.SeriesKey) ?? string.Empty;
            return augment;
        }

        private static LolAramItem ReadItem(SqlDataReader reader)
        {
            return new LolAramItem
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                ItemKey = ReadString(reader, "ItemKey"),
                Name = ReadString(reader, "Name"),
                Aliases = ReadString(reader, "Aliases"),
                ModeName = ReadString(reader, "ModeName"),
                EffectText = ReadString(reader, "EffectText"),
                Tags = ReadString(reader, "Tags"),
                SynergyNotes = ReadString(reader, "SynergyNotes"),
                PatchVersion = ReadString(reader, "PatchVersion"),
                SourceUrl = ReadString(reader, "SourceUrl"),
                Notes = ReadString(reader, "Notes"),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
            };
        }

        private static LolAramAugmentSeries ReadSeries(SqlDataReader reader)
        {
            return new LolAramAugmentSeries
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                SeriesKey = ReadString(reader, "SeriesKey"),
                SeriesName = ReadString(reader, "SeriesName"),
                Description = ReadString(reader, "Description"),
                SetBonusText = ReadString(reader, "SetBonusText"),
                Tags = ReadString(reader, "Tags"),
                PatchVersion = ReadString(reader, "PatchVersion"),
                SourceUrl = ReadString(reader, "SourceUrl"),
                Notes = ReadString(reader, "Notes"),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
            };
        }

        private static LolAramSynergyRule ReadRule(SqlDataReader reader)
        {
            return new LolAramSynergyRule
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                RuleName = ReadString(reader, "RuleName"),
                BoostAugmentKey = ReadString(reader, "BoostAugmentKey"),
                SeriesKey = ReadString(reader, "SeriesKey"),
                TriggerTags = ReadString(reader, "TriggerTags"),
                ChampionTags = ReadString(reader, "ChampionTags"),
                ItemTags = ReadString(reader, "ItemTags"),
                ConditionText = ReadString(reader, "ConditionText"),
                RecommendationText = ReadString(reader, "RecommendationText"),
                Priority = ReadString(reader, "Priority"),
                PatchVersion = ReadString(reader, "PatchVersion"),
                Notes = ReadString(reader, "Notes"),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
            };
        }

        private sealed class ChampionKnowledge
        {
            public string ChampionKey { get; set; } = string.Empty;

            public string ChampionName { get; set; } = string.Empty;

            public string PatchVersion { get; set; } = string.Empty;

            public string CacheScope { get; set; } = string.Empty;

            public string PromptContext { get; set; } = string.Empty;

            public string SourceLabel { get; set; } = string.Empty;

            public string? SourceUrl { get; set; }
        }

        private sealed class AugmentKnowledge
        {
            public static AugmentKnowledge Empty { get; } = new AugmentKnowledge();

            public bool HasData { get; set; }

            public string CacheScope { get; set; } = "no-augment";

            public string PromptContext { get; set; } = string.Empty;

            public string SourceLabel { get; set; } = string.Empty;

            public string? SourceUrl { get; set; }
        }
    }
}
