using Postgrest.Attributes;
using Postgrest.Models;

namespace FamilyLedger.Api.Models
{
    [Table("categories")]
    public class Category : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Column("family_id")]
        public Guid FamilyId { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("color")]
        public string? Color { get; set; }

        [Column("is_default")]
        public bool IsDefault { get; set; }

        [Column("is_archived")]
        public bool IsArchived { get; set; }

        [Column("created_at", ignoreOnInsert: true)]
        public DateTime CreatedAt { get; set; }
    }
}
