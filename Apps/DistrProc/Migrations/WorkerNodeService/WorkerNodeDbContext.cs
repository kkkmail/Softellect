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

    public DbSet<NotificationType> NotificationTypes { get; set; } = null!;
    public DbSet<RunQueueStatus> RunQueueStatuses { get; set; } = null!;
    public DbSet<Solver> Solvers { get; set; } = null!;
    public DbSet<RunQueue> RunQueues { get; set; } = null!;
    public DbSet<ModelData> ModelDatas { get; set; } = null!;
    public DbSet<Setting> Settings { get; set; } = null!;
}
