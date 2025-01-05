namespace Softellect.Sys

module VersionInfo =

    /// !!! Do not forget to update versionNumber in VersionInfo.ps1 when this parameter is updated !!!
    ///
    /// This is an overall system version.
    [<Literal>]
    let VersionNumberValue = "9.0.1.04"


    /// !!! Update all non-empty appsettings.json files to match this value !!!
    /// The same as above but without the dots in order to use in database and folder names.
    [<Literal>]
    let VersionNumberNumericalValue = "901_04"
