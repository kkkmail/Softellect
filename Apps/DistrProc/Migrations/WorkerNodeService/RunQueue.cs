using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Softellect.Migrations.Common;

namespace Softellect.Migrations.WorkerNodeService;

[Table("RunQueue")]
public class RunQueue
{
    [Key]
    [Column("runQueueId")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid RunQueueId { get; set; }

    [Column("runQueueOrder")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long RunQueueOrder { get; set; }

    /// <summary>
    /// A solver id to determine which solver should run the model.
    /// This is needed because the modelData is stored in a zipped binary format.
    /// </summary>
    [Column("solverId")]
    public Guid SolverId { get; set; }

    [Column("runQueueStatusId")]
    public int RunQueueStatusId { get; set; }

    [Column("processId")]
    public int? ProcessId { get; set; }

    [Column("notificationTypeId")]
    public int NotificationTypeId { get; set; }

    [Column("errorMessage")]
    [MaxLength]
    public string? ErrorMessage { get; set; }

    [Column("lastErrorOn")]
    public DateTime? LastErrorOn { get; set; }

    [Column("retryCount")]
    public int RetryCount { get; set; }

    [Column("maxRetries")]
    public int MaxRetries { get; set; }

    [Column("progress")]
    public decimal Progress { get; set; }

    /// <summary>
    /// Additional progress data (if any) used for further analysis and / or for earlier termination.
    /// We want to store the progress data in JSON rather than zipped binary, so that to be able to write some queries when needed.
    /// </summary>
    [Column("progressData")]
    public string? ProgressData { get; set; }

    [Column("callCount")]
    public long CallCount { get; set; }

    [Column("evolutionTime")]
    public decimal EvolutionTime { get; set; }

    /// <summary>
    /// Should be close to 1.0 all the time. Substantial deviation is a sign of errors. If not needed, then set to 1.0.
    /// </summary>
    [Column("relativeInvariant")]
    public double RelativeInvariant { get; set; }

    [Column("createdOn")]
    public DateTime CreatedOn { get; set; }

    [Column("startedOn")]
    public DateTime? StartedOn { get; set; }

    [Column("modifiedOn")]
    public DateTime ModifiedOn { get; set; }

    // Navigation properties
    [ForeignKey("SolverId")]
    public Solver Solver { get; set; } = null!;

    [ForeignKey("RunQueueStatusId")]
    public RunQueueStatus RunQueueStatus { get; set; } = null!;

    [ForeignKey("NotificationTypeId")]
    public NotificationType NotificationType { get; set; } = null!;
}
