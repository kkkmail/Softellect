using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Softellect.Migrations.Common;

namespace Softellect.Migrations.WorkerNodeService;

public class WorkerNodeDbContext : CommonDbContext<WorkerNodeDbContext>, IHasServiceName
{
    public static string GetServiceName() => "WorkerNodeService";

    public WorkerNodeDbContext() : base(GetDesignTimeOptions())
    {
    }

    public WorkerNodeDbContext(DbContextOptions<WorkerNodeDbContext> options) : base(options)
    {
    }

    public DbSet<DeliveryType> DeliveryTypes { get; set; } = null!;
    public DbSet<Message> Messages { get; set; } = null!;
}

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

[Table("RunQueueStatus")]
[Index(nameof(RunQueueStatusName), IsUnique = true)]
public class RunQueueStatus
{
    [Key]
    [Column("runQueueStatusId")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int RunQueueStatusId { get; set; }

    [Required]
    [Column("runQueueStatusName")]
    [StringLength(50)]
    public string RunQueueStatusName { get; set; } = null!;
}

[Table("Solver")]
[Index(nameof(SolverName), IsUnique = true)]
public class Solver
{
    [Key]
    [Column("solverId")]
    public Guid SolverId { get; set; }

    [Column("solverOrder")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long SolverOrder { get; set; }

    [Required]
    [Column("solverName")]
    [StringLength(100)]
    public string SolverName { get; set; } = null!;

    [Column("description")]
    [StringLength(2000)]
    public string? Description { get; set; }

    [Column("solverData")]
    public byte[]? SolverData { get; set; }

    [Column("createdOn")]
    public DateTime CreatedOn { get; set; }

    [Column("isDeployed")]
    public bool IsDeployed { get; set; }
}

[Table("RunQueue")]
public class RunQueue
{
    [Key]
    [Column("runQueueId")]
    public Guid RunQueueId { get; set; }

    [Column("runQueueOrder")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long RunQueueOrder { get; set; }

    [Column("solverId")]
    [ForeignKey("Solver")]
    public Guid SolverId { get; set; }

    [Column("runQueueStatusId")]
    [ForeignKey("RunQueueStatus")]
    public int RunQueueStatusId { get; set; }

    [Column("processId")]
    public int? ProcessId { get; set; }

    [Column("notificationTypeId")]
    [ForeignKey("NotificationType")]
    public int NotificationTypeId { get; set; }

    [Column("errorMessage")]
    public string? ErrorMessage { get; set; }

    [Column("lastErrorOn")]
    public DateTime? LastErrorOn { get; set; }

    [Column("retryCount")]
    public int RetryCount { get; set; }

    [Column("maxRetries")]
    public int MaxRetries { get; set; }

    [Column("progress")]
    public decimal Progress { get; set; }

    [Column("progressData")]
    public string? ProgressData { get; set; }

    [Column("callCount")]
    public long CallCount { get; set; }

    [Column("evolutionTime")]
    public decimal EvolutionTime { get; set; }

    [Column("relativeInvariant")]
    public float RelativeInvariant { get; set; }

    [Column("createdOn")]
    public DateTime CreatedOn { get; set; }

    [Column("startedOn")]
    public DateTime? StartedOn { get; set; }

    [Column("modifiedOn")]
    public DateTime ModifiedOn { get; set; }
}

[Table("ModelData")]
public class ModelData
{
    [Key]
    [Column("runQueueId")]
    [ForeignKey("RunQueue")]
    public Guid RunQueueId { get; set; }

    [Required]
    [Column("modelData")]
    public byte[] ModelDataBytes { get; set; } = null!;
}

[Table("Setting")]
[Index(nameof(SettingName), IsUnique = true)]
public class Setting
{
    [Key]
    [Column("settingName")]
    [StringLength(100)]
    public string SettingName { get; set; } = null!;

    [Column("settingBool")]
    public bool? SettingBool { get; set; }

    [Column("settingGuid")]
    public Guid? SettingGuid { get; set; }

    [Column("settingLong")]
    public long? SettingLong { get; set; }

    [Column("settingText")]
    public string? SettingText { get; set; }

    [Column("settingBinary")]
    public byte[]? SettingBinary { get; set; }

    [Column("createdOn")]
    public DateTime CreatedOn { get; set; }
}
