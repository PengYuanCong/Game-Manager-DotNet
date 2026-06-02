using Proposal.Models;

namespace Proposal.Services
{
    public interface IAiRecommendationFavoriteService
    {
        Task<int> SaveAsync(
            string username,
            AiRecommendationInput input,
            GameRecommendation recommendation,
            CancellationToken cancellationToken = default);

        Task<int> AdoptAsync(
            string username,
            AiRecommendationInput input,
            GameRecommendation recommendation,
            CancellationToken cancellationToken = default);

        Task<AiRecommendationFavorite?> GetAsync(
            string username,
            int id,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<AiRecommendationFavorite>> GetRecentAsync(
            string username,
            int limit,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<AiRecommendationFavorite>> FindRelevantAsync(
            string username,
            AiRecommendationInput input,
            int limit,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<AiRecommendationFavorite>> FindCommunityAcceptedAsync(
            AiRecommendationInput input,
            int limit,
            CancellationToken cancellationToken = default);
    }
}
