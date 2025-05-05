using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Softellect.Migrations.WorkerNodeService.Migrations;

public partial class Message
{
    [Key]
    public Guid messageId { get; set; }

    public Guid senderId { get; set; }

    public Guid recipientId { get; set; }

    public long messageOrder { get; set; }

    public int dataVersion { get; set; }

    public int deliveryTypeId { get; set; }

    public byte[] messageData { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime createdOn { get; set; }

    [ForeignKey("deliveryTypeId")]
    [InverseProperty("Message")]
    public virtual DeliveryType deliveryType { get; set; } = null!;
}
