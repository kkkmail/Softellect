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
