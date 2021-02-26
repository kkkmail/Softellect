namespace Softellect.Sys

/// https://fsharpforfunandprofit.com/posts/recipe-part2/
module Rop =

    type UnitResult<'E> = Result<unit, 'E>
    type ListResult<'T, 'E> = Result<list<Result<'T, 'E>>, 'E>
    type StateWithResult<'T, 'E> = 'T * UnitResult<'E>


    /// convert a single value into a two-track result
    let succeed x = Ok x


    /// convert a single value into a two-track result
    let fail x = Error x


    /// apply either a success function or failure function
    let either successFunc failureFunc twoTrackInput =
        match twoTrackInput with
        | Ok s -> successFunc s
        | Error f -> failureFunc f


    /// convert a switch function into a two-track function
    let bind g = either g fail


    /// Converts a regular "success" function into a two-track function.
    let bindSuccess g r =
        match r with
        | Ok s -> Ok (g s)
        | Error e -> Error e


    let bindSuccessOption g w r =
        match r with
        | Ok (Some s) -> Ok (g s)
        | Ok None -> w
        | Error e -> Error e


    let bindErrOption g f r =
        match r with
        | Ok (Some s) -> Ok (g s)
        | Ok None -> Error f
        | Error e -> Error e


    /// Calls a continuation function in case of an error.
    let bindError f r =
        match r with
        | Ok s -> Ok s
        | Error e -> f e


    /// pipe a two-track value into a switch function
    let (>>=) x f = bind f x


    /// compose two switches into another switch
    let (>=>) s1 s2 = s1 >> bind s2


    /// convert a one-track function into a switch
    let switch f = f >> succeed


    /// convert a one-track function into a two-track function
    let map g = either (g >> succeed) fail


    /// Changes success value into another success value.
    /// This is useful when remapping UnitResult into something else.
    let mapSuccessValue x v =
        match v with
        | Ok _ -> Ok x
        | Error e -> Error e


    let mapFailure f = either succeed (f >> fail)

    /// convert a dead-end function into a one-track function
    let tee f x = f x; x


    /// convert a one-track function into a switch with exception handling
    let tryCatch g exnHandler x =
        try
            g x |> succeed
        with
        | ex -> exnHandler ex |> fail


    /// convert two one-track functions into a two-track function
    let doubleMap successFunc failureFunc =
        either (successFunc >> succeed) (failureFunc >> fail)


    /// add two switches in parallel
    let plus addSuccess addFailure switch1 switch2 x =
        match (switch1 x), (switch2 x) with
        | Ok s1, Ok s2 -> Ok (addSuccess s1 s2)
        | Error f1, Ok _ -> Error f1
        | Ok _, Error f2 -> Error f2
        | Error f1, Error f2 -> Error (addFailure f1 f2)


    /// Unwraps Result<List<Result<'A, 'B>>, 'C> into a list of successes only using given continuation functions.
    /// Use it for replacing failures with some default values.
    let unwrapAll a b r =
        match r with
        | Ok v -> v |> List.map (fun e -> match e with | Ok x -> Some x | Error f -> b f)
        | Error f -> a f
        |> List.choose id


    /// Same as above but extracts only successes.
    /// Use it for logging the failures.
    let unwrapSuccess a b r =
        match r with
        | Ok v ->
            v
            |> List.map (fun e ->
                    match e with
                    | Ok x -> Some x
                    | Error f ->
                        b f |> ignore
                        None)
        | Error f ->
            a f |> ignore
            []
        |> List.choose id


    /// Splits the list of results into list of successes and list of failures.
    let unzip r =
        let success e =
            match e with
            | Ok v -> Some v
            | Error _ -> None

        let failure e =
            match e with
            | Ok _ -> None
            | Error e -> Some e

        let sf e = success e, failure e
        let s, f = r |> List.map sf |> List.unzip
        s |> List.choose id, f |> List.choose id


    /// Updates the state using success or failure.
    let x successFunc failureFunc state twoTrackInput =
        match twoTrackInput with
        | Ok v -> successFunc state v
        | Error f -> failureFunc state f


    /// Updates the state if success and calls failure function (logger) in case of failure.
    let y successFunc failureFunc state twoTrackInput =
        match twoTrackInput with
        | Ok v -> successFunc state v
        | Error f ->
            failureFunc f
            state


    let mapListResult g r =
        match r with
        | Ok v -> v |> List.map (fun e -> bindSuccess g e) |> Ok
        | Error e -> Error e


    let unzipListResult r =
        match r with
        | Ok v -> v |> unzip
        | Error e -> [], [e]


    /// Unwraps things like Result<Result<RunQueue, ClmError> option, ClmError>
    /// into Result<RunQueue option, ClmError>
    let unwrapResultOption r =
        match r with
        | Ok w ->
            match w with
            | Some x ->
                match x with
                | Ok a -> a |> Some |> Ok
                | Error e -> Error e
            | None -> Ok None
        | Error e -> Error e


    /// Applies state modifying function to the list of items while the function returns Ok().
    let foldWhileOk f a s =
        let rec inner rem w =
            match rem with
            | [] -> w, Ok()
            | h :: t ->
                match f w h with
                | w1, Ok() -> inner t w1
                | w1, Error e -> w1, Error e

        inner a s


    /// Folds list of errors: list<'E> into a single 'E.
    /// The head should contain the latest error.
    let foldErrors adder a =
        match a with
        | [] -> None
        | h :: t -> t |> List.fold (fun acc r -> adder r acc) h |> Some


    /// Converts an error option into a unit result.
    let toUnitResult eo : UnitResult<'E> =
        match eo with
        | None -> Ok()
        | Some e -> Error e


    /// Folds list of errors: list<'E> into a single 'E, then converts it into UnitResult<'E>.
    let foldToUnitResult adder x = (foldErrors adder >> toUnitResult) x


    /// Adds error f if the result is (Error e).
    /// Otherwise returns then same (Ok r).
    let addError adder v f =
        match v with
        | Ok r -> Ok r
        | Error e -> Error (adder f e)


    /// Converts Result into Option type.
    let toOption v =
        match v with
        | Ok r -> Some r
        | Error _ -> None


    /// The first result r1 is an earlier result and the second result r2 is a later result,
    /// so that we can partially apply the first result.
    /// And we want to sum up errors as (e2 + e1), in order to keep
    /// the latest error at the beginning.
    let combineUnitResults adder r1 r2 =
        match r1, r2 with
        | Ok(), Ok() -> Ok()
        | Error e1, Ok() -> Error e1
        | Ok(), Error e2 -> Error e2
        | Error e1, Error e2 -> Error (adder e2 e1)


    let toErrorOption adder f g r =
        match r with
        | Ok() -> None
        | Error e -> Some (adder (f g) e)


    /// The head should contain the latest result.
    let foldUnitResults adder r =
        let rec fold acc w =
            match w with
            | [] -> acc
            | h :: t -> fold (combineUnitResults adder h acc) t

        fold (Ok()) r
