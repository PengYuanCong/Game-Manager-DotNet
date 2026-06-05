internal static class AugmentSeriesMigrationNormalizerTest
{
    public static int Run()
    {
        var expectedMappings = new Dictionary<string, string>
        {
            ["firecracker"] = "firecracker",
            ["stacking_dino"] = "stackosaurus",
            ["堆疊暴龍"] = "stackosaurus",
            ["雪球"] = "snowday",
            ["自爆"] = "self_destruct",
            ["嗚咿嗚咿"] = "low_health_ally",
            ["大法師"] = "archmage",
            ["全自動"] = "automation",
            ["土豪賭客"] = "high_roller",
            ["錢如雨下"] = "coinrain"
        };

        foreach (var (source, expected) in expectedMappings)
        {
            Assert(
                AugmentSeriesMigrationNormalizer.NormalizePrimary(source) == expected,
                $"Expected '{source}' to normalize to '{expected}'.");
        }

        Assert(
            AugmentSeriesMigrationNormalizer.NormalizePrimary("自爆、全自動") == "self_destruct",
            "Self-destruct must remain the primary series for the dual-series augment.");
        Assert(
            AugmentSeriesMigrationNormalizer.NormalizeAll("自爆、全自動")
                .SequenceEqual(["self_destruct", "automation"]),
            "The automation secondary series must be preserved for the dual-series augment.");
        Assert(
            AugmentSeriesMigrationNormalizer.NormalizeAll("嗚咿嗚咿、全自動")
                .SequenceEqual(["low_health_ally", "automation"]),
            "The support and automation series must both be recognized.");
        Assert(
            AugmentSeriesMigrationNormalizer.SecondarySeriesArePreserved(
                "自爆、全自動",
                "true_damage; self_destruct; automation"),
            "Secondary series tags should preserve the dual-series relationship.");
        Assert(
            !AugmentSeriesMigrationNormalizer.SecondarySeriesArePreserved(
                "自爆、全自動",
                "true_damage; self_destruct"),
            "A missing secondary series tag must be rejected.");

        var definitionKeys = AugmentSeriesMigrationNormalizer.Definitions
            .Select(definition => definition.Key)
            .ToHashSet(StringComparer.Ordinal);
        Assert(definitionKeys.Count == 9, "Exactly nine curated series definitions are required.");
        Assert(
            expectedMappings.Values.All(definitionKeys.Contains),
            "Every normalized series key must have a parent definition.");

        AssertThrowsUnknown("not-a-real-series");

        var seriesSpec = TableSpecs.All.Single(spec => spec.Name == "lol_aram_augment_series");
        var augmentsSpec = TableSpecs.All.Single(spec => spec.Name == "lol_aram_augments");
        var rulesSpec = TableSpecs.All.Single(spec => spec.Name == "lol_aram_synergy_rules");

        AssertSeriesMapping(seriesSpec, "stacking_dino", "stackosaurus");
        AssertSeriesMapping(augmentsSpec, "自爆、全自動", "self_destruct");
        AssertSeriesMapping(rulesSpec, "錢如雨下", "coinrain");

        Console.WriteLine("SUPABASE_AUGMENT_SERIES_MIGRATION_TEST=PASS");
        return 0;
    }

    private static void AssertSeriesMapping(TableSpec spec, string source, string expected)
    {
        var column = spec.Columns.Single(item => item.Target == "series_key");
        Assert(column.ValueNormalizer is not null, $"{spec.Name}.series_key must use the migration normalizer.");
        Assert(
            Convert.ToString(column.ValueNormalizer!(source)) == expected,
            $"{spec.Name}.series_key did not normalize '{source}' to '{expected}'.");
    }

    private static void AssertThrowsUnknown(string value)
    {
        try
        {
            AugmentSeriesMigrationNormalizer.NormalizePrimary(value);
            throw new InvalidOperationException("Expected an unknown series key to be rejected.");
        }
        catch (InvalidOperationException exception)
            when (exception.Message != "Expected an unknown series key to be rejected.")
        {
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
