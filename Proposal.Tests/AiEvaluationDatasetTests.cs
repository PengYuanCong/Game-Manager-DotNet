using System.Text.Json;

namespace Proposal.Tests;

public sealed class AiEvaluationDatasetTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Dataset_ContainsThirtyUniqueCases()
    {
        var cases = LoadCases();

        Assert.Equal(30, cases.Count);
        Assert.Equal(cases.Count, cases.Select(testCase => testCase.Id).Distinct().Count());
    }

    [Fact]
    public void Dataset_UsesValidStageProgressionAndThreeChoices()
    {
        var expectedPreviousCounts = new Dictionary<string, int>
        {
            ["開局"] = 0,
            ["7 等"] = 1,
            ["11 等"] = 2,
            ["15 等"] = 3
        };

        foreach (var testCase in LoadCases())
        {
            Assert.True(
                expectedPreviousCounts.TryGetValue(testCase.Stage, out var previousCount),
                $"Unknown stage in {testCase.Id}: {testCase.Stage}");
            Assert.Equal(previousCount, testCase.PreviousAugments.Count);
            Assert.Equal(3, testCase.OfferedAugments.Count);
            Assert.Equal(3, testCase.OfferedAugments.Distinct().Count());
        }
    }

    [Fact]
    public void Dataset_DefinesActionableExpectedRules()
    {
        foreach (var testCase in LoadCases())
        {
            Assert.False(string.IsNullOrWhiteSpace(testCase.Champion));
            Assert.False(string.IsNullOrWhiteSpace(testCase.RoleCategory));
            Assert.False(string.IsNullOrWhiteSpace(testCase.Notes));
            Assert.True(testCase.RequiredConcepts.Count >= 3, $"{testCase.Id} requires at least three concepts.");
            Assert.True(testCase.AcceptableItems.Count >= 4, $"{testCase.Id} requires at least four acceptable items.");
            Assert.Contains("守護天使", testCase.ForbiddenItems);
            Assert.NotEmpty(testCase.ForbiddenTerms);
            Assert.Empty(testCase.AcceptableItems.Intersect(testCase.ForbiddenItems));
        }
    }

    [Fact]
    public void Dataset_UsesTraditionalChineseItemNames()
    {
        var forbiddenFragments = new[]
        {
            "Guardian Angel",
            "Blackfire Torch",
            "Liandry",
            "Zhonya",
            "Rabadon",
            "守护",
            "兰德里"
        };

        foreach (var testCase in LoadCases())
        {
            foreach (var item in testCase.AcceptableItems)
            {
                Assert.DoesNotContain(
                    forbiddenFragments,
                    fragment => item.Contains(fragment, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void Dataset_AllowsExhaustOnlyWhenBurnItUpIsPresent()
    {
        foreach (var testCase in LoadCases().Where(testCase => testCase.AllowExhaust))
        {
            var augments = testCase.PreviousAugments.Concat(testCase.OfferedAugments);

            Assert.Contains(
                augments,
                augment => augment.Contains("燒起來", StringComparison.Ordinal));
        }
    }

    private static IReadOnlyList<AiEvaluationCase> LoadCases()
    {
        var path = FindRepositoryFile("evaluation", "aram-recommendation-cases.json");
        var cases = JsonSerializer.Deserialize<List<AiEvaluationCase>>(
            File.ReadAllText(path),
            JsonOptions);

        return cases ?? throw new InvalidDataException("AI evaluation dataset is empty.");
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"Unable to find repository file: {Path.Combine(segments)}");
    }

    private sealed class AiEvaluationCase
    {
        public string Id { get; init; } = string.Empty;
        public string Champion { get; init; } = string.Empty;
        public string RoleCategory { get; init; } = string.Empty;
        public string Stage { get; init; } = string.Empty;
        public List<string> PreviousAugments { get; init; } = [];
        public List<string> OfferedAugments { get; init; } = [];
        public string Notes { get; init; } = string.Empty;
        public List<string> RequiredConcepts { get; init; } = [];
        public List<string> AcceptableItems { get; init; } = [];
        public List<string> ForbiddenItems { get; init; } = [];
        public List<string> ForbiddenTerms { get; init; } = [];
        public bool AllowExhaust { get; init; }
    }
}
