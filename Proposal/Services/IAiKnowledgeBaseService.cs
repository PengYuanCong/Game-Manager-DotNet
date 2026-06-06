using Proposal.Models;

namespace Proposal.Services
{
    public interface IAiKnowledgeBaseService
    {
        Task<AiKnowledgeContext> GetContextAsync(
            AiRecommendationInput input,
            CancellationToken cancellationToken = default);
    }
}
