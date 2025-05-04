using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace Softellect.Migrations.Common;

public abstract class CommonDbContext<TContext> : DbContext where TContext: DbContext, IHasServiceName
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
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var directoryName = Path.GetDirectoryName(assemblyLocation);
        var basePath = new Uri(directoryName!).LocalPath;
        var appSettingsPath = Path.Combine(basePath, "appsettings.json");
        var json = File.ReadAllText(appSettingsPath);
        var appSettings = JObject.Parse(json);
        var connectionString = appSettings["connectionStrings"]![TContext.GetServiceName()]!.ToString();
        optionsBuilder.UseSqlServer(connectionString);
        return optionsBuilder.Options;
    }
}
