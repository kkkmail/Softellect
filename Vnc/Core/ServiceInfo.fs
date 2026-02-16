namespace Softellect.Vnc.Core

open System.ServiceModel
open Microsoft.Extensions.Hosting
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open Softellect.Wcf.Common
open Softellect.Vnc.Core.Primitives
open Softellect.Vnc.Core.Errors

module ServiceInfo =

    /// WCF service contract for VNC control channel.
    [<ServiceContract(ConfigurationName = VncServiceName)>]
    type IVncWcfService =

        [<OperationContract(Name = "connect")>]
        abstract connect : data:byte[] -> byte[]

        [<OperationContract(Name = "disconnect")>]
        abstract disconnect : data:byte[] -> byte[]

        [<OperationContract(Name = "sendInput")>]
        abstract sendInput : data:byte[] -> byte[]

        [<OperationContract(Name = "getClipboard")>]
        abstract getClipboard : data:byte[] -> byte[]

        [<OperationContract(Name = "setClipboard")>]
        abstract setClipboard : data:byte[] -> byte[]

        [<OperationContract(Name = "listDirectory")>]
        abstract listDirectory : data:byte[] -> byte[]

        [<OperationContract(Name = "readFileChunk")>]
        abstract readFileChunk : data:byte[] -> byte[]

        [<OperationContract(Name = "writeFileChunk")>]
        abstract writeFileChunk : data:byte[] -> byte[]


    /// WCF service contract for VNC repeater.
    [<ServiceContract(ConfigurationName = VncRepeaterServiceName)>]
    type IVncRepeaterWcfService =

        [<OperationContract(Name = "registerMachine")>]
        abstract registerMachine : data:byte[] -> byte[]

        [<OperationContract(Name = "requestConnection")>]
        abstract requestConnection : data:byte[] -> byte[]

        [<OperationContract(Name = "queryStatus")>]
        abstract queryStatus : data:byte[] -> byte[]


    /// High-level F# interface for VNC service.
    type IVncService =
        inherit IHostedService
        abstract connect : VncConnectRequest -> VncResult<VncConnectResponse>
        abstract disconnect : VncSessionId -> VncUnitResult
        abstract sendInput : InputEvent -> VncUnitResult
        abstract getClipboard : unit -> VncResult<ClipboardData>
        abstract setClipboard : ClipboardData -> VncUnitResult


    /// High-level F# interface for VNC repeater.
    type IVncRepeaterService =
        abstract registerMachine : VncMachineInfo -> VncUnitResult
        abstract requestConnection : VncMachineName -> VncResult<VncSessionId>
        abstract queryStatus : VncMachineName list -> VncResult<VncMachineInfo list>


    type VncServiceAccessInfo =
        {
            serviceAccessInfo : ServiceAccessInfo
            udpPort : int
        }

        static member defaultValue =
            {
                serviceAccessInfo =
                    {
                        netTcpServiceAddress = ServiceAddress localHost
                        netTcpServicePort = ServicePort DefaultVncWcfPort
                        netTcpServiceName = ServiceName VncServiceName
                        netTcpSecurityMode = NoSecurity
                    }
                    |> NetTcpServiceInfo
                udpPort = DefaultVncUdpPort
            }


    type VncServerData =
        {
            vncServiceAccessInfo : VncServiceAccessInfo
            serverPrivateKey : PrivateKey
            serverPublicKey : PublicKey
            viewerKeysPath : FolderName
            encryptionType : EncryptionType
        }


    type VncViewerData =
        {
            viewerId : VncViewerId
            viewerPrivateKey : PrivateKey
            viewerPublicKey : PublicKey
            serverPublicKey : PublicKey
            encryptionType : EncryptionType
        }


    type VncRepeaterAccessInfo =
        {
            serviceAccessInfo : ServiceAccessInfo
        }
