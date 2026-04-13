using Postgrest.Attributes;
using Postgrest.Models;

namespace FamilyLedger.Api.Models
{
    [Table("budgets")]
    public class Budget : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Column("user_id")]
        public Guid UserId { get; set; }

        [Column("category_id")]
        public Guid CategoryId { get; set; }

        [Column("month")]
        public string Month { get; set; } = string.Empty;

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("created_at", ignoreOnInsert: true)]
        public DateTime CreatedAt { get; set; }
    }
}
