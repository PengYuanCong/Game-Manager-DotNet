namespace Proposal.Models
{
    public class OpenRouterOptions
    {
        public string ApiKey { get; set; } = string.Empty;

        public string Model { get; set; } = "nvidia/nemotron-3-super-120b-a12b:free";

        public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1/";

        public string? SiteUrl { get; set; }

        public string? SiteName { get; set; }

        public bool BypassSystemProxy { get; set; } = true;

        public int TimeoutSeconds { get; set; } = 45;

        public int MaxTokens { get; set; } = 2600;

        public double Temperature { get; set; } = 0.3;
    }
}
