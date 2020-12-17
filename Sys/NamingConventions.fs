namespace Softellect.Sys

/// Description of naming conventions used in the code.
/// Actual values are irrelevant but the comments describe the usage.
module NamingConventions =

    /// Suffixes:
    ///     Info: ...
    ///     Proxy: ...
    ///     Data: ...

    ///     Param: ...
    let general = 0


    /// Collection (record) of simple non-functional F# structures.
    /// It must be serializable / deserializable with standard means.
    let infoSuffix = 0


    /// Collection (record) of IO functions.
    let proxySuffix = 0


    /// Collection (record) of mixed info, proxy, and /or some interface data.
    let dataSuffix = 0
