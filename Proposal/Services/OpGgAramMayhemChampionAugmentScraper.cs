using System.Net;
using System.Text.RegularExpressions;
using Proposal.Models;

namespace Proposal.Services
{
    public class OpGgAramMayhemChampionAugmentScraper : IOpGgAramMayhemChampionAugmentScraper
    {
        public const string DefaultSourceUrl = "https://op.gg/zh-tw/lol/modes/aram-mayhem";
        private const int RequestDelayMilliseconds = 650;
        private const int MaxRecommendedAugments = 12;

        private static readonly Regex HeroLinkRegex = new(
            "<a[^>]+href=\"/zh-tw/lol/modes/aram-mayhem/([^/\"#]+)/build\"[^>]*>.*?champion/([^/\"?.]+)\\.png.*?<span[^>]*>([^<]+)</span>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex AugmentRowRegex = new(
            "<li class=\"flex w-full items-center.*?</li>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex AugmentNameRegex = new(
            "<img alt=\"([^\"]+)\"[^>]*aram-augment/",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex FillRegex = new(
            "<path fill=\"([^\"]+)\"",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex VersionRegex = new(
            "meta/images/lol/([0-9]+\\.[0-9]+(?:\\.[0-9]+)?)/champion",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly HttpClient _httpClient;

        public OpGgAramMayhemChampionAugmentScraper(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<IReadOnlyList<LolAramGuide>> ScrapeChampionAugmentGuidesAsync(
            string sourceUrl,
            int startIndex,
            int maxHeroes,
            CancellationToken cancellationToken = default)
        {
            var url = NormalizeAndValidateSourceUrl(sourceUrl);
            var html = await DownloadStringAsync(url, cancellationToken);
            var heroes = ParseHeroes(html);
            if (heroes.Count == 0)
            {
                throw new InvalidOperationException("OP.GG 主頁格式可能已改變，找不到英雄清單。");
            }

            var normalizedStartIndex = Math.Max(startIndex, 1);
            if (normalizedStartIndex > heroes.Count)
            {
                throw new InvalidOperationException($"起始順位 {normalizedStartIndex} 超過 OP.GG 目前解析到的英雄總數 {heroes.Count}。");
            }

            heroes = heroes.Skip(normalizedStartIndex - 1).ToList();
            if (maxHeroes > 0)
            {
                heroes = heroes.Take(maxHeroes).ToList();
            }

            var guides = new List<LolAramGuide>();
            foreach (var hero in heroes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var augmentUrl = new Uri(url, $"/zh-tw/lol/modes/aram-mayhem/{hero.Slug}/augments");
                var augmentHtml = await DownloadStringAsync(augmentUrl, cancellationToken);
                var augmentRecommendations = ParseAugments(augmentHtml).Take(MaxRecommendedAugments).ToList();
                if (augmentRecommendations.Count == 0)
                {
                    continue;
                }

                guides.Add(CreateGuide(hero, augmentRecommendations, augmentUrl, FindPatchVersion(augmentHtml)));
                await Task.Delay(RequestDelayMilliseconds, cancellationToken);
            }

            if (guides.Count == 0)
            {
                throw new InvalidOperationException("已讀取 OP.GG 英雄頁，但沒有解析到可用的推薦海克斯。");
            }

            return guides;
        }

        private async Task<string> DownloadStringAsync(Uri url, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124 Safari/537.36");
            request.Headers.AcceptLanguage.ParseAdd("zh-TW,zh;q=0.9,en;q=0.8");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode == 429)
            {
                throw new HttpRequestException("OP.GG 暫時限制請求，請等幾分鐘後再從中斷的起始順位繼續匯入。");
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
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

        private static List<HeroSource> ParseHeroes(string html)
        {
            var heroes = new Dictionary<string, HeroSource>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in HeroLinkRegex.Matches(html))
            {
                var slug = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
                if (heroes.ContainsKey(slug))
                {
                    continue;
                }

                heroes[slug] = new HeroSource(
                    slug,
                    WebUtility.HtmlDecode(match.Groups[2].Value).Trim(),
                    WebUtility.HtmlDecode(match.Groups[3].Value).Trim());
            }

            return heroes.Values.OrderBy(hero => hero.Slug, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<AugmentSource> ParseAugments(string html)
        {
            var augments = new List<AugmentSource>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match row in AugmentRowRegex.Matches(html))
            {
                var name = FindFirst(AugmentNameRegex, row.Value);
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                {
                    continue;
                }

                augments.Add(new AugmentSource(
                    WebUtility.HtmlDecode(name).Trim(),
                    "未知",
                    InferTier(FindFirst(FillRegex, row.Value))));
            }

            return augments;
        }

        private static LolAramGuide CreateGuide(
            HeroSource hero,
            IReadOnlyList<AugmentSource> augments,
            Uri augmentUrl,
            string patchVersion)
        {
            var augmentText = string.Join("；", augments.Select(augment =>
                $"{augment.Name}（{augment.Tier}/{augment.Rarity}）"));

            return new LolAramGuide
            {
                ChampionKey = hero.Slug,
                ChampionName = hero.ChampionName,
                LocalizedName = hero.LocalizedName,
                ModeName = "ARAM Mayhem",
                PatchVersion = patchVersion,
                RoleSummary = "此英雄的推薦海克斯由 OP.GG 公開頁面匯入，尚未完成人工定位整理。",
                CoreItems = "待人工補齊",
                SituationalItems = "待人工補齊",
                Augments = $"OP.GG 推薦海克斯（依頁面順序）：{augmentText}",
                SummonerSpells = "待人工補齊",
                SkillOrder = "待人工補齊",
                PlaystyleTips = "先依英雄技能型態判斷海克斯：技能型重視技能觸發，普攻型重視攻速與普攻，坦克重視生存與開戰，輔助重視護盾治療與團隊增益。",
                PositioningTips = "待人工補齊",
                Weaknesses = "待人工補齊",
                SourceUrl = augmentUrl.ToString(),
                Notes = "由管理員手動從 OP.GG 批次匯入；顏色分組若未能由頁面穩定解析，暫以未知標記。"
            };
        }

        private static string InferTier(string fill)
        {
            return fill switch
            {
                var value when value.StartsWith("url(", StringComparison.OrdinalIgnoreCase) => "S",
                "#EB9C00" => "A",
                "#9AA4AF" => "B",
                "#907659" => "C",
                "#676678" => "D",
                "#424254" => "E",
                _ => "未定"
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

        private sealed record HeroSource(string Slug, string ChampionName, string LocalizedName);

        private sealed record AugmentSource(string Name, string Rarity, string Tier);
    }
}
