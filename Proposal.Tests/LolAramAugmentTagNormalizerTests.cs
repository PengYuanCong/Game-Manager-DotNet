using Proposal.Services;

namespace Proposal.Tests;

public sealed class LolAramAugmentTagNormalizerTests
{
    [Theory]
    [InlineData("爆竹系列", "firecracker")]
    [InlineData("疊層暴龍", "stackosaurus")]
    [InlineData("堆疊暴龍", "stackosaurus")]
    [InlineData("下雪天", "snowday")]
    [InlineData("雪球", "snowday")]
    [InlineData("完全自動化", "automation")]
    public void NormalizeSeriesKey_MapsKnownAliases(string input, string expected)
    {
        Assert.Equal(expected, LolAramAugmentTagNormalizer.NormalizeSeriesKey(input));
    }

    [Theory]
    [InlineData("成長", "stackosaurus")]
    [InlineData("疊層", "stackosaurus")]
    [InlineData("治療", "healing")]
    [InlineData("魔法傷害", "magic_damage")]
    [InlineData("技能急速", "ability_haste")]
    [InlineData("隊友增益", "team_support")]
    public void NormalizeTag_MapsDisplayLabelsToCanonicalKeys(string input, string expected)
    {
        Assert.Equal(expected, LolAramAugmentTagNormalizer.NormalizeTag(input));
    }

    [Fact]
    public void NormalizeTags_DeduplicatesAliases()
    {
        var tags = LolAramAugmentTagNormalizer.NormalizeTags("治療; heal; 回復");

        Assert.Equal("healing", tags);
    }

    [Fact]
    public void NormalizeTags_AddsSeriesTag()
    {
        var tags = LolAramAugmentTagNormalizer.NormalizeTags(null, seriesKey: "爆竹系列");

        Assert.Equal("firecracker", tags);
    }

    [Fact]
    public void NormalizeTags_InfersTagsFromEffectText()
    {
        var tags = LolAramAugmentTagNormalizer.NormalizeTags(
            null,
            "對友軍施加護盾時獲得移動速度，並縮短技能冷卻時間。");

        Assert.Contains("shield", tags);
        Assert.Contains("movement_speed", tags);
        Assert.Contains("ability_haste", tags);
        Assert.Contains("team_support", tags);
    }

    [Fact]
    public void NormalizeTag_PreservesUnknownCanonicalValue()
    {
        Assert.Equal("custom_tag", LolAramAugmentTagNormalizer.NormalizeTag(" custom_tag "));
    }

    [Fact]
    public void NormalizeTags_ReturnsNullForBlankInput()
    {
        Assert.Null(LolAramAugmentTagNormalizer.NormalizeTags("  "));
    }
}
