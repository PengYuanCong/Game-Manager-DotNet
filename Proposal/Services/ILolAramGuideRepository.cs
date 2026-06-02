using Proposal.Models;

namespace Proposal.Services
{
    public interface ILolAramGuideRepository
    {
        Task<IReadOnlyList<LolAramGuide>> SearchAsync(string? searchText, CancellationToken cancellationToken = default);

        Task<LolAramGuide?> FindAsync(int id, CancellationToken cancellationToken = default);

        Task CreateAsync(LolAramGuide guide, CancellationToken cancellationToken = default);

        Task<GuideImportResult> UpsertManyAsync(IEnumerable<LolAramGuide> guides, CancellationToken cancellationToken = default);

        Task<GuideImportResult> UpsertAugmentRecommendationsAsync(IEnumerable<LolAramGuide> guides, CancellationToken cancellationToken = default);

        Task<bool> UpdateAsync(LolAramGuide guide, CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    }
}
