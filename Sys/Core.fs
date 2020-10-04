namespace Softellect.Sys

open System
open System.Threading.Tasks
open System.IO
open System.IO.Compression
open System.Text
open MBrace.FsPickler
open Newtonsoft.Json

open Softellect.Sys.GeneralErrors
open Softellect.Sys.Primitives
open Softellect.Sys.Logging

/// Collection of various low level functions, extension methods, and system types.
module Core =

    let toVariableName (s : string) =
        match s.Length with
        | 0 -> s
        | 1 -> s.ToLower()
        | _ -> s.Substring(0, 1).ToLower() + s.Substring(1)


    let zipBytes (b : byte[]) =
        use i = new MemoryStream(b)
        use o = new MemoryStream()
        use g = new GZipStream(o, CompressionMode.Compress)
        i.CopyTo(g, 4096)
        i.Close()
        g.Close()
        o.Close()
        o.ToArray()


    let unZipBytes (b : byte[]) =
        use i = new MemoryStream(b)
        use g = new GZipStream(i, CompressionMode.Decompress)
        use o = new MemoryStream()
        g.CopyTo(o, 4096)
        g.Close()
        i.Close()
        o.Close()
        let b = o.ToArray()
        b

    let toByteArray (s : string) = s |> Encoding.UTF8.GetBytes
    let fromByteArray (b : byte[]) = b |> Encoding.UTF8.GetString
    let zip (s : string) = s |> toByteArray |> zipBytes
    let unZip (b : byte[]) = b |> unZipBytes |> fromByteArray


    let toAsync (f : unit-> unit) = async { do f() }


    let formatTimeSpan (t : TimeSpan) =
        let x = sprintf "%i:%02i:%02i" t.Hours t.Minutes t.Seconds

        if t.Days = 0
        then x
        else sprintf "%i day(s), %s" t.Days x


    let estimateEndTime progress (started : DateTime) =
        if progress > 0.0m && progress <= 1.0m
        then
            let estRunTime = (decimal (DateTime.Now.Subtract(started).Ticks)) / progress |> int64 |> TimeSpan.FromTicks
            started.Add estRunTime |> Some
        else None


    let partition maxVal q n =
        let (a, b) =
            q
            |> List.mapi (fun i e -> (i + n + 1, e))
            |> List.partition (fun (i, _) -> i <= maxVal)

        (a |> List.map snd, b |> List.map snd)


    /// http://www.fssnip.net/1T/title/Remove-first-ocurrence-from-list.
    /// Removes first occurrence from the list when the element matches a given predicate.
    let rec removeFirst pred lst =
        match lst with
        | [] -> []
        | h :: t when pred h -> t
        | h :: t -> h :: removeFirst pred t


    /// Gets the full file name located in the folder where the assembly normally resides on disk or the install directory.
    /// See:
    ///     https://stackoverflow.com/questions/278761/is-there-a-net-framework-method-for-converting-file-uris-to-paths-with-drive-le
    ///     https://stackoverflow.com/questions/837488/how-can-i-get-the-applications-path-in-a-net-console-application
    ///     https://stackoverflow.com/questions/52797/how-do-i-get-the-path-of-the-assembly-the-code-is-in
    let getFileName fileName =
        let x = Uri(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().CodeBase)).LocalPath
        x + @"\" + fileName


    let private xmlSerializer = FsPickler.CreateXmlSerializer(indent = true)
    let xmlSerialize t = xmlSerializer.PickleToString t
    let xmlDeserialize s = xmlSerializer.UnPickleOfString s


    let private binSerializer = FsPickler.CreateBinarySerializer()
    let binSerialize t = binSerializer.Pickle t
    let binDeserialize b = binSerializer.UnPickle b


    let jsonSerialize t = JsonConvert.SerializeObject t
    let jsonDeserialize<'T> s = JsonConvert.DeserializeObject<'T> s


    let serialize f t =
        match f with
        | BinaryFormat -> t |> binSerialize
        | BinaryZippedFormat -> t |> binSerialize |> zipBytes
        | JSonFormat -> t |> jsonSerialize |> toByteArray
        | XmlFormat -> t |> xmlSerialize |> toByteArray


    let deserialize f b =
        match f with
        | BinaryFormat -> b |> binDeserialize
        | BinaryZippedFormat -> b |> unZipBytes |> binDeserialize
        | JSonFormat -> b |> fromByteArray |> jsonDeserialize
        | XmlFormat -> b |> fromByteArray |> xmlDeserialize


    let trySerialize<'A> f (a : 'A) : Result<byte[], SerializationError> =
        try
            let b = serialize f a
            Ok b
        with
        | e ->
            printfn "trySerialize: Exception: '%A'." e
            e |> SerializationExn |> Error


    /// https://stackoverflow.com/questions/2361851/c-sharp-and-f-casting-specifically-the-as-keyword
    let tryCastAs<'T> (o:obj) : 'T option =
        //printfn "tryCastAs: typeof<'T> = '%A', o.GetType() = '%A'." typeof<'T> (o.GetType())
        match o with
        | :? 'T as res -> Some res
        | _ -> None


    let tryDeserialize<'A> f (b : byte[]) : Result<'A, SerializationError> =
        try
            let (y : 'A) = deserialize f b
            Ok y
        with
        | e ->
            printfn "tryDeserialize: Exception: '%A'." e
            e |> DeserializationExn |> Error


    let reply (r : AsyncReplyChannel<'T>) result = r.Reply result


    /// Replies with result and returns the state.
    /// It is used by MailboxProcessor based classes to standardize approach for PostAndReply.
    let withReply (r : AsyncReplyChannel<'T>) (s, result) =
        r.Reply result
        s


    let withoutReply (s, _) = s


    let combine (b, (s: string)) (x, e) =
        let r =
            match b, x with
            | false, false -> s + ", " + e
            | true, false -> e
            | false, true -> s
            | true, true -> EmptyString
        b && x, r


    let tryExecute g f =
        try
            g()
        with
        | e -> f e


    /// http://www.fssnip.net/iW/title/Oneliner-generic-timing-function
    let time f a = System.Diagnostics.Stopwatch.StartNew() |> (fun sw -> (f a, sw.Elapsed))


    let timedImplementation<'E, 'A> b (l : Logger<'E>) name (f : unit -> 'A) =
        let (r, t) = time f ()

        if t.TotalSeconds <= 10.0
        then
            if b then l.logInfoString (sprintf "%s: Execution time: %A" name t)
        else l.logInfoString (sprintf "%s: !!! LARGE Execution time: %A" name t)

        r


    type IUpdater<'I, 'A, 'S> =
        abstract member init : 'I -> 'S
        abstract member add : 'A -> 'S -> 'S


    type Updater<'T> = MailboxProcessor<'T>


    type UpdatableStorage<'A, 'S> =
        | AddContent of 'A
        | GetContent of AsyncReplyChannel<'S>


    type AsyncUpdater<'I, 'A, 'S> (updater : IUpdater<'I, 'A, 'S>, i : 'I) =
        let chat = Updater.Start(fun u ->
          let rec loop s = async {
            let! m = u.Receive()

            match m with
            | AddContent a -> return! loop (updater.add a s)
            | GetContent r ->
                r.Reply s
                return! loop s }

          updater.init i |> loop)

        member _.addContent p = AddContent p |> chat.Post
        member _.getContent () = chat.PostAndReply GetContent


    type Map<'k, 'v when 'k : comparison>
        with

        /// Tries to remove a given key from the map.
        /// If found, then returns a new map with the key removed.
        /// If not found, then returns the orignial map.
        member m.tryRemove k =
            match m.ContainsKey k with
            | true -> m.Remove k
            | false -> m


        /// Tries to get value out of the map OR returns a given default value if there is none.
        member m.getValueOrDefault k v = m |> Map.tryFind k |> Option.defaultValue v


    type Queue<'A> =
        | Queue of 'A list * 'A list


    let emptyQueue = Queue([], [])


    let enqueue q e =
        match q with
        | Queue(fs, bs) -> Queue(e :: fs, bs)


    let tryDequeue q =
        match q with
        | Queue([], []) -> None, q
        | Queue(fs, b :: bs) -> Some b, Queue(fs, bs)
        | Queue(fs, []) ->
            let bs = List.rev fs
            Some bs.Head, Queue([], bs.Tail)


    type List<'T>
        with
        static member mapWhileSome mapper tList =
            let rec doMap x acc =
                match x with
                | [] -> acc
                | h :: t ->
                    match mapper h with
                    | Some u -> doMap t (u :: acc)
                    | None -> acc

            doMap tList []
            |> List.rev


        static member mapWhileSomeAsync mapper tList =
            async {
                let rec doMap x acc =
                    async {
                        match x with
                        | [] -> return acc
                        | h :: t ->
                            match! mapper h with
                            | Some u -> return! doMap t (u :: acc)
                            | None -> return acc
                    }

                let! lst = doMap tList []
                return lst |> List.rev
            }


        static member mapAsync mapper tList =
            async {
                let x = tList |> List.map (fun e -> async { return! mapper e} )
                let rec doMap x acc =
                    async {
                        match x with
                        | [] -> return acc
                        | h :: t ->
                            let! u = mapper h
                            return! doMap t (u :: acc)
                    }

                let! lst = doMap tList []
                return lst |> List.rev
            }


        static member foldWhileSome mapper tList seed =
            //printfn "foldWhileSome: seed = %A" seed

            let rec doFold x acc =
                //printfn "    foldWhileSome: acc = %A" acc
                match x with
                | [] -> acc
                | h :: t ->
                    match mapper acc h with
                    | Some u -> doFold t u
                    | None -> acc

            let y = doFold tList seed
            //printfn "    foldWhileSome - completed: y = %A" y
            y


        static member foldWhileSomeAsync mapper tList seed =
            async {
                //printfn "foldAsyncWhileSome: seed = %A" seed

                let rec doFold x acc =
                    async {
                        //printfn "    foldAsyncWhileSome: acc = %A" acc
                        match x with
                        | [] -> return acc
                        | h :: t ->
                            match! mapper acc h with
                            | Some u -> return! doFold t u
                            | None -> return acc
                    }

                let! y = doFold tList seed
                //printfn "    foldAsyncWhileSome - completed: y = %A" y
                return y
            }


    /// http://www.fssnip.net/7Rc/title/AsyncAwaitTaskCorrect
    type Async with
        static member AwaitTaskCorrect(task : Task) : Async<unit> =
            Async.FromContinuations(fun (sc, ec, cc) ->
                task.ContinueWith(fun (task:Task) ->
                    if task.IsFaulted then
                        let e = task.Exception
                        if e.InnerExceptions.Count = 1 then ec e.InnerExceptions.[0]
                        else ec e
                    elif task.IsCanceled then
                        ec(TaskCanceledException())
                    else
                        sc ())
                |> ignore)

        static member AwaitTaskCorrect(task : Task<'T>) : Async<'T> =
            Async.FromContinuations(fun (sc, ec, cc) ->
                task.ContinueWith(fun (task:Task<'T>) ->
                    if task.IsFaulted then
                        let e = task.Exception
                        if e.InnerExceptions.Count = 1 then ec e.InnerExceptions.[0]
                        else ec e
                    elif task.IsCanceled then
                        ec(TaskCanceledException())
                    else
                        sc task.Result)
                |> ignore)
