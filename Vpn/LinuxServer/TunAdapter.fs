namespace Softellect.Vpn.LinuxServer

open System
open System.Runtime.InteropServices
open System.Threading
open Softellect.Sys.Primitives
open Softellect.Vpn.Core.Primitives

module TunAdapter =

    // ============================================================
    // libc P/Invoke declarations
    // ============================================================

    [<Literal>]
    let private O_RDWR = 2

    [<Literal>]
    let private IFF_TUN = 0x0001s

    [<Literal>]
    let private IFF_NO_PI = 0x1000s

    [<Literal>]
    let private TUNSETIFF = 0x400454caUL

    [<Literal>]
    let private POLLIN = 0x0001s

    [<Literal>]
    let private IFNAMSIZ = 16

    [<StructLayout(LayoutKind.Sequential)>]
    [<Struct>]
    type private IfReq =
        [<MarshalAs(UnmanagedType.ByValArray, SizeConst = IFNAMSIZ)>]
        val mutable ifrName : byte[]
        val mutable ifrFlags : int16
        [<MarshalAs(UnmanagedType.ByValArray, SizeConst = 22)>]
        val mutable ifrPadding : byte[]

    [<StructLayout(LayoutKind.Sequential)>]
    [<Struct>]
    type private PollFd =
        val mutable fd : int
        val mutable events : int16
        val mutable revents : int16

    [<DllImport("libc", SetLastError = true)>]
    extern int private ``open``([<MarshalAs(UnmanagedType.LPStr)>] string path, int flags)

    [<DllImport("libc", SetLastError = true)>]
    extern int private close(int fd)

    [<DllImport("libc", SetLastError = true)>]
    extern int private ioctl(int fd, uint64 request, IfReq& ifr)

    [<DllImport("libc", SetLastError = true)>]
    extern nativeint private read(int fd, byte[] buf, nativeint count)

    [<DllImport("libc", SetLastError = true)>]
    extern nativeint private write(int fd, byte[] buf, nativeint count)

    [<DllImport("libc", SetLastError = true)>]
    extern int private poll(PollFd[] fds, uint64 nfds, int timeout)

    // ============================================================
    // Helper functions
    // ============================================================

    let private getErrno () = Marshal.GetLastWin32Error()

    let private runCommand fileName arguments operationName =
        try
            let startInfo = System.Diagnostics.ProcessStartInfo(
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            )
            use proc = System.Diagnostics.Process.Start(startInfo)
            proc.WaitForExit(30000) |> ignore
            if proc.ExitCode <> 0 then
                let err = proc.StandardError.ReadToEnd()
                Error $"Failed to {operationName}: {err}"
            else
                Ok ()
        with ex ->
            Error $"Exception in {operationName}: {ex.Message}"

    let private runIp arguments operationName =
        runCommand "/sbin/ip" arguments operationName

    let private truncateName (name : string) =
        if name.Length >= IFNAMSIZ then name.Substring(0, IFNAMSIZ - 1)
        else name

    // ============================================================
    // LinuxTunAdapter implementation
    // ============================================================

    type LinuxTunAdapter(fd : int, ifName : string) =
        let mutable disposed = false
        let mutable sessionActive = false
        let mutable pollThread : Thread option = None
        let mutable pollThreadRunning = false
        let readEvent = new ManualResetEvent(false)
        let pollStopEvent = new ManualResetEvent(false)

        let pollLoop () =
            let mutable fds = [| PollFd(fd = fd, events = POLLIN, revents = 0s) |]
            while pollThreadRunning do
                let result = poll(fds, 1UL, 100)
                if result > 0 && (fds[0].revents &&& POLLIN) <> 0s then
                    readEvent.Set() |> ignore
                fds[0].revents <- 0s

        interface ITunAdapter with
            member _.StartSession() =
                if sessionActive then
                    Error "Session already active"
                else
                    sessionActive <- true
                    pollThreadRunning <- true
                    pollStopEvent.Reset() |> ignore
                    let thread = Thread(ThreadStart(pollLoop), IsBackground = true, Name = "TunPollThread")
                    thread.Start()
                    pollThread <- Some thread
                    Ok ()

            member _.EndSession() =
                if sessionActive then
                    pollThreadRunning <- false
                    pollStopEvent.Set() |> ignore
                    match pollThread with
                    | Some t ->
                        t.Join(1000) |> ignore
                        pollThread <- None
                    | None -> ()
                    sessionActive <- false

            member _.GetReadWaitEvent() = IntPtr.Zero

            member _.IsSessionActive = sessionActive

            member _.GetReadWaitHandle() =
                if sessionActive then readEvent :> WaitHandle
                else null

            member _.ReceivePacket() =
                if not sessionActive then
                    None
                else
                    let buffer = Array.zeroCreate<byte> 65535
                    let bytesRead = read(fd, buffer, nativeint buffer.Length)
                    if bytesRead > 0n then
                        readEvent.Reset() |> ignore
                        let packet = Array.zeroCreate<byte> (int bytesRead)
                        Array.Copy(buffer, packet, int bytesRead)
                        Some packet
                    else
                        None

            member _.SendPacket(packet : byte[]) =
                if not sessionActive then
                    Error "No active session"
                else
                    let written = write(fd, packet, nativeint packet.Length)
                    if written < 0n then
                        let errno = getErrno()
                        Error $"Write failed with errno {errno}"
                    elif int written <> packet.Length then
                        Error $"Partial write: {written} of {packet.Length} bytes"
                    else
                        Ok ()

            member _.SetIpAddress(ipAddress : IpAddress) (subnetMask : IpAddress) =
                let cidr =
                    let parts = subnetMask.value.Split('.')
                    if parts.Length <> 4 then 24
                    else
                        parts
                        |> Array.sumBy (fun p ->
                            match Byte.TryParse(p) with
                            | true, b ->
                                let mutable count = 0
                                let mutable v = b
                                while v <> 0uy do
                                    count <- count + int (v &&& 1uy)
                                    v <- v >>> 1
                                count
                            | false, _ -> 0)
                runIp $"addr add {ipAddress.value}/{cidr} dev {ifName}" "set IP address"
                |> Result.bind (fun () -> runIp $"link set dev {ifName} up" "bring interface up")

            member _.SetDnsServer(_ : IpAddress) =
                Ok ()

            member _.AddRoute(destination : IpAddress) (mask : IpAddress) (gateway : IpAddress) (metric : int) =
                let cidr =
                    let parts = mask.value.Split('.')
                    if parts.Length <> 4 then 24
                    else
                        parts
                        |> Array.sumBy (fun p ->
                            match Byte.TryParse(p) with
                            | true, b ->
                                let mutable count = 0
                                let mutable v = b
                                while v <> 0uy do
                                    count <- count + int (v &&& 1uy)
                                    v <- v >>> 1
                                count
                            | false, _ -> 0)
                runIp $"route add {destination.value}/{cidr} via {gateway.value} dev {ifName} metric {metric}" "add route"

            member _.SetInterfaceMetric(_ : int) =
                Ok ()

            member _.SetMtu(mtu : int) =
                if mtu < 576 || mtu > 9000 then
                    Error $"Invalid MTU: {mtu}"
                else
                    runIp $"link set dev {ifName} mtu {mtu}" "set MTU"

            member this.Dispose() =
                if not disposed then
                    (this :> ITunAdapter).EndSession()
                    close(fd) |> ignore
                    readEvent.Dispose()
                    pollStopEvent.Dispose()
                    disposed <- true

    // ============================================================
    // Creator function
    // ============================================================

    let create (name : string) (_tunnelType : string) (_guid : Guid option) : Result<ITunAdapter, string> =
        let tunPath = "/dev/net/tun"
        let fd = ``open``(tunPath, O_RDWR)
        if fd < 0 then
            let errno = getErrno()
            Error $"Failed to open {tunPath}: errno {errno}"
        else
            let truncatedName = truncateName name
            let nameBytes = Array.zeroCreate<byte> IFNAMSIZ
            let srcBytes = System.Text.Encoding.ASCII.GetBytes(truncatedName)
            Array.Copy(srcBytes, nameBytes, min srcBytes.Length (IFNAMSIZ - 1))

            let mutable ifr = IfReq(
                ifrName = nameBytes,
                ifrFlags = (IFF_TUN ||| IFF_NO_PI),
                ifrPadding = (Array.zeroCreate<byte> 22)
            )

            let result = ioctl(fd, TUNSETIFF, &ifr)
            if result < 0 then
                let errno = getErrno()
                close(fd) |> ignore
                Error $"ioctl TUNSETIFF failed: errno {errno}"
            else
                let actualName =
                    let idx = Array.IndexOf(ifr.ifrName, 0uy)
                    let len = if idx < 0 then ifr.ifrName.Length else idx
                    System.Text.Encoding.ASCII.GetString(ifr.ifrName, 0, len)
                Ok (new LinuxTunAdapter(fd, actualName) :> ITunAdapter)
