using System.ComponentModel.DataAnnotations;

namespace Proposal.Models
{
    public class LolAramGuide
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "請輸入英雄 Key")]
        [StringLength(100)]
        [Display(Name = "英雄 Key")]
        public string ChampionKey { get; set; } = string.Empty;

        [Required(ErrorMessage = "請輸入英文英雄名稱")]
        [StringLength(100)]
        [Display(Name = "英文英雄名稱")]
        public string ChampionName { get; set; } = string.Empty;

        [StringLength(100)]
        [Display(Name = "中文英雄名稱")]
        public string? LocalizedName { get; set; }

        [Required(ErrorMessage = "請輸入遊戲模式")]
        [StringLength(100)]
        [Display(Name = "遊戲模式")]
        public string ModeName { get; set; } = "ARAM Mayhem";

        [Required(ErrorMessage = "請輸入版本或資料來源標記")]
        [StringLength(50)]
        [Display(Name = "版本 / 資料標記")]
        public string PatchVersion { get; set; } = "manual";

        [Required(ErrorMessage = "請輸入英雄定位摘要")]
        [StringLength(500)]
        [Display(Name = "英雄定位摘要")]
        public string RoleSummary { get; set; } = string.Empty;

        [Required(ErrorMessage = "請輸入核心裝備")]
        [StringLength(1000)]
        [Display(Name = "核心裝備")]
        public string CoreItems { get; set; } = string.Empty;

        [StringLength(1000)]
        [Display(Name = "情境裝備")]
        public string? SituationalItems { get; set; }

        [StringLength(1000)]
        [Display(Name = "強化符文 / 增強方向")]
        public string? Augments { get; set; }

        [StringLength(500)]
        [Display(Name = "召喚師技能")]
        public string? SummonerSpells { get; set; }

        [StringLength(500)]
        [Display(Name = "技能順序")]
        public string? SkillOrder { get; set; }

        [StringLength(1200)]
        [Display(Name = "打法重點")]
        public string? PlaystyleTips { get; set; }

        [StringLength(1200)]
        [Display(Name = "站位重點")]
        public string? PositioningTips { get; set; }

        [StringLength(1200)]
        [Display(Name = "弱點 / 注意事項")]
        public string? Weaknesses { get; set; }

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
