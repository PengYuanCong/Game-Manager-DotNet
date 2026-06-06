using System.ComponentModel.DataAnnotations;

namespace Proposal.Models
{
    public class AiRecommendationInput
    {
        [Required(ErrorMessage = "請填寫遊戲模式")]
        [Display(Name = "遊戲模式")]
        public string GameTitle { get; set; } = "英雄聯盟 隨機單中大亂鬥";

        [Required(ErrorMessage = "請填寫英雄名稱")]
        [Display(Name = "英雄")]
        public string CoreChampion { get; set; } = string.Empty;

        [Display(Name = "對局階段")]
        public string? CurrentStage { get; set; }

        [Display(Name = "目前海克斯 / 增強選項")]
        public string? Augment { get; set; }

        [Display(Name = "目前可用裝備 / 散件")]
        public string? AvailableItems { get; set; }

        [Display(Name = "對局狀況 / 其他問題")]
        public string? Notes { get; set; }
    }
}
