namespace Softellect.Tests.TransportTests

open Xunit
open FluentAssertions
open Softellect.Transport.UdpProtocol

module BoundedPacketQueueTests =

    [<Fact>]
    let ``enqueue dequeue single`` () =
        let q = BoundedPacketQueue(1024, 100)
        let packet = [| 1uy; 2uy; 3uy |]
        q.enqueue(packet).Should().BeTrue("") |> ignore
        match q.tryDequeue() with
        | Some p -> p.Should().BeEquivalentTo(packet, "") |> ignore
        | None -> failwith "Expected a packet"

    [<Fact>]
    let ``enqueue dequeue fifo`` () =
        let q = BoundedPacketQueue(1024, 100)
        let p1 = [| 1uy |]
        let p2 = [| 2uy |]
        let p3 = [| 3uy |]
        q.enqueue(p1) |> ignore
        q.enqueue(p2) |> ignore
        q.enqueue(p3) |> ignore
        (q.tryDequeue().Value).Should().BeEquivalentTo(p1, "") |> ignore
        (q.tryDequeue().Value).Should().BeEquivalentTo(p2, "") |> ignore
        (q.tryDequeue().Value).Should().BeEquivalentTo(p3, "") |> ignore

    [<Fact>]
    let ``tryDequeue empty`` () =
        let q = BoundedPacketQueue(1024, 100)
        q.tryDequeue().Should().BeNull("") |> ignore

    [<Fact>]
    let ``headDrop maxPackets`` () =
        let q = BoundedPacketQueue(1024, 2)
        q.enqueue([| 1uy |]) |> ignore
        q.enqueue([| 2uy |]) |> ignore
        q.enqueue([| 3uy |]) |> ignore  // should drop [1]
        q.count.Should().Be(2, "") |> ignore
        (q.tryDequeue().Value).[0].Should().Be(2uy, "") |> ignore

    [<Fact>]
    let ``headDrop maxBytes`` () =
        let q = BoundedPacketQueue(5, 100)
        q.enqueue([| 1uy; 2uy; 3uy |]) |> ignore  // 3 bytes
        q.enqueue([| 4uy; 5uy; 6uy |]) |> ignore  // would be 6 total, drop first -> 3 bytes
        q.count.Should().Be(1, "") |> ignore
        (q.tryDequeue().Value).[0].Should().Be(4uy, "") |> ignore

    [<Fact>]
    let ``oversizePacket rejected`` () =
        let q = BoundedPacketQueue(5, 100)
        let big = Array.zeroCreate 10
        q.enqueue(big).Should().BeFalse("") |> ignore
        q.count.Should().Be(0, "") |> ignore

    [<Fact>]
    let ``dequeueMany partial`` () =
        let q = BoundedPacketQueue(1024, 100)
        q.enqueue([| 1uy |]) |> ignore
        q.enqueue([| 2uy |]) |> ignore
        let result = q.dequeueMany(5)
        result.Length.Should().Be(2, "") |> ignore

    [<Fact>]
    let ``dequeueMany limit`` () =
        let q = BoundedPacketQueue(1024, 100)
        for i in 0..9 do q.enqueue([| byte i |]) |> ignore
        let result = q.dequeueMany(3)
        result.Length.Should().Be(3, "") |> ignore
        q.count.Should().Be(7, "") |> ignore

    [<Fact>]
    let ``wait signaled`` () =
        let q = BoundedPacketQueue(1024, 100)
        q.enqueue([| 1uy |]) |> ignore
        q.wait(100).Should().BeTrue("") |> ignore

    [<Fact>]
    let ``wait timeout`` () =
        let q = BoundedPacketQueue(1024, 100)
        q.wait(50).Should().BeFalse("") |> ignore

    [<Fact>]
    let ``droppedCounters accurate`` () =
        let q = BoundedPacketQueue(5, 100)
        q.enqueue([| 1uy; 2uy; 3uy |]) |> ignore
        q.enqueue([| 4uy; 5uy; 6uy |]) |> ignore  // drops first (3 bytes)
        q.droppedPackets.Should().Be(1L, "") |> ignore
        q.droppedBytes.Should().Be(3L, "") |> ignore

    [<Fact>]
    let ``resetDropCounters`` () =
        let q = BoundedPacketQueue(5, 100)
        q.enqueue([| 1uy; 2uy; 3uy |]) |> ignore
        q.enqueue([| 4uy; 5uy; 6uy |]) |> ignore
        q.resetDropCounters()
        q.droppedPackets.Should().Be(0L, "") |> ignore
        q.droppedBytes.Should().Be(0L, "") |> ignore

    [<Fact>]
    let ``atomicCounter increment and add`` () =
        let c = AtomicCounter()
        c.increment()
        c.increment()
        c.add(5L)
        c.value.Should().Be(7L, "") |> ignore

    [<Fact>]
    let ``atomicCounter reset`` () =
        let c = AtomicCounter()
        c.add(42L)
        let prev = c.reset()
        prev.Should().Be(42L, "") |> ignore
        c.value.Should().Be(0L, "") |> ignore
