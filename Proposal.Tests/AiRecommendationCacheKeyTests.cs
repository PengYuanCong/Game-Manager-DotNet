using Proposal.Models;
using Proposal.Services;

namespace Proposal.Tests;

public sealed class AiRecommendationCacheKeyTests
{
    [Fact]
    public void Create_NormalizesWhitespaceAndLetterCase()
    {
        var first = CreateInput("  Seraphine  ", " 7 等 ");
        var second = CreateInput("seraphine", "7 等");

        var firstKey = AiRecommendationCacheKey.Create(first, " GUIDE:SERAPHINE ");
        var secondKey = AiRecommendationCacheKey.Create(second, "guide:seraphine");

        Assert.Equal(firstKey, secondKey);
    }

    [Fact]
    public void Create_SeparatesDifferentKnowledgeScopes()
    {
        var input = CreateInput("瑟拉芬", "7 等");

        var firstKey = AiRecommendationCacheKey.Create(input, "guide:v1");
        var secondKey = AiRecommendationCacheKey.Create(input, "guide:v2");

        Assert.NotEqual(firstKey, secondKey);
    }

    [Fact]
    public void Create_ChangesWhenChampionChanges()
    {
        var firstKey = AiRecommendationCacheKey.Create(CreateInput("瑟拉芬", "7 等"));
        var secondKey = AiRecommendationCacheKey.Create(CreateInput("斯溫", "7 等"));

        Assert.NotEqual(firstKey, secondKey);
    }

    [Fact]
    public void Create_ReturnsLowercaseSha256Hex()
    {
        var key = AiRecommendationCacheKey.Create(CreateInput("布蘭德", "開局"));

        Assert.Equal(64, key.Length);
        Assert.Matches("^[0-9a-f]{64}$", key);
    }

    [Fact]
    public void Create_TreatsBlankOptionalValuesConsistently()
    {
        var first = CreateInput("布蘭德", "開局");
        first.AvailableItems = null;
        var second = CreateInput("布蘭德", "開局");
        second.AvailableItems = "  ";

        Assert.Equal(
            AiRecommendationCacheKey.Create(first),
            AiRecommendationCacheKey.Create(second));
    }

    private static AiRecommendationInput CreateInput(string champion, string stage)
    {
        return new AiRecommendationInput
        {
            GameTitle = "英雄聯盟 隨機單中大亂鬥",
            CoreChampion = champion,
            CurrentStage = stage,
            Augment = "阿嬤的辣油",
            Notes = "需要團隊護盾"
        };
    }
}
