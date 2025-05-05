using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Softellect.Migrations.WorkerNodeService.Migrations;

public partial class RunQueue
{
    [Key]
    public Guid runQueueId { get; set; }

    public long runQueueOrder { get; set; }

    public Guid solverId { get; set; }

    public int runQueueStatusId { get; set; }

    public int? processId { get; set; }

    public int notificationTypeId { get; set; }

    public string? errorMessage { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? lastErrorOn { get; set; }

    public int retryCount { get; set; }

    public int maxRetries { get; set; }

    [Column(TypeName = "decimal(38, 16)")]
    public decimal progress { get; set; }

    public string? progressData { get; set; }

    public long callCount { get; set; }

    [Column(TypeName = "decimal(38, 16)")]
    public decimal evolutionTime { get; set; }

    public double relativeInvariant { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime createdOn { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? startedOn { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime modifiedOn { get; set; }

    [InverseProperty("runQueue")]
    public virtual ModelData? ModelData { get; set; }

    [ForeignKey("notificationTypeId")]
    [InverseProperty("RunQueue")]
    public virtual NotificationType notificationType { get; set; } = null!;

    [ForeignKey("runQueueStatusId")]
    [InverseProperty("RunQueue")]
    public virtual RunQueueStatus runQueueStatus { get; set; } = null!;

    [ForeignKey("solverId")]
    [InverseProperty("RunQueue")]
    public virtual Solver solver { get; set; } = null!;
}
