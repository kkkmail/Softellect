using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Softellect.Migrations.WorkerNodeService.Migrations;

[Index("notificationTypeName", Name = "UX_NotificationType", IsUnique = true)]
public partial class NotificationType
{
    [Key]
    public int notificationTypeId { get; set; }

    [StringLength(50)]
    public string notificationTypeName { get; set; } = null!;

    [InverseProperty("notificationType")]
    public virtual ICollection<RunQueue> RunQueue { get; set; } = new List<RunQueue>();
}
