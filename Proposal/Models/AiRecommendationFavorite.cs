namespace Proposal.Models
{
    public class AiRecommendationFavorite
    {
        public int Id { get; set; }

        public string Username { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string GameTitle { get; set; } = string.Empty;

        public string CoreChampion { get; set; } = string.Empty;

        public string? CurrentStage { get; set; }

        public string? Augment { get; set; }

        public string? AvailableItems { get; set; }

        public string? Notes { get; set; }

        public string Summary { get; set; } = string.Empty;

        public string RecommendedItems { get; set; } = string.Empty;

        public string RecommendedAugments { get; set; } = string.Empty;

        public string InputJson { get; set; } = string.Empty;

        public string RecommendationJson { get; set; } = string.Empty;

        public int AdoptedCount { get; set; }

        public DateTime? LastAdoptedAt { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public AiRecommendationInput ToInput()
        {
            return new AiRecommendationInput
            {
                GameTitle = GameTitle,
                CoreChampion = CoreChampion,
                CurrentStage = CurrentStage,
                Augment = Augment,
                AvailableItems = AvailableItems,
                Notes = Notes
            };
        }
    }
}
