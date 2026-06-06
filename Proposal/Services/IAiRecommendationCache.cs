using Proposal.Models;

namespace Proposal.Services
{
    public interface IAiRecommendationCache
    {
        Task<GameRecommendation?> GetAsync(
            string username,
            AiRecommendationInput input,
            string cacheScope,
            CancellationToken cancellationToken = default);

        Task SaveAsync(
            string username,
            AiRecommendationInput input,
            string cacheScope,
            GameRecommendation recommendation,
            CancellationToken cancellationToken = default);
    }
}
