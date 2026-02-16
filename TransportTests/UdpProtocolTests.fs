namespace Softellect.Tests.TransportTests

open System
open Xunit
open FluentAssertions
open Softellect.Transport.UdpProtocol

module UdpProtocolTests =

    [<Fact>]
    let ``buildPushDatagram roundtrip`` () =
        let sessionId = PushSessionId 42uy
        let nonce = Guid.NewGuid()
        let payload = [| 1uy; 2uy; 3uy; 4uy; 5uy |]
        let datagram = buildPushDatagram sessionId nonce payload
        match tryParsePushDatagram datagram with
        | Ok (sid, n, p) ->
            sid.value.Should().Be(42uy, "") |> ignore
            n.Should().Be(nonce, "") |> ignore
            p.Should().BeEquivalentTo(payload, "") |> ignore
        | Error e -> failwith $"Parse failed: {e}"

    [<Fact>]
    let ``buildPushDatagram maxPayload`` () =
        let sessionId = PushSessionId 1uy
        let nonce = Guid.NewGuid()
        let payload = Array.zeroCreate PushMaxPayload
        let datagram = buildPushDatagram sessionId nonce payload
        datagram.Length.Should().Be(PushMtu, "") |> ignore

    [<Fact>]
    let ``buildPushDatagram oversize throws`` () =
        let sessionId = PushSessionId 1uy
        let nonce = Guid.NewGuid()
        let payload = Array.zeroCreate (PushMaxPayload + 1)
        Assert.ThrowsAny<Exception>(fun () -> buildPushDatagram sessionId nonce payload |> ignore) |> ignore

    [<Fact>]
    let ``tryParsePushDatagram tooShort`` () =
        let data = Array.zeroCreate (PushHeaderSize - 1)
        match tryParsePushDatagram data with
        | Error _ -> ()
        | Ok _ -> failwith "Should have returned Error for short data"

    [<Fact>]
    let ``buildPayload roundtrip`` () =
        let cmd = PushCmdData
        let data = [| 10uy; 20uy; 30uy |]
        let payload = buildPayload cmd data
        match tryParsePayload payload with
        | Ok (c, d) ->
            c.Should().Be(cmd, "") |> ignore
            d.Should().BeEquivalentTo(data, "") |> ignore
        | Error _ -> failwith "Parse failed"

    [<Fact>]
    let ``tryParsePayload empty`` () =
        match tryParsePayload [||] with
        | Error _ -> ()
        | Ok _ -> failwith "Should have returned Error for empty payload"

    [<Fact>]
    let ``derivePacketAesKey deterministic`` () =
        let sessionKey = Array.init 32 (fun i -> byte i)
        let nonce = Guid.NewGuid()
        let key1 = derivePacketAesKey sessionKey nonce
        let key2 = derivePacketAesKey sessionKey nonce
        key1.key.Should().BeEquivalentTo(key2.key, "") |> ignore
        key1.iv.Should().BeEquivalentTo(key2.iv, "") |> ignore

    [<Fact>]
    let ``derivePacketAesKey different nonces`` () =
        let sessionKey = Array.init 32 (fun i -> byte i)
        let key1 = derivePacketAesKey sessionKey (Guid.NewGuid())
        let key2 = derivePacketAesKey sessionKey (Guid.NewGuid())
        (key1.key = key2.key).Should().BeFalse("") |> ignore

    [<Fact>]
    let ``derivePacketAesKey keyLength`` () =
        let sessionKey = Array.init 32 (fun i -> byte i)
        let nonce = Guid.NewGuid()
        let aesKey = derivePacketAesKey sessionKey nonce
        aesKey.key.Length.Should().Be(32, "") |> ignore
        aesKey.iv.Length.Should().Be(16, "") |> ignore

    [<Fact>]
    let ``buildPushDatagram with various sessionIds`` () =
        for b in [0uy; 1uy; 127uy; 255uy] do
            let sessionId = PushSessionId b
            let nonce = Guid.NewGuid()
            let payload = [| 99uy |]
            let datagram = buildPushDatagram sessionId nonce payload
            match tryParsePushDatagram datagram with
            | Ok (sid, n, _) ->
                sid.value.Should().Be(b, "") |> ignore
                n.Should().Be(nonce, "") |> ignore
            | Error e -> failwith $"Failed for sessionId {b}: {e}"
