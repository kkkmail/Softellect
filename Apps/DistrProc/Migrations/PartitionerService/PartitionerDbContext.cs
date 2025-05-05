using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Softellect.Migrations.Common;

namespace Softellect.Migrations.PartitionerService;

public class PartitionerDbContext : CommonDbContext<PartitionerDbContext>, IHasServiceName
{
    public static string GetServiceName() => "PartitionerService";

    public PartitionerDbContext() : base(GetDesignTimeOptions())
    {
    }

    public PartitionerDbContext(DbContextOptions<PartitionerDbContext> options) : base(options)
    {
    }

    public DbSet<DeliveryType> DeliveryTypes { get; set; } = null!;
    public DbSet<Message> Messages { get; set; } = null!;
    public DbSet<NotificationType> NotificationTypes { get; set; } = null!;
    public DbSet<RunQueueStatus> RunQueueStatuses { get; set; } = null!;
    public DbSet<Solver> Solvers { get; set; } = null!;
    public DbSet<WorkerNode> WorkerNodes { get; set; } = null!;
    public DbSet<RunQueue> RunQueues { get; set; } = null!;
    public DbSet<ModelData> ModelDatas { get; set; } = null!;
    public DbSet<Setting> Settings { get; set; } = null!;
    public DbSet<WorkerNodeSolver> WorkerNodeSolvers { get; set; } = null!;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Set decimal precision scale decimal(38, 16) as a default
        configurationBuilder
            .Properties<decimal>()
            .HavePrecision(38, 16);
    }
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

    // Navigation property
    public ICollection<RunQueue> RunQueues { get; set; } = new List<RunQueue>();
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

    // Navigation property
    public virtual ICollection<RunQueue> RunQueues { get; set; } = new List<RunQueue>();
}

[Table("Solver")]
[Index(nameof(SolverName), IsUnique = true)]
public class Solver
{
    [Key]
    [Column("solverId")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
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

    [Required]
    [Column("solverHash")]
    [StringLength(64)]
    public string SolverHash { get; set; } = null!;

    [Column("createdOn")]
    public DateTime CreatedOn { get; set; }

    [Column("isInactive")]
    public bool IsInactive { get; set; }

    // Navigation properties
    public ICollection<RunQueue> RunQueues { get; set; } = new List<RunQueue>();
    public ICollection<WorkerNodeSolver> WorkerNodeSolvers { get; set; } = new List<WorkerNodeSolver>();
}

[Table("WorkerNode")]
[Index(nameof(WorkerNodeName), IsUnique = true)]
[Index(nameof(WorkerNodeOrder), IsUnique = true)]
public class WorkerNode
{
    [Key]
    [Column("workerNodeId")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid WorkerNodeId { get; set; }

    [Column("workerNodeOrder")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long WorkerNodeOrder { get; set; }

    [Required]
    [Column("workerNodeName")]
    [StringLength(100)]
    public string WorkerNodeName { get; set; } = null!;

    [Column("nodePriority")]
    public int NodePriority { get; set; }

    [Column("numberOfCores")]
    public int NumberOfCores { get; set; }

    [Column("description")]
    [StringLength(1000)]
    public string? Description { get; set; }

    [Column("isInactive")]
    public bool IsInactive { get; set; }

    [Column("workerNodePublicKey")]
    public byte[]? WorkerNodePublicKey { get; set; }

    [Column("createdOn")]
    public DateTime CreatedOn { get; set; }

    [Column("modifiedOn")]
    public DateTime ModifiedOn { get; set; }

    // Navigation properties
    public ICollection<RunQueue> RunQueues { get; set; } = new List<RunQueue>();
    public virtual ICollection<WorkerNodeSolver> WorkerNodeSolvers { get; set; } = new List<WorkerNodeSolver>();
}

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
    /// Should be close to 1.0 all the time. Substantial deviations is a sign of errors. If not needed, then set to 1.0.
    /// </summary>
    [Column("relativeInvariant")]
    public float RelativeInvariant { get; set; }

    [Column("createdOn")]
    public DateTime CreatedOn { get; set; }

    [Column("startedOn")]
    public DateTime? StartedOn { get; set; }

    [Column("modifiedOn")]
    public DateTime ModifiedOn { get; set; }

    /// <summary>
    /// Partitioner has extra column to account for the worker node running the calculation.
    /// </summary>
    [Column("workerNodeId")]
    public Guid? WorkerNodeId { get; set; }

    // Navigation properties
    [ForeignKey(nameof(SolverId))]
    public Solver Solver { get; set; } = null!;

    [ForeignKey(nameof(RunQueueStatusId))]
    public RunQueueStatus RunQueueStatus { get; set; } = null!;

    [ForeignKey(nameof(NotificationTypeId))]
    public NotificationType NotificationType { get; set; } = null!;

    [ForeignKey(nameof(WorkerNodeId))]
    public WorkerNode? WorkerNode { get; set; }

    public ModelData? ModelData { get; set; }
}

[Table("ModelData")]
public class ModelData
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column("runQueueId")]
    public Guid RunQueueId { get; set; }

    /// <summary>
    /// All the initial data that is needed to run the calculation.
    /// It is designed to be huge, and so zipped binary format is used.
    /// </summary>
    [Required]
    [Column("modelData")]
    public byte[] ModelDataBytes { get; set; } = null!;

    // Navigation property
    [ForeignKey(nameof(RunQueueId))]
    public RunQueue RunQueue { get; set; } = null!;
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
    [MaxLength]
    public string? SettingText { get; set; }

    [Column("settingBinary")]
    public byte[]? SettingBinary { get; set; }

    [Column("createdOn")]
    public DateTime CreatedOn { get; set; }
}

[Table("WorkerNodeSolver")]
[PrimaryKey(nameof(WorkerNodeId), nameof(SolverId))]
public class WorkerNodeSolver
{
    [Column("workerNodeId")]
    public Guid WorkerNodeId { get; set; }

    [Column("solverId")]
    public Guid SolverId { get; set; }

    [Column("createdOn")]
    public DateTime CreatedOn { get; set; }

    [Column("isDeployed")]
    public bool IsDeployed { get; set; }

    [Column("deploymentError")]
    [MaxLength]
    public string? DeploymentError { get; set; }

    // Navigation properties
    [ForeignKey(nameof(WorkerNodeId))]
    public WorkerNode WorkerNode { get; set; } = null!;

    [ForeignKey(nameof(SolverId))]
    public Solver Solver { get; set; } = null!;
}
