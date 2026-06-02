using System.Net;
using System.Text.RegularExpressions;
using Proposal.Models;

namespace Proposal.Services
{
    public class OpGgAramMayhemAugmentScraper : IOpGgAramMayhemAugmentScraper
    {
        public const string DefaultSourceUrl = "https://op.gg/zh-tw/lol/modes/aram-mayhem";

        private static readonly Regex AugmentCardRegex = new(
            "<li class=\"box-border flex min-h-\\[218px\\].*?</li>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex NameRegex = new(
            "<strong[^>]*>(.*?)</strong>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex EffectRegex = new(
            "<p[^>]*>(.*?)</p>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex ImageRegex = new(
            "aram-augment/([^\"?]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex FillRegex = new(
            "<path fill=\"([^\"]+)\"",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ChampionRegex = new(
            "<img alt=\"([^\"]+)\"[^>]*champion/([^\"?/.]+)\\.png",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex VersionRegex = new(
            "meta/images/lol/([0-9]+\\.[0-9]+(?:\\.[0-9]+)?)/champion",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex HtmlTagRegex = new(
            "<[^>]+>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Dictionary<string, string> KnownAugmentKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            ["GrandmasChiliOil"] = "grandma_spicy_oil",
            ["MagicMissile"] = "magic_missile",
            ["ARAM_PhenomenalEvil"] = "extremely_evil"
        };

        private readonly HttpClient _httpClient;

        public OpGgAramMayhemAugmentScraper(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<IReadOnlyList<LolAramAugment>> ScrapeAugmentsAsync(
            string sourceUrl,
            CancellationToken cancellationToken = default)
        {
            var url = NormalizeAndValidateSourceUrl(sourceUrl);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124 Safari/537.36");
            request.Headers.AcceptLanguage.ParseAdd("zh-TW,zh;q=0.9,en;q=0.8");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var patchVersion = FindPatchVersion(html);
            var cards = AugmentCardRegex.Matches(html);
            if (cards.Count == 0)
            {
                throw new InvalidOperationException("OP.GG 頁面格式可能已改變，找不到 ARAM Mayhem 海克斯卡片。");
            }

            var augments = new List<LolAramAugment>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match card in cards)
            {
                var augment = ParseCard(card.Value, url, patchVersion);
                if (augment == null || !seenKeys.Add(augment.AugmentKey))
                {
                    continue;
                }

                augments.Add(augment);
            }

            if (augments.Count == 0)
            {
                throw new InvalidOperationException("已下載 OP.GG 頁面，但沒有解析到可用的海克斯資料。");
            }

            return augments;
        }

        private static Uri NormalizeAndValidateSourceUrl(string? sourceUrl)
        {
            var candidate = string.IsNullOrWhiteSpace(sourceUrl) ? DefaultSourceUrl : sourceUrl.Trim();
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !uri.Host.EndsWith("op.gg", StringComparison.OrdinalIgnoreCase)
                || !uri.AbsolutePath.Contains("/lol/modes/aram-mayhem", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("只能匯入 OP.GG 的 ARAM Mayhem 頁面。");
            }

            return uri;
        }

        private static LolAramAugment? ParseCard(string cardHtml, Uri sourceUrl, string patchVersion)
        {
            var name = CleanText(FindFirst(NameRegex, cardHtml));
            var effectHtml = FindFirst(EffectRegex, cardHtml);
            var effectText = CleanText(effectHtml);
            var imageName = FindFirst(ImageRegex, cardHtml);
            var augmentKey = CreateAugmentKey(imageName, name);

            if (string.IsNullOrWhiteSpace(augmentKey)
                || string.IsNullOrWhiteSpace(name)
                || string.IsNullOrWhiteSpace(effectText))
            {
                return null;
            }

            var champions = FindRecommendedChampions(cardHtml);
            var tags = InferTags(effectHtml, effectText);
            var seriesKey = InferSeriesKey(effectText);
            var tier = InferTier(FindFirst(FillRegex, cardHtml));
            var notes = champions.Count == 0
                ? "由 OP.GG ARAM Mayhem 公開頁面匯入。"
                : $"OP.GG 推薦英雄：{string.Join("、", champions)}。";

            if (!string.IsNullOrWhiteSpace(seriesKey))
            {
                tags.Add(seriesKey);
                notes += $" 推論系列：{seriesKey}。";
            }

            return new LolAramAugment
            {
                AugmentKey = augmentKey,
                Name = name,
                ModeName = "ARAM Mayhem",
                Rarity = "未知",
                Tier = tier,
                SeriesKey = seriesKey,
                EffectText = effectText,
                Tags = string.Join("; ", tags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)),
                SynergyNotes = notes,
                PatchVersion = patchVersion,
                SourceUrl = sourceUrl.ToString(),
                Notes = $"OP.GG 圖片代碼：{imageName}"
            };
        }

        private static IReadOnlyList<string> FindRecommendedChampions(string cardHtml)
        {
            var champions = new List<string>();
            foreach (Match match in ChampionRegex.Matches(cardHtml))
            {
                var championName = CleanText(match.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(championName)
                    && !champions.Contains(championName, StringComparer.OrdinalIgnoreCase))
                {
                    champions.Add(championName);
                }
            }

            return champions;
        }

        private static SortedSet<string> InferTags(string effectHtml, string effectText)
        {
            var tags = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var lowerHtml = effectHtml.ToLowerInvariant();

            AddTagIf(tags, lowerHtml.Contains("<magicdamage") || effectText.Contains("魔法傷害"), "magic_damage");
            AddTagIf(tags, lowerHtml.Contains("<truedamage") || effectText.Contains("真實傷害"), "true_damage");
            AddTagIf(tags, lowerHtml.Contains("<physicaldamage") || effectText.Contains("物理傷害"), "physical_damage");
            AddTagIf(tags, lowerHtml.Contains("<heal") || effectText.Contains("治療") || effectText.Contains("回復"), "healing");
            AddTagIf(tags, effectText.Contains("護盾"), "shield");
            AddTagIf(tags, effectText.Contains("燃燒"), "burn");
            AddTagIf(tags, effectText.Contains("爆竹"), "firecracker");
            AddTagIf(tags, effectText.Contains("導彈"), "missile");
            AddTagIf(tags, effectText.Contains("大絕") || effectText.Contains("終極技能"), "ultimate");
            AddTagIf(tags, effectText.Contains("普攻") || effectText.Contains("攻擊"), "attack");
            AddTagIf(tags, effectText.Contains("暴擊"), "critical");
            AddTagIf(tags, effectText.Contains("技能加速") || effectText.Contains("冷卻"), "ability_haste");
            AddTagIf(tags, effectText.Contains("移動速度") || effectText.Contains("跑速"), "movement_speed");
            AddTagIf(tags, effectText.Contains("暈眩") || effectText.Contains("定身") || effectText.Contains("緩速") || effectText.Contains("控場"), "crowd_control");
            AddTagIf(tags, effectText.Contains("法力") || effectText.Contains("魔力"), "mana");
            AddTagIf(tags, effectText.Contains("生命"), "health");
            AddTagIf(tags, effectText.Contains("物防") || effectText.Contains("護甲"), "armor");
            AddTagIf(tags, effectText.Contains("魔法防禦") || effectText.Contains("魔防"), "magic_resist");
            AddTagIf(tags, effectText.Contains("金幣"), "coinrain");
            AddTagIf(tags, effectText.Contains("疊層"), "stackosaurus");

            if (tags.Count == 0)
            {
                tags.Add("opgg_imported");
            }

            return tags;
        }

        private static void AddTagIf(ISet<string> tags, bool condition, string tag)
        {
            if (condition)
            {
                tags.Add(tag);
            }
        }

        private static string? InferSeriesKey(string effectText)
        {
            if (effectText.Contains("爆竹"))
            {
                return "firecracker";
            }

            if (effectText.Contains("疊層暴龍"))
            {
                return "stacking_dino";
            }

            return null;
        }

        private static string? InferTier(string fill)
        {
            return fill switch
            {
                var value when value.StartsWith("url(", StringComparison.OrdinalIgnoreCase) => "S",
                "#EB9C00" => "A",
                "#9AA4AF" => "B",
                "#907659" => "C",
                "#676678" => "D",
                "#424254" => "E",
                _ => null
            };
        }

        private static string FindPatchVersion(string html)
        {
            var match = VersionRegex.Match(html);
            return match.Success ? $"op.gg {match.Groups[1].Value}" : "op.gg";
        }

        private static string FindFirst(Regex regex, string value)
        {
            var match = regex.Match(value);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string CleanText(string html)
        {
            var withoutTags = HtmlTagRegex.Replace(html, string.Empty);
            return WebUtility.HtmlDecode(withoutTags).Trim();
        }

        private static string CreateAugmentKey(string imageName, string name)
        {
            var source = string.IsNullOrWhiteSpace(imageName) ? name : imageName;
            source = source
                .Replace("_large", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(".png", string.Empty, StringComparison.OrdinalIgnoreCase);

            var dotIndex = source.IndexOf('.');
            if (dotIndex >= 0)
            {
                source = source[..dotIndex];
            }

            source = source.Trim();
            return KnownAugmentKeys.TryGetValue(source, out var knownKey)
                ? knownKey
                : ToSnakeCase(source);
        }

        private static string ToSnakeCase(string value)
        {
            var withWordBoundaries = Regex.Replace(value, "([A-Z]+)([A-Z][a-z])", "$1_$2");
            withWordBoundaries = Regex.Replace(withWordBoundaries, "([a-z0-9])([A-Z])", "$1_$2");
            return Regex.Replace(withWordBoundaries, "[^\\p{L}\\p{Nd}]+", "_").Trim('_').ToLowerInvariant();
        }
    }
}
