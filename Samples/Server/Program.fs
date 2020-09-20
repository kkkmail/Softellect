open System
open CoreWCF.Configuration
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting

let CreateWebHostBuilder args : IWebHostBuilder =
    WebHost
        .CreateDefaultBuilder(args)
        //.UseKestrel(fun options -> [ options.ListenLocalhost(8080) ])
        .UseUrls("http://localhost:8080")
        .UseNetTcp(8808)
        .UseStartup()


    //.UseKestrel(options => { options.ListenLocalhost(8080); })
    //.UseUrls("http://localhost:8080")
    //.UseNetTcp(8808)
    //.UseStartup<Startup>()


[<EntryPoint>]
let main argv =
    printfn "Hello World from F#!"
    let host = (CreateWebHostBuilder argv).Build()
    host.Run()
    0
