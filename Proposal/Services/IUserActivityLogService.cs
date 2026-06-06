using Proposal.Models;

namespace Proposal.Services
{
    public interface IUserActivityLogService
    {
        Task AddAsync(
            string username,
            string category,
            string title,
            string detail,
            string? linkUrl = null,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<UserActivityLog>> GetRecentAsync(
            string username,
            int limit = 5,
            CancellationToken cancellationToken = default);
    }
}
