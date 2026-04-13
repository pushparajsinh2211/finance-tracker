using Postgrest.Attributes;
using Postgrest.Models;
using System.Text.Json.Serialization;

namespace FamilyLedger.Api.Models
{
    [Table("families")]
    public class Family : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("head_user_id")]
        public Guid HeadUserId { get; set; }

        [Column("invite_code")]
        public string InviteCode { get; set; } = string.Empty;

        [Column("invite_active")]
        public bool InviteActive { get; set; } = true;

        [Column("created_at", ignoreOnInsert: true)]
        public DateTime CreatedAt { get; set; }
    }
}
