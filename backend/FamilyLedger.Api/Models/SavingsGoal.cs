using Postgrest.Attributes;
using Postgrest.Models;

namespace FamilyLedger.Api.Models
{
    [Table("savings_goals")]
    public class SavingsGoal : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Column("user_id")]
        public Guid UserId { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("target_amount")]
        public decimal TargetAmount { get; set; }

        [Column("current_amount")]
        public decimal CurrentAmount { get; set; }

        [Column("deadline")]
        public DateTime? Deadline { get; set; }

        [Column("is_completed")]
        public bool IsCompleted { get; set; }

        [Column("created_at", ignoreOnInsert: true)]
        public DateTime CreatedAt { get; set; }
    }
}
