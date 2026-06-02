using Proposal.Models;

namespace Proposal.Services;

public sealed record LoadoutItemSummary(int Id, string Name);

public sealed record LoadoutSummary(
    int Id,
    string LoadoutName,
    IReadOnlyList<LoadoutItemSummary> Items,
    IReadOnlyList<int> ItemIds,
    int InvalidCount);

public interface IEquipmentRepository
{
    Task<IReadOnlyList<Equipment>> ListAsync(
        string username,
        string? searchString = null,
        string? statFilter = null,
        CancellationToken cancellationToken = default);

    Task<Equipment?> GetByIdAsync(
        string username,
        int id,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        string username,
        Equipment equipment,
        CancellationToken cancellationToken = default);

    Task CreateManyAsync(
        string username,
        IEnumerable<Equipment> equipments,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(
        string username,
        Equipment equipment,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string username,
        int id,
        CancellationToken cancellationToken = default);

    Task<int> UpsertAsync(
        string username,
        Equipment equipment,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LoadoutSummary>> GetLoadoutsAsync(
        string username,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> SaveLoadoutAsync(
        string username,
        string loadoutName,
        IReadOnlyList<int> equipmentIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>?> UpdateLoadoutAsync(
        string username,
        int loadoutId,
        string loadoutName,
        IReadOnlyList<int> equipmentIds,
        CancellationToken cancellationToken = default);

    Task<string?> DeleteLoadoutAsync(
        string username,
        int loadoutId,
        CancellationToken cancellationToken = default);

    Task<int> DeleteInvalidLoadoutsAsync(
        string username,
        CancellationToken cancellationToken = default);
}
