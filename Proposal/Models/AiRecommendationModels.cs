using System.Collections.Generic;

namespace Proposal.Models
{
    public class AiRecommendationPageViewModel
    {
        public AiRecommendationInput Input { get; set; } = new AiRecommendationInput();

        public GameRecommendation? Recommendation { get; set; }

        public bool WasLoadedFromCache { get; set; }

        public bool WasKnowledgeUsed { get; set; }

        public string? KnowledgeSourceLabel { get; set; }

        public string? KnowledgeSourceUrl { get; set; }

        public bool WasLoadedFromFavorite { get; set; }

        public bool WasFavoriteMemoryUsed { get; set; }

        public int? FavoriteId { get; set; }

        public string? ErrorMessage { get; set; }
    }

    public class GameRecommendation
    {
        public string Summary { get; set; } = string.Empty;

        public string CoreChampion { get; set; } = string.Empty;

        public string StagePlan { get; set; } = string.Empty;

        public List<ItemRecommendation> RecommendedItems { get; set; } = new List<ItemRecommendation>();

        public List<AugmentRecommendation> RecommendedAugments { get; set; } = new List<AugmentRecommendation>();

        public List<TraitRecommendation> TeamTraits { get; set; } = new List<TraitRecommendation>();

        public List<string> PositioningTips { get; set; } = new List<string>();

        public List<string> GameplayTips { get; set; } = new List<string>();

        public string Confidence { get; set; } = string.Empty;

        public string CacheKey { get; set; } = string.Empty;
    }

    public class ItemRecommendation
    {
        public string ItemName { get; set; } = string.Empty;

        public string Priority { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;
    }

    public class AugmentRecommendation
    {
        public string AugmentName { get; set; } = string.Empty;

        public string Rarity { get; set; } = string.Empty;

        public string Tier { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;
    }

    public class TraitRecommendation
    {
        public string TraitName { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;
    }
}
