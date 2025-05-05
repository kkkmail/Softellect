using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Softellect.Migrations.WorkerNodeService.Migrations;

public partial class ModelData
{
    [Key]
    public Guid runQueueId { get; set; }

    public byte[] modelData { get; set; } = null!;

    [ForeignKey("runQueueId")]
    [InverseProperty("ModelData")]
    public virtual RunQueue runQueue { get; set; } = null!;
}
