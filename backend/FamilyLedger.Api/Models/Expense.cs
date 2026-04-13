using Postgrest.Attributes;
using Postgrest.Models;

namespace FamilyLedger.Api.Models
{
    [Table("expenses")]
    public class Expense : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Column("user_id")]
        public Guid UserId { get; set; }

        [Column("family_id")]
        public Guid FamilyId { get; set; }

        [Column("category_id")]
        public Guid CategoryId { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("date")]
        public DateTime Date { get; set; }

        [Column("note")]
        public string? Note { get; set; }

        [Column("is_recurring")]
        public bool IsRecurring { get; set; }

        [Column("receipt_url")]
        public string? ReceiptUrl { get; set; }

        [Column("deleted_at")]
        public DateTime? DeletedAt { get; set; }

        [Column("created_at", ignoreOnInsert: true)]
        public DateTime CreatedAt { get; set; }
    }
}
