using Proposal.Models;

namespace Proposal.Services
{
    public interface IOpGgAramMayhemChampionAugmentScraper
    {
        Task<IReadOnlyList<LolAramGuide>> ScrapeChampionAugmentGuidesAsync(
            string sourceUrl,
            int startIndex,
            int maxHeroes,
            CancellationToken cancellationToken = default);
    }
}
