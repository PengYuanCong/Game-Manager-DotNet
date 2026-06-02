namespace Proposal.Models
{
    public class LoadoutStats
    {
        public int Hp { get; set; }

        public int Mana { get; set; }

        public int Attack { get; set; }

        public int MagicAttack { get; set; }

        public int PDef { get; set; }

        public int MDef { get; set; }

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
    }
}
