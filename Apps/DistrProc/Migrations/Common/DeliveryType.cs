using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Softellect.Migrations.Common;

[Table("DeliveryType")]
public class DeliveryType
{
    [Key]
    [Column("deliveryTypeId")]
    public int DeliveryTypeId { get; set; }

    [Required]
    [MaxLength(50)]
    [Column("deliveryTypeName")]
    public string DeliveryTypeName { get; set; } = null!;

    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
