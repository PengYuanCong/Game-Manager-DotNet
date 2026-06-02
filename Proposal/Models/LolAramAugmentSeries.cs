using System.ComponentModel.DataAnnotations;

namespace Proposal.Models
{
    public class LolAramAugmentSeries
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string SeriesKey { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string SeriesName { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Description { get; set; }

        [StringLength(1200)]
        public string? SetBonusText { get; set; }

        [StringLength(1000)]
        public string? Tags { get; set; }

        [StringLength(50)]
        public string PatchVersion { get; set; } = "manual";

        [StringLength(1000)]
        public string? SourceUrl { get; set; }

        [StringLength(1200)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
