using Postgrest.Attributes;
using Postgrest.Models;

namespace FamilyLedger.Api.Models
{
    [Table("family_members")]
    public class FamilyMember : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Column("family_id")]
        public Guid FamilyId { get; set; }

        [Column("user_id")]
        public Guid UserId { get; set; }

        [Column("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [Column("relation")]
        public string? Relation { get; set; }

        [Column("is_dependent")]
        public bool IsDependent { get; set; } = true;

        [Column("joined_at", ignoreOnInsert: true)]
        public DateTime JoinedAt { get; set; }
    }
}
