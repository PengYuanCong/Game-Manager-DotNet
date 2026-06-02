namespace Proposal.Services
{
    public static class LolItemNameNormalizer
    {
        private static readonly (string Alias, string TraditionalName)[] ItemNameAliases =
        {
            ("Liandry's Anguish", "蘭德里的折磨"),
            ("Liandry's Torment", "蘭德里的折磨"),
            ("Liandry", "蘭德里的折磨"),
            ("兰德里的折磨", "蘭德里的折磨"),
            ("兰德里的苦楚", "蘭德里的折磨"),
            ("李安德", "蘭德里的折磨"),
            ("Zhonya's Hourglass", "中婭沙漏"),
            ("Zhonya", "中婭沙漏"),
            ("中娅沙漏", "中婭沙漏"),
            ("Rylai's Crystal Scepter", "瑞萊的冰晶節杖"),
            ("Rylai", "瑞萊的冰晶節杖"),
            ("瑞莱", "瑞萊的冰晶節杖"),
            ("Blackfire Torch", "黑焰火炬"),
            ("Malignance", "惡意"),
            ("恶意", "惡意"),
            ("Sorcerer's Shoes", "法師之靴"),
            ("Void Staff", "虛空之杖"),
            ("Rabadon's Deathcap", "死亡之帽"),
            ("Morellonomicon", "黑魔禁書"),
            ("Shadowflame", "影焰"),
            ("Horizon Focus", "視界專注"),
            ("Banshee's Veil", "女妖面紗"),
            ("Cryptbloom", "碎空花"),
            ("Stormsurge", "風暴奔湧"),
            ("Luden's Companion", "盧登夥伴"),
            ("Nashor's Tooth", "納什之牙"),
            ("Riftmaker", "峽谷製造者"),
            ("Infinity Edge", "無盡之刃"),
            ("The Collector", "蒐集者"),
            ("Lord Dominik's Regards", "多明尼克的問候"),
            ("Mortal Reminder", "致死宣告"),
            ("Serylda's Grudge", "賽瑞爾達的怨恨"),
            ("Youmuu's Ghostblade", "妖夢鬼刀"),
            ("Opportunity", "良機"),
            ("Hubris", "傲慢"),
            ("Profane Hydra", "褻瀆九頭蛇"),
            ("Ravenous Hydra", "貪欲九頭蛇"),
            ("Titanic Hydra", "泰坦九頭蛇"),
            ("Eclipse", "星蝕"),
            ("Manamune", "魔劍正宗"),
            ("Muramana", "魔劍"),
            ("Kraken Slayer", "海妖殺手"),
            ("Guinsoo's Rageblade", "鬼索的狂暴之刃"),
            ("Blade of the Ruined King", "殞落王者之劍"),
            ("Terminus", "終界弓"),
            ("Heartsteel", "心之鋼"),
            ("Warmog's Armor", "好戰者鎧甲"),
            ("Jak'Sho", "千變者賈修"),
            ("Spirit Visage", "振奮盔甲"),
            ("Randuin's Omen", "蘭頓之兆"),
            ("Thornmail", "荊棘之甲"),
            ("Sunfire Aegis", "日炎聖盾")
        };

        public static string Normalize(string? itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName))
            {
                return string.Empty;
            }

            var normalized = itemName
                .Trim()
                .Replace('’', '\'')
                .Replace("：", ":", StringComparison.Ordinal)
                .Replace("　", " ", StringComparison.Ordinal);

            while (normalized.Contains("  ", StringComparison.Ordinal))
            {
                normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
            }

            foreach (var (alias, traditionalName) in ItemNameAliases)
            {
                if (normalized.Equals(alias, StringComparison.OrdinalIgnoreCase))
                {
                    return traditionalName;
                }
            }

            foreach (var (alias, traditionalName) in ItemNameAliases)
            {
                if (normalized.Contains(alias, StringComparison.OrdinalIgnoreCase))
                {
                    return traditionalName;
                }
            }

            return normalized;
        }
    }
}
