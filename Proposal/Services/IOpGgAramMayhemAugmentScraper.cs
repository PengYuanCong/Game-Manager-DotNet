using Proposal.Models;

namespace Proposal.Services
{
    public interface IOpGgAramMayhemAugmentScraper
    {
        Task<IReadOnlyList<LolAramAugment>> ScrapeAugmentsAsync(string sourceUrl, CancellationToken cancellationToken = default);
    }
}
