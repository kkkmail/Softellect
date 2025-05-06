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
}
