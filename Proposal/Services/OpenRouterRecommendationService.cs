using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Proposal.Models;

namespace Proposal.Services
{
    public class OpenRouterRecommendationService : IAiRecommendationService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        private readonly HttpClient _httpClient;
        private readonly OpenRouterOptions _options;

        public OpenRouterRecommendationService(HttpClient httpClient, IOptionsMonitor<OpenRouterOptions> options)
        {
            _httpClient = httpClient;
            _options = options.CurrentValue;

            var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
                ? "https://openrouter.ai/api/v1/"
                : _options.BaseUrl;

            if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
            {
                baseUrl += "/";
            }

            _httpClient.BaseAddress = new Uri(baseUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 10, 240));
        }

        public async Task<GameRecommendation> CreateRecommendationAsync(
            AiRecommendationInput input,
            AiKnowledgeContext knowledgeContext,
            CancellationToken cancellationToken = default)
        {
            var apiKey = ResolveApiKey();
            var outputText = await SendRecommendationRequestAsync(
                apiKey,
                input,
                knowledgeContext,
                forceConcise: false,
                maxTokens: _options.MaxTokens,
                cancellationToken);

            GameRecommendation recommendation;
            try
            {
                recommendation = DeserializeRecommendation(outputText);
            }
            catch (JsonException firstException)
            {
                var retryOutputText = await SendRecommendationRequestAsync(
                    apiKey,
                    input,
                    knowledgeContext,
                    forceConcise: true,
                    maxTokens: Math.Max(_options.MaxTokens, 3200),
                    cancellationToken);

                try
                {
                    recommendation = DeserializeRecommendation(retryOutputText);
                }
                catch (JsonException retryException)
                {
                    throw new JsonException(
                        $"OpenRouter 連續兩次回傳無法解析的 JSON。第一次錯誤：{firstException.Message}；重試錯誤：{retryException.Message}",
                        retryException);
                }
            }

            NormalizeRecommendation(recommendation);
            recommendation.CacheKey = AiRecommendationCacheKey.Create(input, knowledgeContext.CacheScope);
            return recommendation;
        }

        private async Task<string> SendRecommendationRequestAsync(
            string apiKey,
            AiRecommendationInput input,
            AiKnowledgeContext knowledgeContext,
            bool forceConcise,
            int maxTokens,
            CancellationToken cancellationToken)
        {
            var payload = new
            {
                model = _options.Model,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = BuildSystemPrompt(forceConcise)
                    },
                    new
                    {
                        role = "user",
                        content = BuildUserPrompt(input, knowledgeContext, forceConcise)
                    }
                },
                response_format = CreateRecommendationResponseFormat(),
                provider = new
                {
                    require_parameters = true
                },
                max_tokens = Math.Clamp(maxTokens, 800, 6000),
                temperature = _options.Temperature,
                stream = false
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            AddOptionalOpenRouterHeaders(request);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"OpenRouter API 呼叫失敗 ({(int)response.StatusCode})：{ExtractErrorMessage(responseJson)}");
            }

            var outputText = ExtractMessageContent(responseJson);
            if (string.IsNullOrWhiteSpace(outputText))
            {
                throw new JsonException("OpenRouter 回應缺少 choices[0].message.content。");
            }

            return outputText;
        }

        private static GameRecommendation DeserializeRecommendation(string outputText)
        {
            return JsonSerializer.Deserialize<GameRecommendation>(NormalizeJsonContent(outputText), JsonOptions)
                ?? throw new JsonException("OpenRouter JSON 回應無法解析成推薦結果。");
        }

        private static void NormalizeRecommendation(GameRecommendation recommendation)
        {
            foreach (var item in recommendation.RecommendedItems)
            {
                item.ItemName = LolItemNameNormalizer.Normalize(item.ItemName);
            }
        }

        private static string NormalizeJsonContent(string outputText)
        {
            var trimmed = outputText.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var firstLineEnd = trimmed.IndexOf('\n');
                if (firstLineEnd >= 0)
                {
                    trimmed = trimmed.Substring(firstLineEnd + 1).Trim();
                }

                if (trimmed.EndsWith("```", StringComparison.Ordinal))
                {
                    trimmed = trimmed.Substring(0, trimmed.Length - 3).Trim();
                }
            }

            var objectStart = trimmed.IndexOf('{');
            var objectEnd = trimmed.LastIndexOf('}');
            if (objectStart > 0 && objectEnd > objectStart)
            {
                return trimmed.Substring(objectStart, objectEnd - objectStart + 1);
            }

            return trimmed;
        }

        private string ResolveApiKey()
        {
            var apiKey = string.IsNullOrWhiteSpace(_options.ApiKey)
                ? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
                : _options.ApiKey;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "尚未設定 OpenRouter API Key。請使用 user-secrets 設定 OpenRouter:ApiKey，或設定 OPENROUTER_API_KEY 環境變數。");
            }

            return apiKey;
        }

        private void AddOptionalOpenRouterHeaders(HttpRequestMessage request)
        {
            if (!string.IsNullOrWhiteSpace(_options.SiteUrl))
            {
                request.Headers.TryAddWithoutValidation("HTTP-Referer", _options.SiteUrl);
            }

            if (!string.IsNullOrWhiteSpace(_options.SiteName))
            {
                request.Headers.TryAddWithoutValidation("X-OpenRouter-Title", _options.SiteName);
            }
        }

        private static string BuildSystemPrompt(bool forceConcise)
        {
            var prompt = """
                你是英雄聯盟「隨機單中大亂鬥」的中文配裝與海克斯推薦助手。
                請一律使用繁體中文回答。
                你要根據玩家輸入與本機知識庫，推薦裝備、海克斯方向、站位與團戰打法。
                如果本機知識庫提供該英雄攻略，請把它視為主要依據。
                不要捏造裝備效果、版本資訊或未出現在本機知識庫/玩家輸入中的 meta 結論。
                如果缺少該英雄的本機攻略，請明確表示這是低信心的一般建議。
                回覆只能是符合 JSON schema 的 JSON，不要使用 Markdown。
                每個字串都要精簡，summary、stagePlan、reason、tip 最多一句話。
                海克斯推薦要標出稜彩、黃金、白銀與 S/A/B/C/D/E 評級；若本機資料不足，請用英雄特性推論並保守表達。
                """;

            prompt += """

                Hard ARAM Mayhem constraints:
                - Do not recommend Guardian Angel / 守護天使; it is not available in this ARAM Mayhem build context.
                - Do not recommend Exhaust / 虛弱 as a normal summoner spell. Mention Exhaust only if the player explicitly says a special augment or effect grants it, such as 燒起來了.
                - Item names must be Taiwan Traditional Chinese names. Do not output English item names, Simplified Chinese item names, or mixed names.
                - ARAM Mayhem augments are sequential decisions. For level 7, 11, and 15 choices, evaluate previously selected augments first, then rank the current three choices.
                - A current augment roll normally has three choices of the same rarity. Only treat the roll as upgraded when the player marks 黃金重抽升階 or explicitly says the roll was upgraded.
                - If the user provides "本階段三選一", recommendedAugments must rank only those three current choices, with the best current pick first. Do not add outside augments to that list.
                - Player accepted feedback is weighted evidence, but champion identity and actual item availability still override it.
                - If feedback conflicts with the local champion guide or available items, explain the safer local guide route.
                """;

            if (!forceConcise)
            {
                return prompt;
            }

            return prompt + """

                你的上一版回覆可能讓 JSON 無法解析或被截斷。請只回傳更短、有效的 JSON 物件。
                不要加入額外解釋、註解、Markdown 或結尾文字。
                """;
        }

        private static string BuildUserPrompt(
            AiRecommendationInput input,
            AiKnowledgeContext knowledgeContext,
            bool forceConcise)
        {
            var prompt = $"""
                遊戲模式：{TextOrDefault(input.GameTitle)}
                英雄：{TextOrDefault(input.CoreChampion)}
                對局階段：{TextOrDefault(input.CurrentStage)}
                目前海克斯 / 符文 / 特殊選項：{TextOrDefault(input.Augment)}
                裝備需求：玩家未預填裝備，請你根據英雄、海克斯三選一與對局狀況直接推薦。
                對局狀況、問題或補充：{TextOrDefault(input.Notes)}

                本機知識庫：
                {knowledgeContext.PromptContext}

                若玩家已輸入「本階段三選一」，請把 recommendedAugments 當成本輪三選一排序，只回傳這三個候選並把最佳解放第一。
                若玩家沒有輸入本階段三選一，才回傳 5 到 6 個泛用海克斯方向，並盡量涵蓋稜彩、黃金、白銀。
                請回傳 3 件下一步或最終裝備、海克斯判斷、英雄特性方向、站位提醒與 ARAM 團戰打法。
                裝備名稱必須使用台服繁體中文名稱，例如黑焰火炬、蘭德里的折磨、惡意、中婭沙漏、瑞萊的冰晶節杖；不可輸出英文、簡中或中英混合名稱。
                每個海克斯都要有 S/A/B/C/D/E 評級與一句理由。
                摘要請說明是否使用了本機整理過的英雄知識。
                """;

            if (!forceConcise)
            {
                return prompt;
            }

            return prompt + """

                精簡模式：
                - 裝備固定 3 件。
                - 若有本階段三選一，海克斯固定只回 3 個候選排序；否則回 5 到 6 個泛用候選。
                - 英雄方向最多 3 筆。
                - 站位提醒最多 3 筆。
                - 團戰提示最多 3 筆。
                - 每個字串都要短到能放進畫面卡片。
                """;
        }

        private static object CreateRecommendationResponseFormat()
        {
            return new Dictionary<string, object?>
            {
                ["type"] = "json_schema",
                ["json_schema"] = new Dictionary<string, object?>
                {
                    ["name"] = "game_build_recommendation",
                    ["strict"] = true,
                    ["schema"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new[]
                        {
                            "summary",
                            "coreChampion",
                            "stagePlan",
                            "recommendedItems",
                            "recommendedAugments",
                            "teamTraits",
                            "positioningTips",
                            "gameplayTips",
                            "confidence",
                            "cacheKey"
                        },
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["summary"] = new Dictionary<string, object?> { ["type"] = "string", ["maxLength"] = 180 },
                            ["coreChampion"] = new Dictionary<string, object?> { ["type"] = "string", ["maxLength"] = 80 },
                            ["stagePlan"] = new Dictionary<string, object?> { ["type"] = "string", ["maxLength"] = 220 },
                            ["recommendedItems"] = new Dictionary<string, object?>
                            {
                                ["type"] = "array",
                                ["minItems"] = 3,
                                ["maxItems"] = 3,
                                ["items"] = new Dictionary<string, object?>
                                {
                                    ["type"] = "object",
                                    ["additionalProperties"] = false,
                                    ["required"] = new[] { "itemName", "priority", "reason" },
                                    ["properties"] = new Dictionary<string, object?>
                                    {
                                        ["itemName"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "string",
                                            ["maxLength"] = 80,
                                            ["description"] = "台服繁體中文裝備名稱，不可使用英文、簡中或中英混合名稱。"
                                        },
                                        ["priority"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "string",
                                            ["enum"] = new[] { "core", "alternative", "situational" }
                                        },
                                        ["reason"] = new Dictionary<string, object?> { ["type"] = "string", ["maxLength"] = 180 }
                                    }
                                }
                            },
                            ["recommendedAugments"] = new Dictionary<string, object?>
                            {
                                ["type"] = "array",
                                ["minItems"] = 3,
                                ["maxItems"] = 6,
                                ["items"] = new Dictionary<string, object?>
                                {
                                    ["type"] = "object",
                                    ["additionalProperties"] = false,
                                    ["required"] = new[] { "augmentName", "rarity", "tier", "reason" },
                                    ["properties"] = new Dictionary<string, object?>
                                    {
                                        ["augmentName"] = new Dictionary<string, object?> { ["type"] = "string", ["maxLength"] = 80 },
                                        ["rarity"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "string",
                                            ["enum"] = new[] { "稜彩", "黃金", "白銀", "未知" }
                                        },
                                        ["tier"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "string",
                                            ["enum"] = new[] { "S", "A", "B", "C", "D", "E", "未定" }
                                        },
                                        ["reason"] = new Dictionary<string, object?> { ["type"] = "string", ["maxLength"] = 180 }
                                    }
                                }
                            },
                            ["teamTraits"] = new Dictionary<string, object?>
                            {
                                ["type"] = "array",
                                ["maxItems"] = 3,
                                ["items"] = new Dictionary<string, object?>
                                {
                                    ["type"] = "object",
                                    ["additionalProperties"] = false,
                                    ["required"] = new[] { "traitName", "reason" },
                                    ["properties"] = new Dictionary<string, object?>
                                    {
                                        ["traitName"] = new Dictionary<string, object?> { ["type"] = "string", ["maxLength"] = 80 },
                                        ["reason"] = new Dictionary<string, object?> { ["type"] = "string", ["maxLength"] = 180 }
                                    }
                                }
                            },
                            ["positioningTips"] = new Dictionary<string, object?>
                            {
                                ["type"] = "array",
                                ["maxItems"] = 3,
                                ["items"] = new Dictionary<string, object?> { ["type"] = "string", ["maxLength"] = 180 }
                            },
                            ["gameplayTips"] = new Dictionary<string, object?>
                            {
                                ["type"] = "array",
                                ["maxItems"] = 3,
                                ["items"] = new Dictionary<string, object?> { ["type"] = "string", ["maxLength"] = 180 }
                            },
                            ["confidence"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string",
                                ["enum"] = new[] { "high", "medium", "low" }
                            },
                            ["cacheKey"] = new Dictionary<string, object?> { ["type"] = "string", ["maxLength"] = 64 }
                        }
                    }
                }
            };
        }

        private static string? ExtractMessageContent(string responseJson)
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;

            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return null;
            }

            var firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out var message)
                || !message.TryGetProperty("content", out var content))
            {
                return null;
            }

            if (content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }

            return content.GetRawText();
        }

        private static string ExtractErrorMessage(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return "沒有回傳錯誤內容。";
            }

            try
            {
                using var document = JsonDocument.Parse(responseJson);
                if (document.RootElement.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? "未知錯誤。";
                }

                if (document.RootElement.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind == JsonValueKind.String)
                    {
                        return error.GetString() ?? "未知錯誤。";
                    }

                    if (error.TryGetProperty("message", out var errorMessage))
                    {
                        return errorMessage.GetString() ?? "未知錯誤。";
                    }
                }
            }
            catch (JsonException)
            {
                // Fall through to the trimmed raw response.
            }

            return responseJson.Length <= 500 ? responseJson : responseJson.Substring(0, 500);
        }

        private static string TextOrDefault(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "未提供" : value.Trim();
        }

    }
}
