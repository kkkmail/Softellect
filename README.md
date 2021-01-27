# Softellect.Wcf

`Softellect.Wcf` is a thin `NET5` wrapper around [CoreWcf](https://github.com/CoreWCF/CoreWCF) to simplify writing WCF client / server applications in a natural F# way. This is achieved by using two interfaces instead of one. The higher-level interface describes client - server communication in F# way by using immutable F# structures (records, discriminated unions, etc...). And the lower-level interface performs communication using [FsPicler](https://mbraceproject.github.io/FsPickler/) to serialize arbitrary native F# object into byte array, then zips it and sends as an array of bytes where it is unzipped and then deserialized. Similarly, native F# response, e.g. `Result<'A, 'B>` is sent the same way back. Projects `.\Samples\Wcf\WcfClient` and `.\Samples\Wcf\WcfService` contain examples of how it works and folders `.\Samples\Wcf\NetCoreClient` and `.\Samples\Wcf\NetCoreService` contain F# ports of .net Core examples from `CoreWcf`.


# Softellect.Messaging
`Softellect.Messaging` is a simple generic messaging client / server application, which allows multiple clients exchange strongly typed messages in a natural F# way. It is using `Softellect.Wcf` for communication. This library was created due to the need of exchanging huge F# structures (up to 150MB and more if serialized into human readable JSON or XML) among computers located in different places. Switching to zipped binary format as provided by `Softellect.Wcf` allowed approximately 100X size reduction. Projects `.\Samples\Msg\MsgService`, `.\Samples\Msg\MsgClientOne`, and `.\Samples\Msg\MsgClienttwo` contain examples of how it works.


# Softellect.Sys
`Softellect.Sys` is a collection of primitives used by `Softellect.Wcf`and `Softellect.Messaging`.
