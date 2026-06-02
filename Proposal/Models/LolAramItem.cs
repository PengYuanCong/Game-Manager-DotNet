using System.ComponentModel.DataAnnotations;

namespace Proposal.Models
{
    public class LolAramItem
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string ItemKey { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Aliases { get; set; }

        [Required]
        [StringLength(100)]
        public string ModeName { get; set; } = "ARAM Mayhem";

        [Required]
        [StringLength(1200)]
        public string EffectText { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Tags { get; set; }

        [StringLength(1200)]
        public string? SynergyNotes { get; set; }

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
