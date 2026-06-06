using System.Security.Cryptography;
using System.Text;
using Proposal.Models;

namespace Proposal.Services
{
    public static class AiRecommendationCacheKey
    {
        public static string Create(AiRecommendationInput input, string cacheScope = "")
        {
            var rawKey = string.Join("|", new[]
            {
                TextOrDefault(input.GameTitle),
                TextOrDefault(input.CoreChampion),
                TextOrDefault(input.CurrentStage),
                TextOrDefault(input.Augment),
                TextOrDefault(input.AvailableItems),
                TextOrDefault(input.Notes),
                TextOrDefault(cacheScope)
            }).ToLowerInvariant();

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string TextOrDefault(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "not provided" : value.Trim();
        }
    }
}
