using Proposal.Models;

namespace Proposal.Services;

public interface ICalculationHistoryRepository
{
    Task<IReadOnlyList<CalculationHistory>> GetRecentAsync(
        string username,
        int count,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        string username,
        string formulaType,
        string inputDetails,
        string resultContent,
        CancellationToken cancellationToken = default);
}
