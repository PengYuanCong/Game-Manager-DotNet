namespace Proposal.Models
{
    public class Loadout
    {
        public int Id { get; set; }

        public string LoadoutName { get; set; } = string.Empty;

        public int? Eq1_Id { get; set; }

        public int? Eq2_Id { get; set; }

        public int? Eq3_Id { get; set; }

        public int? Eq4_Id { get; set; }

        public int? Eq5_Id { get; set; }

        public int? Eq6_Id { get; set; }

        public int TotalHP { get; set; }

        public int TotalAttack { get; set; }

        public int TotalPrice { get; set; }
    }
}
