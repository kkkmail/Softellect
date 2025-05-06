using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Softellect.Migrations.Common;

[Table("DeliveryType")]
[Index(nameof(DeliveryTypeName), IsUnique = true)]
public class DeliveryType
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column("deliveryTypeId")]
    public int DeliveryTypeId { get; set; }

    [Required]
    [MaxLength(50)]
    [Column("deliveryTypeName")]
    public string DeliveryTypeName { get; set; } = null!;
}
