using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace Softellect.Migrations.Common;

public abstract class CommonDbContext<TContext> : DbContext where TContext : DbContext, IHasServiceName
{
    protected CommonDbContext() : base(GetDesignTimeOptions())
    {
    }

    protected CommonDbContext(DbContextOptions<TContext> options) : base(options)
    {
    }

    protected static DbContextOptions<TContext> GetDesignTimeOptions()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TContext>();
        var basePath = new Uri(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!).LocalPath;
        var appSettings = JObject.Parse(File.ReadAllText(Path.Combine(basePath, "appsettings.json")));
        var connectionString = appSettings["connectionStrings"]![TContext.GetServiceName()]!.ToString();
        optionsBuilder.UseSqlServer(connectionString);
        return optionsBuilder.Options;
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder
            .Properties<decimal>()
            .HavePrecision(38, 16);

        configurationBuilder
            .Properties<double>()
            .HaveColumnType("float");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Disable cascade delete for all relationships
        foreach (var relationship in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(e => e.GetForeignKeys()))
        {
            relationship.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}
