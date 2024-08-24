namespace Softellect.Sys

open System.Threading
open System

/// http://www.fssnip.net/bb/title/Exception-Retry-Computation-Expression
module Retry =

    type RetryParams =
        {
            maxRetries : int
            waitBetweenRetries : int
        }


    let defaultRetryParams =
        {
            maxRetries = 10
            waitBetweenRetries = 10_000
        }


    type RetryMonad<'a> = RetryParams -> 'a
    let rm<'a> (f : RetryParams -> 'a) : RetryMonad<'a> = f


    let internal retryFunc<'a> (f : RetryMonad<'a>) =
        rm (fun retryParams ->
            let rec execWithRetry f i e =
                match i with
                | n when n = retryParams.maxRetries -> raise e
                | _ ->
                    try
                        f retryParams
                    with
                        | e ->
                            Thread.Sleep(retryParams.waitBetweenRetries)
                            execWithRetry f (i + 1) e
            execWithRetry f 0 (Exception())
            )


    type RetryBuilder() =
        member this.Bind (p : RetryMonad<'a>, f : 'a -> RetryMonad<'b>) =
            rm (fun retryParams -> 
                let value = retryFunc p retryParams
                f value retryParams
            )

        member this.Return (x : 'a) = fun defaultRetryParams -> x
        member this.Run(m : RetryMonad<'a>) = m
        member this.Delay(f : unit -> RetryMonad<'a>) = f ()


    /// Builds a retry workflow using computation expression syntax.
    let retry = RetryBuilder()


    let tryFun g =
        try
            (retry {
                let! b = rm (fun _ -> g())
                return Ok b
            }) defaultRetryParams
        with
        | e -> Error e


    let tryRopFun f g =
        try
            (retry {
                let! b = rm (fun _ -> g())
                return b
            }) defaultRetryParams
        with
        | e -> e |> f |> Error


    let tryDbFun connectionString g = tryFun (fun () -> g connectionString)
    let tryDbRopFun connectionString f g = tryRopFun f (fun () -> g connectionString)
