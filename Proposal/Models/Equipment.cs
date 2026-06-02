using System.ComponentModel.DataAnnotations;

namespace Proposal.Models
{
    public class Equipment
    {
        [Key] // 告訴系統這是主鍵 (Primary Key)
        public int Id { get; set; }

        [Required] // 必填欄位
        [Display(Name = "裝備名稱")]
        public string Name { get; set; } = string.Empty;

        public int HP { get; set; }

        public int Mana { get; set; }

        public int Attack { get; set; }

        public int MagicAttack { get; set; }

        public int PhysicalDefense { get; set; }

        public int MagicDefense { get; set; }

        public decimal HealthRegen { get; set; }

        public decimal ManaRegen { get; set; }

        public decimal AbilityHaste { get; set; }

        public decimal AttackSpeed { get; set; }

        public decimal CriticalStrikeChance { get; set; }

        public int MoveSpeed { get; set; }

        public decimal MoveSpeedPercent { get; set; }

        public decimal Lethality { get; set; }

        public decimal ArmorPenetrationPercent { get; set; }

        public decimal MagicPenetration { get; set; }

        public decimal MagicPenetrationPercent { get; set; }

        public decimal LifeSteal { get; set; }

        public decimal Omnivamp { get; set; }

        public decimal HealAndShieldPower { get; set; }

        public decimal Tenacity { get; set; }

        public int Price { get; set; }

        public string? DataDragonId { get; set; }

        public string? ItemImageUrl { get; set; }

        public string? ItemTags { get; set; }

        public string? ItemDescription { get; set; }
    }
}
