using CoreWCF;
using CoreWCF.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Softellect.Interop
{
    public class WcfStartup<TService, TInterface>
        //where TService : class
        where TService : class, TInterface
    {
        private void CreateServiceModel(IServiceBuilder builder)
        {
            builder
                .AddService<TService>()
                .AddServiceEndpoint<TService, TInterface>(new BasicHttpBinding(), "/basichttp")
                .AddServiceEndpoint<TService, TInterface>(new NetTcpBinding(), "/nettcp");
        }

        public void ConfigureServices(IServiceCollection services) => services.AddServiceModelServices();
        public void Configure(IApplicationBuilder app, IHostingEnvironment env) => app.UseServiceModel(CreateServiceModel);
    }
}
