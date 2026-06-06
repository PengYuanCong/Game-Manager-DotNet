using Proposal.Models;

namespace Proposal.Services;

public interface ICalculatorDataRepository
{
    Task<IReadOnlyList<Equipment>> GetEquipmentOptionsAsync(
        string username,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Loadout>> GetLoadoutOptionsAsync(
        string username,
        CancellationToken cancellationToken = default);

    Task<LoadoutStats?> GetLoadoutStatsAsync(
        string username,
        int loadoutId,
        CancellationToken cancellationToken = default);
}
