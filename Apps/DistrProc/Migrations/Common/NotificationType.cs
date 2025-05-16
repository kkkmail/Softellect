using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Softellect.Migrations.Common;

[Table("NotificationType")]
[Index(nameof(NotificationTypeName), IsUnique = true)]
public class NotificationType
{
    [Key]
    [Column("notificationTypeId")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int NotificationTypeId { get; set; }

    [Required]
    [Column("notificationTypeName")]
    [StringLength(50)]
    public string NotificationTypeName { get; set; } = null!;
}