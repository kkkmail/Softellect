namespace Softellect.Communication.Samples

open CoreWCF
open  CoreWCF.Configuration
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection

module Startup =

    type Startup() =

        member _.ConfigureServices(services : IServiceCollection) =
            ()

        member _.Configure(app : IApplicationBuilder, env : IHostingEnvironment) =
            ()

