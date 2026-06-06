using System.Text;
using Npgsql;
using NpgsqlTypes;
using Proposal.Models;

namespace Proposal.Services;

public sealed class PostgresLolAramKnowledgeBaseService : IAiKnowledgeBaseService
{
    private const int MaxSearchInputLength = 1000;
    private const int MaxChampionPromptLength = 6000;
    private const int MaxAugmentPromptLength = 9000;
    private const int MaxKnowledgePromptLength = 12000;
    private const int MaxDetectedTagTextLength = 14000;

    private readonly IPostgresConnectionFactory _connectionFactory;
    private readonly ILogger<PostgresLolAramKnowledgeBaseService> _logger;

    public PostgresLolAramKnowledgeBaseService(
        IPostgresConnectionFactory connectionFactory,
        ILogger<PostgresLolAramKnowledgeBaseService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<AiKnowledgeContext> GetContextAsync(
        AiRecommendationInput input,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (NpgsqlException ex)
        {
            _logger.LogWarning(ex, "PostgreSQL ARAM knowledge database is unavailable.");
            return CreateEmptyContext();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "PostgreSQL ARAM knowledge connection is not configured.");
            return CreateEmptyContext();
        }

        var championGuide = await GetChampionGuideAsync(connection, input.CoreChampion, cancellationToken);
        var augmentKnowledge = await GetAugmentKnowledgeAsync(connection, input, championGuide, cancellationToken);

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
            prompt.AppendLine("No champion-specific local guide matched the current input.");
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
            SourceLabel = string.Join(" + ", sourceLabels.DefaultIfEmpty("local ARAM knowledge")),
            PromptContext = TrimTo(prompt.ToString(), MaxKnowledgePromptLength)
        };
    }

    private static async Task<ChampionKnowledge?> GetChampionGuideAsync(
        NpgsqlConnection connection,
        string? championInput,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(championInput))
        {
            return null;
        }

        var text = TrimTo(championInput, 100);
        var championKey = NormalizeChampionKey(text);

        const string sql = """
            select champion_key, champion_name, localized_name, mode_name, patch_version,
                   role_summary, core_items, situational_items, augments, summoner_spells,
                   skill_order, playstyle_tips, positioning_tips, weaknesses, source_url,
                   notes, updated_at
            from public.lol_aram_guides
            where champion_key = @champion_key
               or lower(champion_name) = lower(@text)
               or lower(coalesce(localized_name, '')) = lower(@text)
               or @text ilike '%' || champion_name || '%'
               or (localized_name is not null and @text ilike '%' || localized_name || '%')
            order by case
                when champion_key = @champion_key then 0
                when lower(coalesce(localized_name, '')) = lower(@text) then 1
                when lower(champion_name) = lower(@text) then 2
                else 3
            end, updated_at desc
            limit 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@champion_key", NpgsqlDbType.Varchar, 100).Value = championKey;
        command.Parameters.Add("@text", NpgsqlDbType.Varchar, 100).Value = text;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var updatedAt = ReadDateTime(reader, "updated_at");
        var patchVersion = ReadString(reader, "patch_version");
        var key = ReadString(reader, "champion_key");
        var championName = ReadString(reader, "champion_name");
        var localizedName = ReadString(reader, "localized_name");
        var sourceUrl = ReadString(reader, "source_url");

        var promptContext = $"""
            Local champion guide:
            Champion: {championName} ({localizedName})
            Mode: {ReadString(reader, "mode_name")}
            Patch / reference version: {patchVersion}
            Role summary: {ReadString(reader, "role_summary")}
            Core items: {ReadString(reader, "core_items")}
            Situational items: {ReadString(reader, "situational_items")}
            Augment direction: {ReadString(reader, "augments")}
            Summoner spells: {ReadString(reader, "summoner_spells")}
            Skill order: {ReadString(reader, "skill_order")}
            Playstyle tips: {ReadString(reader, "playstyle_tips")}
            Positioning tips: {ReadString(reader, "positioning_tips")}
            Weaknesses and cautions: {ReadString(reader, "weaknesses")}
            Curator notes: {ReadString(reader, "notes")}
            Source reference: {sourceUrl}

            Use this champion guide as the source of truth for champion-specific advice.
            """;

        return new ChampionKnowledge
        {
            ChampionKey = key,
            ChampionName = championName,
            PatchVersion = patchVersion,
            CacheScope = $"champion:{key}:{patchVersion}:{updatedAt:yyyyMMddHHmmss}",
            SourceUrl = sourceUrl,
            SourceLabel = $"{TextOrDefault(localizedName, championName)} ARAM guide {patchVersion}",
            PromptContext = TrimTo(promptContext, MaxChampionPromptLength)
        };
    }

    private async Task<AugmentKnowledge> GetAugmentKnowledgeAsync(
        NpgsqlConnection connection,
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

        foreach (var augment in matchedAugments)
        {
            prompt.AppendLine($"""
                Augment: {augment.Name}
                Key: {augment.AugmentKey}
                Rarity: {augment.Rarity}
                Tier: {TextOrDefault(augment.Tier)}
                Series key: {TextOrDefault(augment.SeriesKey)}
                Tags: {TextOrDefault(augment.Tags)}
                Effect: {augment.EffectText}
                Synergy notes: {TextOrDefault(augment.SynergyNotes)}
                Curator notes: {TextOrDefault(augment.Notes)}
                """);
        }

        if (matchedItems.Count > 0)
        {
            prompt.AppendLine("Matched item knowledge:");
            foreach (var item in matchedItems)
            {
                prompt.AppendLine($"""
                    Item: {item.Name}
                    Key: {item.ItemKey}
                    Aliases: {TextOrDefault(item.Aliases)}
                    Tags: {TextOrDefault(item.Tags)}
                    Effect: {item.EffectText}
                    Synergy notes: {TextOrDefault(item.SynergyNotes)}
                    Curator notes: {TextOrDefault(item.Notes)}
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
                    Tags: {TextOrDefault(item.Tags)}
                    Description: {TextOrDefault(item.Description)}
                    Set bonus / combo logic: {TextOrDefault(item.SetBonusText)}
                    Notes: {TextOrDefault(item.Notes)}
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
                    Trigger tags: {TextOrDefault(rule.TriggerTags)}
                    Condition: {rule.ConditionText}
                    Recommendation logic: {rule.RecommendationText}
                    Notes: {TextOrDefault(rule.Notes)}
                    """);
            }
        }

        prompt.AppendLine("""
            When judging augments, combine direct series synergy and tag synergy.
            Series synergy means augments share the same SeriesKey.
            Tag synergy means effect words match across champion, item, and augment knowledge.
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
            SourceLabel = $"{matchedAugments.Count} local augments + {matchedItems.Count} local items",
            SourceUrl = matchedAugments.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.SourceUrl))?.SourceUrl,
            PromptContext = TrimTo(prompt.ToString(), MaxAugmentPromptLength)
        };
    }

    private static async Task<List<LolAramAugment>> FindInputAugmentsAsync(
        NpgsqlConnection connection,
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
            .Take(8)
            .ToList();
    }

    private static async Task<List<LolAramItem>> FindInputItemsAsync(
        NpgsqlConnection connection,
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
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select id, augment_key, name, mode_name, rarity, tier, series_key, effect_text,
                   tags, synergy_notes, patch_version, source_url, notes, created_at, updated_at
            from public.lol_aram_augments
            order by updated_at desc
            limit 300;
            """;

        var results = new List<LolAramAugment>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadAugment(reader));
        }

        return results;
    }

    private static async Task<List<LolAramItem>> ReadAllItemsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select id, item_key, name, aliases, mode_name, effect_text, tags,
                   synergy_notes, patch_version, source_url, notes, created_at, updated_at
            from public.lol_aram_items
            order by updated_at desc
            limit 300;
            """;

        var results = new List<LolAramItem>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadItem(reader));
        }

        return results;
    }

    private static async Task<List<LolAramAugmentSeries>> FindSeriesAsync(
        NpgsqlConnection connection,
        IEnumerable<string?> seriesKeys,
        CancellationToken cancellationToken)
    {
        var keys = seriesKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (keys.Length == 0)
        {
            return new List<LolAramAugmentSeries>();
        }

        const string sql = """
            select id, series_key, series_name, description, set_bonus_text, tags,
                   patch_version, source_url, notes, created_at, updated_at
            from public.lol_aram_augment_series
            where series_key = any(@series_keys);
            """;

        var results = new List<LolAramAugmentSeries>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@series_keys", NpgsqlDbType.Array | NpgsqlDbType.Varchar).Value = keys;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadSeries(reader));
        }

        return results;
    }

    private static async Task<List<LolAramSynergyRule>> FindSynergyRulesAsync(
        NpgsqlConnection connection,
        IReadOnlyList<LolAramAugment> augments,
        IReadOnlyList<LolAramAugmentSeries> series,
        IReadOnlySet<string> tags,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select id, rule_name, boost_augment_key, series_key, trigger_tags, champion_tags,
                   item_tags, condition_text, recommendation_text, priority, patch_version,
                   notes, created_at, updated_at
            from public.lol_aram_synergy_rules
            order by case priority when 'high' then 1 when 'medium' then 2 else 3 end,
                     updated_at desc
            limit 100;
            """;

        var augmentKeys = augments
            .Select(augment => augment.AugmentKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seriesKeys = series
            .Select(item => item.SeriesKey)
            .Concat(augments.Select(augment => augment.SeriesKey ?? string.Empty))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var results = new List<LolAramSynergyRule>();
        await using var command = new NpgsqlCommand(sql, connection);
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
        AddTagWhenContains(tags, combinedText, "burn", "burn", "blackfire", "liandry", "malignance", "sunfire", "brand");
        AddTagWhenContains(tags, combinedText, "damage_over_time", "burn", "liandry", "blackfire", "malignance", "sunfire");
        AddTagWhenContains(tags, combinedText, "missile", "missile", "projectile");
        AddTagWhenContains(tags, combinedText, "stackosaurus", "stack", "stackosaurus");
        AddTagWhenContains(tags, combinedText, "healing", "heal", "healing", "shield");
        AddTagWhenContains(tags, combinedText, "assassin", "assassin", "lethality");
        AddTagWhenContains(tags, combinedText, "magic_damage", "ap", "magic", "mage");
        AddTagWhenContains(tags, combinedText, "physical_damage", "ad", "attack", "marksman");
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

    private static AiKnowledgeContext CreateEmptyContext()
    {
        return new AiKnowledgeContext
        {
            HasGuide = false,
            CacheScope = "no-guide",
            PromptContext = "No local ARAM knowledge was available for this request."
        };
    }

    private static IEnumerable<string> SplitTags(string? tags)
    {
        return string.IsNullOrWhiteSpace(tags)
            ? Enumerable.Empty<string>()
            : tags.Split(new[] { ';', ',', '/', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IEnumerable<string> SplitAliases(string? aliases)
    {
        return string.IsNullOrWhiteSpace(aliases)
            ? Enumerable.Empty<string>()
            : aliases.Split(new[] { ';', ',', '/', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string NormalizeChampionKey(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(" ", string.Empty).ToLowerInvariant();
    }

    private static string NormalizeSearchText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
    }

    private static string TextOrDefault(string? value, string fallback = "none")
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : TrimTo(value, 1200);
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

    private static string ReadString(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    private static int ReadInt32(NpgsqlDataReader reader, string name)
    {
        return Convert.ToInt32(reader[name]);
    }

    private static DateTime ReadDateTime(NpgsqlDataReader reader, string name)
    {
        var value = reader[name];
        return value is DateTime dateTime ? dateTime : Convert.ToDateTime(value);
    }

    private static LolAramAugment ReadAugment(NpgsqlDataReader reader)
    {
        var augment = new LolAramAugment
        {
            Id = ReadInt32(reader, "id"),
            AugmentKey = ReadString(reader, "augment_key"),
            Name = ReadString(reader, "name"),
            ModeName = ReadString(reader, "mode_name"),
            Rarity = ReadString(reader, "rarity"),
            Tier = ReadString(reader, "tier"),
            SeriesKey = LolAramAugmentTagNormalizer.NormalizeSeriesKey(ReadString(reader, "series_key")),
            EffectText = ReadString(reader, "effect_text"),
            Tags = ReadString(reader, "tags"),
            SynergyNotes = ReadString(reader, "synergy_notes"),
            PatchVersion = ReadString(reader, "patch_version"),
            SourceUrl = ReadString(reader, "source_url"),
            Notes = ReadString(reader, "notes"),
            CreatedAt = ReadDateTime(reader, "created_at"),
            UpdatedAt = ReadDateTime(reader, "updated_at")
        };

        augment.Tags = LolAramAugmentTagNormalizer.NormalizeTags(augment.Tags, augment.EffectText, augment.SeriesKey) ?? string.Empty;
        return augment;
    }

    private static LolAramItem ReadItem(NpgsqlDataReader reader)
    {
        return new LolAramItem
        {
            Id = ReadInt32(reader, "id"),
            ItemKey = ReadString(reader, "item_key"),
            Name = ReadString(reader, "name"),
            Aliases = ReadString(reader, "aliases"),
            ModeName = ReadString(reader, "mode_name"),
            EffectText = ReadString(reader, "effect_text"),
            Tags = ReadString(reader, "tags"),
            SynergyNotes = ReadString(reader, "synergy_notes"),
            PatchVersion = ReadString(reader, "patch_version"),
            SourceUrl = ReadString(reader, "source_url"),
            Notes = ReadString(reader, "notes"),
            CreatedAt = ReadDateTime(reader, "created_at"),
            UpdatedAt = ReadDateTime(reader, "updated_at")
        };
    }

    private static LolAramAugmentSeries ReadSeries(NpgsqlDataReader reader)
    {
        return new LolAramAugmentSeries
        {
            Id = ReadInt32(reader, "id"),
            SeriesKey = ReadString(reader, "series_key"),
            SeriesName = ReadString(reader, "series_name"),
            Description = ReadString(reader, "description"),
            SetBonusText = ReadString(reader, "set_bonus_text"),
            Tags = ReadString(reader, "tags"),
            PatchVersion = ReadString(reader, "patch_version"),
            SourceUrl = ReadString(reader, "source_url"),
            Notes = ReadString(reader, "notes"),
            CreatedAt = ReadDateTime(reader, "created_at"),
            UpdatedAt = ReadDateTime(reader, "updated_at")
        };
    }

    private static LolAramSynergyRule ReadRule(NpgsqlDataReader reader)
    {
        return new LolAramSynergyRule
        {
            Id = ReadInt32(reader, "id"),
            RuleName = ReadString(reader, "rule_name"),
            BoostAugmentKey = ReadString(reader, "boost_augment_key"),
            SeriesKey = ReadString(reader, "series_key"),
            TriggerTags = ReadString(reader, "trigger_tags"),
            ChampionTags = ReadString(reader, "champion_tags"),
            ItemTags = ReadString(reader, "item_tags"),
            ConditionText = ReadString(reader, "condition_text"),
            RecommendationText = ReadString(reader, "recommendation_text"),
            Priority = ReadString(reader, "priority"),
            PatchVersion = ReadString(reader, "patch_version"),
            Notes = ReadString(reader, "notes"),
            CreatedAt = ReadDateTime(reader, "created_at"),
            UpdatedAt = ReadDateTime(reader, "updated_at")
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
        public static AugmentKnowledge Empty { get; } = new();

        public bool HasData { get; set; }

        public string CacheScope { get; set; } = "no-augment";

        public string PromptContext { get; set; } = string.Empty;

        public string SourceLabel { get; set; } = string.Empty;

        public string? SourceUrl { get; set; }
    }
}
