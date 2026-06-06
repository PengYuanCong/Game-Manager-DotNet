internal static class AugmentSeriesMigrationNormalizer
{
    private static readonly char[] Separators = ['、', ',', '，', ';', '；', '/', '|'];

    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["firecracker"] = "firecracker",
            ["爆竹"] = "firecracker",
            ["爆竹系列"] = "firecracker",

            ["stackosaurus"] = "stackosaurus",
            ["stackosaurus_rex"] = "stackosaurus",
            ["stacking_dino"] = "stackosaurus",
            ["stackingdino"] = "stackosaurus",
            ["疊層暴龍"] = "stackosaurus",
            ["堆疊暴龍"] = "stackosaurus",
            ["堆疊暴龍系列"] = "stackosaurus",

            ["snowday"] = "snowday",
            ["snow_day"] = "snowday",
            ["snowball"] = "snowday",
            ["下雪天"] = "snowday",
            ["下雪天系列"] = "snowday",
            ["雪球"] = "snowday",
            ["雪球系列"] = "snowday",

            ["self_destruct"] = "self_destruct",
            ["selfdestruct"] = "self_destruct",
            ["divebomb"] = "self_destruct",
            ["自爆"] = "self_destruct",
            ["自爆系列"] = "self_destruct",
            ["俯衝炸彈"] = "self_destruct",
            ["俯衝炸彈系列"] = "self_destruct",

            ["low_health_ally"] = "low_health_ally",
            ["lowhealthally"] = "low_health_ally",
            ["siren"] = "low_health_ally",
            ["ping"] = "low_health_ally",
            ["嗚咿嗚咿"] = "low_health_ally",
            ["嗚咿嗚咿系列"] = "low_health_ally",
            ["喂喂喂喂"] = "low_health_ally",
            ["喂喂喂喂系列"] = "low_health_ally",

            ["archmage"] = "archmage",
            ["大法師"] = "archmage",
            ["大法師系列"] = "archmage",

            ["automation"] = "automation",
            ["auto_cast"] = "automation",
            ["autocast"] = "automation",
            ["全自動"] = "automation",
            ["自動化"] = "automation",
            ["自動化系列"] = "automation",
            ["完全自動化"] = "automation",
            ["完全自動化系列"] = "automation",

            ["high_roller"] = "high_roller",
            ["highroller"] = "high_roller",
            ["dice"] = "high_roller",
            ["土豪賭客"] = "high_roller",
            ["土豪賭客系列"] = "high_roller",
            ["擲骰狂人"] = "high_roller",
            ["擲骰狂人系列"] = "high_roller",

            ["coinrain"] = "coinrain",
            ["coin_rain"] = "coinrain",
            ["money_rain"] = "coinrain",
            ["錢如雨下"] = "coinrain",
            ["錢如雨下系列"] = "coinrain",
            ["金幣雨"] = "coinrain",
            ["金幣雨系列"] = "coinrain"
        };

    public static IReadOnlyList<AugmentSeriesDefinition> Definitions { get; } =
    [
        new(
            "snowday",
            "雪球",
            "提升雪球類強化符文的技能急速與傷害。",
            "(2) +50 技能急速，30% 傷害提升。\n(3) +100 技能急速，50% 傷害提升。\n(4) +150 技能急速，100% 傷害提升。",
            "snowday; engage; skillshot; ability_haste"),
        new(
            "self_destruct",
            "自爆",
            "以自動放置炸彈、死亡與復活節奏為核心的套裝。",
            "(2) 使你的復活倒數計時減少 25%。",
            "self_destruct; revive; bomb; automation"),
        new(
            "stackosaurus",
            "堆疊暴龍",
            "在你疊層時獲得額外層數，適合能持續參戰與累積成長的英雄。",
            "(2) 獲得額外的 50% 層數。\n(3) 獲得額外的 100% 層數。\n(4) 獲得額外的 200% 層數。",
            "stackosaurus; takedown; scaling; extended_fight"),
        new(
            "low_health_ally",
            "嗚咿嗚咿",
            "朝低於 50% 生命值的友軍移動時獲得額外支援效果。",
            "(2) 獲得 50% 移動速度。\n(3) 下一個治療或護盾回復目標 12% 已損失生命值，每個目標有 10 秒冷卻時間。",
            "low_health_ally; healing; shield; ally; movement_speed"),
        new(
            "archmage",
            "大法師",
            "施放技能時返還另一個隨機技能的部分冷卻時間。",
            "(2) 返還另一個隨機技能 30% 的冷卻時間。",
            "archmage; spell; ability_haste; mage"),
        new(
            "automation",
            "自動化",
            "強化會自動施放或週期觸發的海克斯效果。",
            "(2) 將自動施放的冷卻時間縮短 30%。\n(3) 自動施放冷卻時間受益於技能急速。",
            "automation; auto_cast; cooldown; proc"),
        new(
            "high_roller",
            "土豪賭客",
            "以小兵掉落屬性與高階鍛造器為核心的經濟套裝。",
            "(2) 小兵陣亡時有機率掉落屬性加成。\n(3) +20% 機率獲得黃金階或稜彩階鍛造器。\n(4) +50% 機率獲得黃金階或稜彩階鍛造器。",
            "high_roller; economy; anvil; minion; stat"),
        new(
            "coinrain",
            "錢如雨下",
            "參與擊殺後讓敵人掉落可拾取的錢幣。",
            "(2) 參與擊殺後掉落 6 枚錢幣。\n(3) 參與擊殺後掉落 12 枚錢幣。\n(4) 每枚錢幣價值 5 金，僅能由持有者與友軍拾取。",
            "coinrain; economy; takedown; gold"),
        new(
            "firecracker",
            "爆竹",
            "爆竹會彈跳到鄰近敵人並造成部分原傷害。",
            "(2) 彈跳 2 次，造成 25% 原傷害。\n(4) 彈跳 3 次，造成 50% 原傷害。",
            "firecracker; missile; explosion; chain; area; proc")
    ];

    public static string NormalizePrimary(string value)
    {
        var normalized = NormalizeAll(value);
        if (normalized.Count == 0)
        {
            throw new InvalidOperationException("Series key cannot be empty.");
        }

        return normalized[0];
    }

    public static IReadOnlyList<string> NormalizeAll(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in value.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Aliases.TryGetValue(part, out var key))
            {
                throw new InvalidOperationException($"Unknown augment series key: {part}");
            }

            if (seen.Add(key))
            {
                normalized.Add(key);
            }
        }

        return normalized;
    }

    public static bool TryNormalizeAll(string? value, out IReadOnlyList<string> normalized)
    {
        try
        {
            normalized = NormalizeAll(value);
            return true;
        }
        catch (InvalidOperationException)
        {
            normalized = [];
            return false;
        }
    }

    public static bool SecondarySeriesArePreserved(string? seriesValue, string? tags)
    {
        var normalizedSeries = NormalizeAll(seriesValue);
        if (normalizedSeries.Count <= 1)
        {
            return true;
        }

        var normalizedTags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in (tags ?? string.Empty)
                     .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Aliases.TryGetValue(tag, out var key))
            {
                normalizedTags.Add(key);
            }
        }

        return normalizedSeries.Skip(1).All(normalizedTags.Contains);
    }

    public static object NormalizeDbValue(object value)
    {
        if (value == DBNull.Value || string.IsNullOrWhiteSpace(Convert.ToString(value)))
        {
            return DBNull.Value;
        }

        return NormalizePrimary(Convert.ToString(value)!);
    }
}

internal sealed record AugmentSeriesDefinition(
    string Key,
    string Name,
    string Description,
    string SetBonusText,
    string Tags);
