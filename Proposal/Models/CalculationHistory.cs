namespace Proposal.Models
{
    public class CalculationHistory
    {
        public int Id { get; set; }

        public string Username { get; set; } = string.Empty;

        public string FormulaType { get; set; } = string.Empty;

        public string InputDetails { get; set; } = string.Empty;

        public string ResultContent { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
    }
}
