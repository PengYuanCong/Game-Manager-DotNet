namespace Proposal.Services
{
    public static class LolAramAugmentTagNormalizer
    {
        private static readonly char[] TagSeparators = { ';', ',', '，', '、', '；' };

        public static string? NormalizeSeriesKey(string? value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "firecracker" or "爆竹" or "爆竹系列" => "firecracker",
                "stackosaurus" or "stacking_dino" or "stackingdino" or "疊層暴龍" or "堆疊暴龍" or "堆疊暴龍系列" => "stackosaurus",
                "snowday" or "snow_day" or "下雪天" or "下雪天系列" or "雪球" or "雪球系列" => "snowday",
                "self_destruct" or "selfdestruct" or "divebomb" or "自爆" or "自爆系列" or "俯衝炸彈" or "俯衝炸彈系列" => "self_destruct",
                "low_health_ally" or "lowhealthally" or "ping" or "嗚咿嗚咿" or "嗚咿嗚咿系列" or "喂喂喂喂" or "喂喂喂喂系列" => "low_health_ally",
                "archmage" or "大法師" or "大法師系列" => "archmage",
                "automation" or "auto_cast" or "autocast" or "自動化" or "自動化系列" or "完全自動化" or "完全自動化系列" => "automation",
                "high_roller" or "highroller" or "dice" or "土豪賭客" or "土豪賭客系列" or "擲骰狂人" or "擲骰狂人系列" => "high_roller",
                "coinrain" or "coin_rain" or "錢如雨下" or "錢如雨下系列" or "金幣雨" or "金幣雨系列" => "coinrain",
                "" or null => null,
                _ => value!.Trim()
            };
        }

        public static string? NormalizeTags(string? tags, string? effectText = null, string? seriesKey = null)
        {
            var normalizedTags = new List<string>();
            var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tag in SplitTags(tags))
            {
                AddTag(normalizedTags, seenTags, NormalizeTag(tag));
            }

            var normalizedSeries = NormalizeSeriesKey(seriesKey);
            AddSeriesTag(normalizedTags, seenTags, normalizedSeries);
            InferTagsFromEffect(normalizedTags, seenTags, effectText);

            return normalizedTags.Count == 0 ? null : string.Join("; ", normalizedTags);
        }

        public static string? NormalizeTag(string? value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "burn" or "燃燒" or "灼燒" => "burn",
                "firecracker" or "爆竹" or "爆竹系列" => "firecracker",
                "self_destruct" or "selfdestruct" or "divebomb" or "自爆" or "復活" or "revive" => "self_destruct",
                "stackosaurus" or "stacking_dino" or "stackingdino" or "scaling" or "成長" or "後期" or "stacking" or "stack" or "疊層" or "層數" or "堆疊" or "疊層暴龍" or "堆疊暴龍" => "stackosaurus",
                "low_health_ally" or "lowhealthally" or "ping" or "嗚咿嗚咿" or "喂喂喂喂" or "低血量友軍" => "low_health_ally",
                "auto_cast" or "autocast" or "automation" or "自動施放" or "自動化" => "automation",
                "high_roller" or "highroller" or "dice" or "anvil" or "鍛造器" or "土豪賭客" => "high_roller",
                "coinrain" or "coin_rain" or "gold" or "coin_drop" or "coindrop" or "economy" or "金錢" or "金幣" or "錢幣" or "經濟" or "錢如雨下" => "coinrain",
                "healing" or "heal" or "治療" or "回復" => "healing",
                "shield" or "護盾" => "shield",
                "magic_damage" or "magicdamage" or "magic" or "ap" or "魔法傷害" or "魔法" => "magic_damage",
                "mana" or "法力" or "魔力" => "mana",
                "physical_damage" or "physicaldamage" or "physical" or "ad" or "物理傷害" or "物理" => "physical_damage",
                "true_damage" or "truedamage" or "真實傷害" => "true_damage",
                "ability_haste" or "abilityhaste" or "haste" or "技能急速" or "冷卻" => "ability_haste",
                "movement_speed" or "movementspeed" or "movespeed" or "跑速" or "移動速度" => "movement_speed",
                "crowd_control" or "crowdcontrol" or "cc" or "控制" or "控場" => "crowd_control",
                "slow" or "緩速" => "slow",
                "missile" or "projectile" or "飛彈" or "導彈" => "missile",
                "multi_hit" or "multihit" or "多段" or "多段傷害" or "多段命中" => "multi_hit",
                "tank" or "health" or "armor" or "magic_resist" or "magicresist" or "坦度" or "生命" or "物防" or "護甲" or "魔防" => "tank",
                "team_support" or "teamsupport" or "support" or "輔助" or "隊友增益" or "團隊增益" => "team_support",
                "opgg_imported" => "opgg_imported",
                "" or null => null,
                _ => value!.Trim()
            };
        }

        private static IEnumerable<string> SplitTags(string? tags)
        {
            return string.IsNullOrWhiteSpace(tags)
                ? Array.Empty<string>()
                : tags.Split(TagSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static void AddSeriesTag(List<string> tags, HashSet<string> seenTags, string? seriesKey)
        {
            switch (seriesKey)
            {
                case "firecracker":
                case "self_destruct":
                case "stackosaurus":
                case "low_health_ally":
                case "automation":
                case "high_roller":
                case "coinrain":
                    AddTag(tags, seenTags, seriesKey);
                    break;
            }
        }

        private static void InferTagsFromEffect(List<string> tags, HashSet<string> seenTags, string? effectText)
        {
            if (string.IsNullOrWhiteSpace(effectText))
            {
                return;
            }

            AddTagIf(tags, seenTags, effectText, "burn", "燃燒", "灼燒");
            AddTagIf(tags, seenTags, effectText, "firecracker", "爆竹");
            AddTagIf(tags, seenTags, effectText, "self_destruct", "自爆", "復活倒數", "復活計時", "炸彈");
            AddTagIf(tags, seenTags, effectText, "stackosaurus", "疊層", "層數", "堆疊", "成長");
            AddTagIf(tags, seenTags, effectText, "low_health_ally", "低於 50%", "低於50%", "生命值友軍", "治療或護盾回復目標");
            AddTagIf(tags, seenTags, effectText, "automation", "自動施放", "自動施放冷卻", "自動化");
            AddTagIf(tags, seenTags, effectText, "high_roller", "鍛造器", "屬性加成");
            AddTagIf(tags, seenTags, effectText, "coinrain", "金幣", "錢幣", "掉落一些錢");
            AddTagIf(tags, seenTags, effectText, "healing", "治療", "回復", "生命回復");
            AddTagIf(tags, seenTags, effectText, "shield", "護盾");
            AddTagIf(tags, seenTags, effectText, "magic_damage", "魔法傷害");
            AddTagIf(tags, seenTags, effectText, "physical_damage", "物理傷害");
            AddTagIf(tags, seenTags, effectText, "true_damage", "真實傷害");
            AddTagIf(tags, seenTags, effectText, "ability_haste", "技能急速", "冷卻時間", "冷卻");
            AddTagIf(tags, seenTags, effectText, "movement_speed", "移動速度", "跑速");
            AddTagIf(tags, seenTags, effectText, "crowd_control", "暈眩", "定身", "緩速", "擊飛", "沉默", "嘲諷", "控場");
            AddTagIf(tags, seenTags, effectText, "mana", "法力", "魔力");
            AddTagIf(tags, seenTags, effectText, "tank", "生命值", "最大生命", "護甲", "魔法防禦", "魔防", "物防");
            AddTagIf(tags, seenTags, effectText, "missile", "飛彈", "導彈");
            AddTagIf(tags, seenTags, effectText, "multi_hit", "每第", "多段", "再次命中");
            AddTagIf(tags, seenTags, effectText, "team_support", "友軍", "隊友", "全隊");
        }

        private static void AddTagIf(
            List<string> tags,
            HashSet<string> seenTags,
            string effectText,
            string tag,
            params string[] needles)
        {
            if (needles.Any(needle => effectText.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            {
                AddTag(tags, seenTags, tag);
            }
        }

        private static void AddTag(List<string> tags, HashSet<string> seenTags, string? tag)
        {
            if (!string.IsNullOrWhiteSpace(tag) && seenTags.Add(tag))
            {
                tags.Add(tag);
            }
        }
    }
}
