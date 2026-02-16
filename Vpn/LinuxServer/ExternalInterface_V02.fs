namespace Softellect.Vpn.Server

open System
open System.IO
open System.Net
open System.Runtime.InteropServices
open System.Threading
open Microsoft.Win32.SafeHandles

open Softellect.Sys.Logging
open Softellect.Vpn.Core.PacketDebug
open Softellect.Transport.UdpProtocol

/// Linux external interface using TUN.
/// Decrypted IP packets are written to TUN.
/// Kernel handles TCP/UDP + routing + NAT.
/// Packets from the internet come back via TUN and are read here.
module ExternalInterface =

    // ============================
    // Native TUN/TAP interop
    // ============================

    [<Literal>]
    let private O_RDWR = 0x0002

    // From <linux/if_tun.h>
    [<Literal>]
    let private TUNSETIFF = 0x400454caUL

    [<Literal>]
    let private IFF_TUN = 0x0001s

    [<Literal>]
    let private IFF_NO_PI = 0x1000s

    // ifreq: 40 bytes on x86_64
    [<Struct; StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)>]
    type private ifreq =
        [<MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)>]
        val mutable ifr_name : string

        val mutable ifr_flags : int16

        [<MarshalAs(UnmanagedType.ByValArray, SizeConst = 22)>]
        val mutable ifr_pad : byte[]

        new (name: string, flags: int16) =
            { ifr_name = name
              ifr_flags = flags
              ifr_pad = Array.zeroCreate 22 }

    module private Native =
        // "open" is an F# keyword → rename via EntryPoint
        [<DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi, EntryPoint = "open")>]
        extern int openFile(string pathname, int flags)

        [<DllImport("libc", SetLastError = true, EntryPoint = "close")>]
        extern int closeFile(int fd)

        [<DllImport("libc", SetLastError = true, EntryPoint = "ioctl")>]
        extern int ioctl(int fd, uint64 request, nativeint argp)

    let private throwLastError prefix =
        let err = Marshal.GetLastWin32Error()
        raise (IOException($"{prefix} (errno={err})"))

    let private createTunDevice (tunName: string) : FileStream =
        let fd = Native.openFile("/dev/net/tun", O_RDWR)
        if fd < 0 then
            throwLastError "Failed to open /dev/net/tun"

        let ifr = ifreq(tunName, (IFF_TUN ||| IFF_NO_PI))
        let size = Marshal.SizeOf<ifreq>()
        let ptr = Marshal.AllocHGlobal(size)

        try
            Marshal.StructureToPtr(ifr, ptr, false)

            let rc = Native.ioctl(fd, TUNSETIFF, ptr)
            if rc < 0 then
                Native.closeFile(fd) |> ignore
                throwLastError $"ioctl(TUNSETIFF) failed for '{tunName}'"

            let safe = new SafeFileHandle(nativeint fd, ownsHandle = true)
            new FileStream(safe, FileAccess.ReadWrite, 64 * 1024, isAsync = false)
        finally
            Marshal.FreeHGlobal(ptr)

    // ============================
    // External config (UNCHANGED)
    // ============================

    type ExternalConfig =
        {
            serverPublicIp : IPAddress
        }

    // ============================
    // External Gateway (TUN)
    // ============================

    type ExternalGateway(config: ExternalConfig) =

        let mutable running = false
        let mutable onPacketCallback : (byte[] -> unit) option = None
        let mutable tunStream : FileStream option = None

        // Derive a deterministic TUN name from server IP
        // e.g. vpn_216_219_95_164
        let tunName =
            let b = config.serverPublicIp.GetAddressBytes()
            $"vpn_{b[0]}_{b[1]}_{b[2]}_{b[3]}"

        // Stats
        let mutable totalRead = 0L
        let mutable totalWritten = 0L
        let mutable readErrors = 0L
        let mutable writeErrors = 0L

        let statsStopwatch = Diagnostics.Stopwatch()

        let logStatsIfDue () =
            if statsStopwatch.ElapsedMilliseconds >= PushStatsIntervalMs then
                let r  = Interlocked.Read(&totalRead)
                let w  = Interlocked.Read(&totalWritten)
                let re = Interlocked.Read(&readErrors)
                let we = Interlocked.Read(&writeErrors)
                Logger.logInfo $"ExternalGateway(Linux/TUN) stats: read={r}, written={w}, readErr={re}, writeErr={we}"
                statsStopwatch.Restart()

        let queue (f: unit -> unit) =
            ThreadPool.UnsafeQueueUserWorkItem((fun _ -> f()), null) |> ignore

        let rec readLoop () =
            if running then
                match tunStream with
                | None -> ()
                | Some s ->
                    try
                        let buf = Array.zeroCreate<byte> 65535
                        let n = s.Read(buf, 0, buf.Length)

                        if n <= 0 then
                            running <- false
                        else
                            Interlocked.Increment(&totalRead) |> ignore

                            let packet = Array.zeroCreate<byte> n
                            Buffer.BlockCopy(buf, 0, packet, 0, n)

                            Logger.logTrace (fun () ->
                                $"HEAVY LOG (Linux) - Read {n} bytes from TUN '{tunName}', packet: {(summarizePacket packet)}.")

                            match onPacketCallback with
                            | Some cb -> cb packet
                            | None -> ()

                            logStatsIfDue()
                            queue readLoop
                    with ex ->
                        if running then
                            Interlocked.Increment(&readErrors) |> ignore
                            Logger.logError $"ExternalGateway(Linux/TUN) read error: {ex.Message}"
                        logStatsIfDue()
                        queue readLoop

        member _.start(onPacketFromInternet: byte[] -> unit) =
            if running then
                Logger.logWarn "ExternalGateway(Linux/TUN) already running"
            else
                let s = createTunDevice tunName
                tunStream <- Some s
                onPacketCallback <- Some onPacketFromInternet
                running <- true
                statsStopwatch.Restart()

                Logger.logInfo $"ExternalGateway(Linux/TUN): created/attached '{tunName}'"
                Logger.logInfo "ExternalGateway(Linux/TUN) started"

                queue readLoop

        /// Send a decrypted IP packet toward the internet (via kernel routing/NAT)
        member _.sendOutbound(packet: byte[]) =
            if packet.Length < 20 then
                Logger.logWarn "ExternalGateway(Linux/TUN).sendOutbound: Packet too short (<20), dropping"
            else
                match tunStream with
                | None ->
                    Logger.logError "ExternalGateway(Linux/TUN).sendOutbound: TUN not started"
                | Some s ->
                    try
                        s.Write(packet, 0, packet.Length)
                        Interlocked.Increment(&totalWritten) |> ignore
                        Logger.logTrace (fun () ->
                            $"HEAVY LOG (Linux) - Wrote {packet.Length} bytes to TUN '{tunName}', packet: {(summarizePacket packet)}.")
                        logStatsIfDue()
                    with ex ->
                        Interlocked.Increment(&writeErrors) |> ignore
                        Logger.logError $"ExternalGateway(Linux/TUN).sendOutbound failed: {(summarizePacket packet)}, exception='{ex.Message}'"
                        logStatsIfDue()

        member _.stop() =
            Logger.logInfo "ExternalGateway(Linux/TUN) stopping"
            running <- false
            onPacketCallback <- None

            match tunStream with
            | Some s ->
                try s.Flush() with _ -> ()
                try s.Dispose() with _ -> ()
                tunStream <- None
            | None -> ()

            Logger.logInfo "ExternalGateway(Linux/TUN) stopped"

        interface IDisposable with
            member this.Dispose() = this.stop()
