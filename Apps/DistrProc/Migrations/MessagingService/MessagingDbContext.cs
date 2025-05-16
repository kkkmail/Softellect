using Microsoft.EntityFrameworkCore;
using Softellect.Migrations.Common;

namespace Softellect.Migrations.MessagingService;

public class MessagingDbContext : CommonDbContext<MessagingDbContext>, IHasServiceName
{
    public static string GetServiceName() => "MessagingService";

    public MessagingDbContext() : base(GetDesignTimeOptions())
    {
    }

    public MessagingDbContext(DbContextOptions<MessagingDbContext> options) : base(options)
    {
    }

    public DbSet<DeliveryType> DeliveryTypes { get; set; } = null!;
    public DbSet<Message> Messages { get; set; } = null!;
}
