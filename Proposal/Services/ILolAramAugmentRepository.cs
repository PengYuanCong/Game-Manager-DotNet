using Proposal.Models;

namespace Proposal.Services
{
    public interface ILolAramAugmentRepository
    {
        Task<IReadOnlyList<LolAramAugment>> SearchAsync(string? searchText, CancellationToken cancellationToken = default);

        Task<LolAramAugment?> FindAsync(int id, CancellationToken cancellationToken = default);

        Task CreateAsync(LolAramAugment augment, CancellationToken cancellationToken = default);

        Task<bool> UpdateAsync(LolAramAugment augment, CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

        Task<AugmentImportResult> UpsertManyAsync(IEnumerable<LolAramAugment> augments, CancellationToken cancellationToken = default);
    }
}
