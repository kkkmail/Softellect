using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MessagingService.Database
{
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

        // Navigation
        public ICollection<Message> Messages { get; set; } = new List<Message>();
    }

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

        // Navigation
        [ForeignKey(nameof(DeliveryTypeId))]
        public DeliveryType DeliveryType { get; set; } = null!;
    }

    public class MessagingDbContext : DbContext
    {
        public MessagingDbContext(DbContextOptions<MessagingDbContext> options)
            : base(options)
        {
        }

        public DbSet<DeliveryType> DeliveryTypes { get; set; } = null!;
        public DbSet<Message> Messages { get; set; } = null!;
    }
}
