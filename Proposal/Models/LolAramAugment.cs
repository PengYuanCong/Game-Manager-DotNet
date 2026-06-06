using System.ComponentModel.DataAnnotations;

namespace Proposal.Models
{
    public class LolAramAugment
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "請輸入海克斯 Key")]
        [StringLength(100)]
        [Display(Name = "海克斯 Key")]
        public string AugmentKey { get; set; } = string.Empty;

        [Required(ErrorMessage = "請輸入海克斯名稱")]
        [StringLength(100)]
        [Display(Name = "海克斯名稱")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "請輸入遊戲模式")]
        [StringLength(100)]
        [Display(Name = "遊戲模式")]
        public string ModeName { get; set; } = "ARAM Mayhem";

        [Required(ErrorMessage = "請輸入稀有度")]
        [StringLength(50)]
        [Display(Name = "稀有度")]
        public string Rarity { get; set; } = "gold";

        [StringLength(20)]
        [Display(Name = "評級")]
        public string? Tier { get; set; }

        [StringLength(100)]
        [Display(Name = "系列 Key")]
        public string? SeriesKey { get; set; }

        [Required(ErrorMessage = "請輸入效果文字")]
        [StringLength(1200)]
        [Display(Name = "效果文字")]
        public string EffectText { get; set; } = string.Empty;

        [StringLength(1000)]
        [Display(Name = "效果標籤")]
        public string? Tags { get; set; }

        [StringLength(1200)]
        [Display(Name = "搭配備註")]
        public string? SynergyNotes { get; set; }

        [Required(ErrorMessage = "請輸入版本或資料來源標記")]
        [StringLength(50)]
        [Display(Name = "版本 / 資料標記")]
        public string PatchVersion { get; set; } = "manual";

        [StringLength(1000)]
        [Display(Name = "參考來源 URL")]
        public string? SourceUrl { get; set; }

        [StringLength(1200)]
        [Display(Name = "人工備註")]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
