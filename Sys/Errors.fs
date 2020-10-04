namespace Softellect.Sys

open Softellect.Sys.GeneralErrors
open Softellect.Sys.WcfErrors
open Softellect.Sys.TimerErrors
open Softellect.Sys.MessagingClientErrors
open Softellect.Sys.MessagingServiceErrors

module Errors =

    /// All errors known in the system.
    type Err<'E> =
        | AggregateErr of Err<'E> * List<Err<'E>>
        | TimerEventErr of TimerEventError
        | WcfErr of WcfError
        | MessagingServiceErr of MessagingServiceError
        | MessagingClientErr of MessagingClientError

        | OtherErr of 'E

        static member (+) (a, b) =
            match a, b with
            | AggregateErr (x, w), AggregateErr (y, z) -> AggregateErr (x, w @ (y :: z))
            | AggregateErr (x, w), _ -> AggregateErr (x, w @ [b])
            | _, AggregateErr (y, z) -> AggregateErr (a, y :: z)
            | _ -> AggregateErr (a, [b])

        member a.add b = a + b


    type StlResult<'T, 'E> = Result<'T, Err<'E>>
    type UnitResult<'E> = StlResult<unit, 'E>
    type ListResult<'T, 'E> = StlResult<list<StlResult<'T, 'E>>, 'E>
    type StateWithResult<'T, 'E> = 'T * UnitResult<'E>


    /// Folds list<Err<'E>> into a single Err<'E>.
    /// The head should contain the latest error.
    let foldErrors (a : list<Err<'E>>) =
        match a with
        | [] -> None
        | h :: t -> t |> List.fold (fun acc r -> r + acc) h |> Some


    /// Converts an error option into a unit result.
    let toUnitResult (fo : Err<'E> option) : UnitResult<'E> =
        match fo with
        | None -> Ok()
        | Some f -> Error f


    /// Folds list<Err<'E>>, then converts it to UnitResult<'E>.
    let foldToUnitResult x = (foldErrors >> toUnitResult) x


    /// Adds error f if the result is (Error e).
    /// Otherwise returns then same (Ok r).
    let addError v (f : Err<'E>) =
        match v with
        | Ok r -> Ok r
        | Error e -> Error (f + e)


    /// The first result r1 is an earlier result and the second result r2 is a later result,
    /// so that we can partially apply the first result.
    /// And we want to sum up errors as (e2 + e1), in order to keep
    /// the latest error at the beginning.
    let combineUnitResults (r1 : UnitResult<'E>) (r2 : UnitResult<'E>) =
        match r1, r2 with
        | Ok(), Ok() -> Ok()
        | Error e1, Ok() -> Error e1
        | Ok(), Error e2 -> Error e2
        | Error e1, Error e2 -> Error (e2 + e1)


    let toErrorOption f g (r : UnitResult<'E>) =
        match r with
        | Ok() -> None
        | Error e -> Some ((f g) + e)


    /// The head should contain the latest result.
    let foldUnitResults (r : list<UnitResult<'E>>) =
        let rec fold acc w =
            match w with
            | [] -> acc
            | h :: t -> fold (combineUnitResults h acc) t

        fold (Ok()) r

