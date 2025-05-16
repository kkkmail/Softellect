using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.SqlServer.Design.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Softellect.Migrations.Common;

#pragma warning disable EF1001
public class DesignTimeServices : IDesignTimeServices
{
    public void ConfigureDesignTimeServices(IServiceCollection services)
    {
        services.AddEntityFrameworkSqlServer();
        services.AddEntityFrameworkDesignTimeServices();
        new SqlServerDesignTimeServices().ConfigureDesignTimeServices(services);
    }
}
#pragma warning restore EF1001
