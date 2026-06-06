namespace Proposal.Models
{
    public class UserActivityLog
    {
        public int Id { get; set; }

        public string Username { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Detail { get; set; } = string.Empty;

        public string? LinkUrl { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
