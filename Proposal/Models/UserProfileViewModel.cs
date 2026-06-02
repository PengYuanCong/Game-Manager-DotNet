namespace Proposal.Models
{
    public class UserProfileViewModel
    {
        public string Username { get; set; } = string.Empty;

        public string DbInfo { get; set; } = string.Empty;

        public IReadOnlyList<UserActivityLog> RecentActivities { get; set; } = Array.Empty<UserActivityLog>();

        public IReadOnlyList<AiRecommendationFavorite> RecommendationFavorites { get; set; } = Array.Empty<AiRecommendationFavorite>();

        public IReadOnlyList<CalculationHistory> RecentCalculations { get; set; } = Array.Empty<CalculationHistory>();
    }
}
