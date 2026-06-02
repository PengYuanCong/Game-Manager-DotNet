namespace Proposal.Models
{
    public class AiKnowledgeContext
    {
        public bool HasGuide { get; set; }

        public string CacheScope { get; set; } = "no-guide";

        public string PromptContext { get; set; } = string.Empty;

        public string? SourceUrl { get; set; }

        public string? SourceLabel { get; set; }
    }
}
