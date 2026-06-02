using Proposal.Models;

namespace Proposal.Services
{
    public interface IAiRecommendationService
    {
        Task<GameRecommendation> CreateRecommendationAsync(
            AiRecommendationInput input,
            AiKnowledgeContext knowledgeContext,
            CancellationToken cancellationToken = default);
    }
}
