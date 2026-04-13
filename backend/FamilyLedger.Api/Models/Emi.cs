using Postgrest.Attributes;
using Postgrest.Models;

namespace FamilyLedger.Api.Models
{
    [Table("emis")]
    public class Emi : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Column("user_id")]
        public Guid UserId { get; set; }

        [Column("lender_name")]
        public string LenderName { get; set; } = string.Empty;

        [Column("principal")]
        public decimal Principal { get; set; }

        [Column("monthly_emi")]
        public decimal MonthlyEmi { get; set; }

        [Column("start_date")]
        public DateTime StartDate { get; set; }

        [Column("tenure_months")]
        public int TenureMonths { get; set; }

        [Column("created_at", ignoreOnInsert: true)]
        public DateTime CreatedAt { get; set; }
    }
}
