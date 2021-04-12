open System
open System.IO
open System.Linq
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Configuration.Json
open Newtonsoft.Json
open FSharp.Interop.Dynamic
open Softellect.Sys.AppSettings

[<EntryPoint>]
let main argv =
    //let fileName = "TestSettings.json"
    //let appSettings = "appSettings"
    //let expirationTimeInMinutes = "ExpirationTimeInMinutes"

    //let update() =
    //    let json = File.ReadAllText(fileName)
    //    let jsonObj = JsonConvert.DeserializeObject(json)
    //    //jsonObj?appSettings?ExpirationTimeInMinutes <- "10"
    //    jsonObj?appSettings?ExpirationTimeInMinutes <- "10"
    //    let output = JsonConvert.SerializeObject(jsonObj, Formatting.Indented)
    //    File.WriteAllText(fileName, output)

    //let updateByKey (section : string) (key : string) (value : string) =
    //    let json = File.ReadAllText(fileName)
    //    let jsonObj = JsonConvert.DeserializeObject(json)
    //    jsonObj?(section)?(key) <- value
    //    let output = JsonConvert.SerializeObject(jsonObj, Formatting.Indented)
    //    File.WriteAllText(fileName, output)


    //let getByKey (section : string) (key : string) =
    //    let json = File.ReadAllText(fileName)
    //    let jsonObj = JsonConvert.DeserializeObject(json)
    //    (jsonObj?(section)?(key)).ToString()

    //printfn "Hello World from F#!"
    //updateByKey appSettings expirationTimeInMinutes "101"

    //let newKey = "SomeNewKey"
    //updateByKey appSettings newKey "12345"

    //let a = getByKey appSettings expirationTimeInMinutes
    //printfn "expirationTimeInMinutes = %A" a

    //let b = getByKey appSettings newKey
    //printfn "%A = %A" newKey b

    let appSettingsFile = "appsettings.json"
    let messagingServiceAddress = ConfigKey "MessagingServiceAddress"
    let messagingHttpServicePort = ConfigKey "MessagingHttpServicePort"

    match AppSettingsProvider.tryCreate appSettingsFile with
    | Ok provider ->
        printfn "Created AppSettingsProvider..."

        let result1 = provider.trySet messagingServiceAddress "new.address"
        printfn $"result1 = %A{result1}"

        let result2 = provider.trySet messagingHttpServicePort 123456
        printfn $"result2 = %A{result2}"

        let result = provider.trySave()
        printfn $"result = %A{result}"
    | Error e -> printfn $"Error: %A{e}"

    0
