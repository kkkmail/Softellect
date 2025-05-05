using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Softellect.Migrations.WorkerNodeService.Migrations;

public partial class DeliveryType
{
    [Key]
    public int deliveryTypeId { get; set; }

    [StringLength(50)]
    public string deliveryTypeName { get; set; } = null!;

    [InverseProperty("deliveryType")]
    public virtual ICollection<Message> Message { get; set; } = new List<Message>();
}
