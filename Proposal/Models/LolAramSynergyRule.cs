using System.ComponentModel.DataAnnotations;

namespace Proposal.Models
{
    public class LolAramSynergyRule
    {
        public int Id { get; set; }

        [Required]
        [StringLength(150)]
        public string RuleName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? BoostAugmentKey { get; set; }

        [StringLength(100)]
        public string? SeriesKey { get; set; }

        [StringLength(1000)]
        public string? TriggerTags { get; set; }

        [StringLength(1000)]
        public string? ChampionTags { get; set; }

        [StringLength(1000)]
        public string? ItemTags { get; set; }

        [Required]
        [StringLength(1200)]
        public string ConditionText { get; set; } = string.Empty;

        [Required]
        [StringLength(1200)]
        public string RecommendationText { get; set; } = string.Empty;

        [StringLength(20)]
        public string Priority { get; set; } = "medium";

        [StringLength(50)]
        public string PatchVersion { get; set; } = "manual";

        [StringLength(1200)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
