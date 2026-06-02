using Proposal.Models;

namespace Proposal.Services
{
    public sealed record LolAramGuideAugmentBadge(string Name, string Tier, string Rarity);

    public static class LolAramGuideAugmentParser
    {
        private static readonly IReadOnlyList<SectionLabel> SectionLabels =
        [
            new("稜彩優先", "稜彩階"),
            new("彩色優先", "稜彩階"),
            new("黃金優先", "黃金階"),
            new("金色優先", "黃金階"),
            new("白銀優先", "白銀階"),
            new("白色優先", "白銀階")
        ];

        public static IReadOnlyList<LolAramGuideAugmentBadge> Parse(
            string? value,
            IReadOnlyDictionary<string, LolAramAugment> catalog)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<LolAramGuideAugmentBadge>();
            }

            var text = RemoveRatingNotes(value.Trim());
            var sections = FindSections(text);
            if (sections.Count > 0)
            {
                var sectionBadges = new List<LolAramGuideAugmentBadge>();
                foreach (var section in sections)
                {
                    sectionBadges.AddRange(ParseList(section.Text, section.Rarity, catalog));
                }

                return sectionBadges;
            }

            text = RemoveKnownPrefix(text);
            return ParseList(text, string.Empty, catalog).ToList();
        }

        private static string RemoveRatingNotes(string value)
        {
            var ratingIndex = value.IndexOf("評級邏輯", StringComparison.Ordinal);
            return ratingIndex >= 0 ? value[..ratingIndex] : value;
        }

        private static string RemoveKnownPrefix(string value)
        {
            var opGgPrefixIndex = value.IndexOf("OP.GG 推薦海克斯", StringComparison.OrdinalIgnoreCase);
            if (opGgPrefixIndex >= 0)
            {
                var colonIndex = value.IndexOf('：', opGgPrefixIndex);
                return colonIndex >= 0 ? value[(colonIndex + 1)..] : value;
            }

            foreach (var prefix in new[] { "推薦海克斯：", "強化符文：", "增強方向：" })
            {
                if (value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return value[prefix.Length..];
                }
            }

            return value;
        }

        private static List<SectionSource> FindSections(string value)
        {
            var markers = new List<SectionMarker>();
            foreach (var label in SectionLabels)
            {
                var start = 0;
                while (start < value.Length)
                {
                    var index = value.IndexOf(label.Name, start, StringComparison.Ordinal);
                    if (index < 0)
                    {
                        break;
                    }

                    var colonIndex = value.IndexOf('：', index + label.Name.Length);
                    if (colonIndex >= 0)
                    {
                        markers.Add(new SectionMarker(index, colonIndex + 1, label.Rarity));
                    }

                    start = index + label.Name.Length;
                }
            }

            if (markers.Count == 0)
            {
                return new List<SectionSource>();
            }

            markers = markers.OrderBy(marker => marker.LabelStart).ToList();
            var sections = new List<SectionSource>();
            for (var i = 0; i < markers.Count; i++)
            {
                var current = markers[i];
                var end = i + 1 < markers.Count ? markers[i + 1].LabelStart : value.Length;
                if (end <= current.ContentStart)
                {
                    continue;
                }

                var sectionText = value[current.ContentStart..end].Trim();
                if (!string.IsNullOrWhiteSpace(sectionText))
                {
                    sections.Add(new SectionSource(sectionText, current.Rarity));
                }
            }

            return sections;
        }

        private static IEnumerable<LolAramGuideAugmentBadge> ParseList(
            string value,
            string defaultRarity,
            IReadOnlyDictionary<string, LolAramAugment> catalog)
        {
            foreach (var rawPart in value.Split(
                new[] { '；', ';', ',', '，', '、', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var name = rawPart.Trim().TrimEnd('。', '.');
                var tier = string.Empty;
                var rarity = defaultRarity;
                var metaStart = name.LastIndexOf('（');
                var metaEnd = name.LastIndexOf('）');

                if (metaStart >= 0 && metaEnd > metaStart)
                {
                    var meta = name[(metaStart + 1)..metaEnd].Split('/', StringSplitOptions.TrimEntries);
                    tier = meta.Length > 0 ? meta[0] : string.Empty;
                    rarity = meta.Length > 1 ? meta[1] : rarity;
                    name = name[..metaStart].Trim();
                }

                if (catalog.TryGetValue(name, out var augment))
                {
                    tier = string.IsNullOrWhiteSpace(augment.Tier) ? tier : augment.Tier!;
                    rarity = string.IsNullOrWhiteSpace(augment.Rarity) ? rarity : augment.Rarity;
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    yield return new LolAramGuideAugmentBadge(name, tier, rarity);
                }
            }
        }

        private sealed record SectionLabel(string Name, string Rarity);

        private sealed record SectionMarker(int LabelStart, int ContentStart, string Rarity);

        private sealed record SectionSource(string Text, string Rarity);
    }
}
