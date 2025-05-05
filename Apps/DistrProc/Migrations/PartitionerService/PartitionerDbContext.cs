using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Set decimal precision scale decimal(38, 16) as a default
        configurationBuilder
            .Properties<decimal>()
            .HavePrecision(38, 16);
    }
}
