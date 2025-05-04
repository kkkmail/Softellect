using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Softellect.MessagingService.Database;

[Table("Message")]
public class Message
{
    [Key]
    [Column("messageId")]
    public Guid MessageId { get; set; }

    [Required]
    [Column("senderId")]
    public Guid SenderId { get; set; }

    [Required]
    [Column("recipientId")]
    public Guid RecipientId { get; set; }

    [Required]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("messageOrder")]
    public long MessageOrder { get; set; }

    [Required]
    [Column("dataVersion")]
    public int DataVersion { get; set; }

    [Required]
    [Column("deliveryTypeId")]
    public int DeliveryTypeId { get; set; }

    [Required]
    [Column("messageData")]
    public byte[] MessageData { get; set; } = null!;

    [Required]
    [Column("createdOn")]
    public DateTime CreatedOn { get; set; }

    [ForeignKey(nameof(DeliveryTypeId))]
    public DeliveryType DeliveryType { get; set; } = null!;
}
