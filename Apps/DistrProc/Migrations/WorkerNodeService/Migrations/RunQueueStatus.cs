using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Softellect.Migrations.WorkerNodeService.Migrations;

[Index("runQueueStatusName", Name = "UX_RunQueueStatus", IsUnique = true)]
public partial class RunQueueStatus
{
    [Key]
    public int runQueueStatusId { get; set; }

    [StringLength(50)]
    public string runQueueStatusName { get; set; } = null!;

    [InverseProperty("runQueueStatus")]
    public virtual ICollection<RunQueue> RunQueue { get; set; } = new List<RunQueue>();
}
