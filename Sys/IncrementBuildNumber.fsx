// This script increments the build number in BuildInfo.fs
// and updates Version/PackageVersion in multiple project files

open System
open System.IO
open System.Text.RegularExpressions
open System.Xml.Linq

// If true, check for a loop using a lock file.
// Otherwise, check only for the presence of the lock file.
let checkTimeStamp = false

// Configure projects to update (relative paths from script location)
let projectsToUpdate = [
    // Project name, relative path to project file
    ("Sys", "Sys.fsproj")
    ("Wcf", @"..\Wcf\Wcf.fsproj")
    ("Analytics", @"..\Analytics\Analytics.fsproj")
    ("Math", @"..\Math\Math.fsproj")
    ("Messaging", @"..\Messaging\Messaging.fsproj")
    ("MessagingService", @"..\MessagingService\MessagingService.fsproj")
    ("DistributedProcessing\Core", @"..\DistributedProcessing\Core\Core.fsproj")
    ("DistributedProcessing\MessagingService", @"..\DistributedProcessing\MessagingService\MessagingService.fsproj")
    ("DistributedProcessing\ModelGenerator", @"..\DistributedProcessing\ModelGenerator\ModelGenerator.fsproj")
    ("DistributedProcessing\PartitionerAdm", @"..\DistributedProcessing\PartitionerAdm\PartitionerAdm.fsproj")
    ("DistributedProcessing\PartitionerService", @"..\DistributedProcessing\PartitionerService\PartitionerService.fsproj")
    ("DistributedProcessing\SolverRunner", @"..\DistributedProcessing\SolverRunner\SolverRunner.fsproj")
    ("DistributedProcessing\WorkerNodeAdm", @"..\DistributedProcessing\WorkerNodeAdm\WorkerNodeAdm.fsproj")
    ("DistributedProcessing\WorkerNodeService", @"..\DistributedProcessing\WorkerNodeService\WorkerNodeService.fsproj")
]

// Get the directory of the script
let scriptDir = __SOURCE_DIRECTORY__

// Path to the BuildInfo.fs file (relative to script location)
let buildInfoPath = Path.Combine(scriptDir, "BuildInfo.fs")

// Path to the BuildInfo.ps1 file (relative to script location)
let buildInfoPsPath = Path.Combine(scriptDir, "BuildInfo.ps1")

// Path to a lock file to prevent infinite loops
let lockFilePath = Path.Combine(scriptDir, ".version-update-lock")

/// Checks if a lock file exists and if we're in a loop
let checkForLoop() =
    if File.Exists(lockFilePath) then
        try
            if checkTimeStamp then
                // Read the lock file content and parse the timestamp
                let lockContent = File.ReadAllText(lockFilePath)
                let timestamp = DateTime.Parse(lockContent)

                // If lock is less than 3600 seconds old, we're in a loop
                if (DateTime.Now - timestamp).TotalSeconds < 3_600 then
                    printfn "Detected potential loop. Skipping version update."
                    true
                else
                    false
            else
                // If checkTimeStamp is false, just check for existence of the lock file
                printfn "Lock file exists but checkTimeStamp is false. Skipping version update."
                true
        with
        | _ ->
            // If we can't parse the timestamp, consider it invalid
            false
    else
        false

/// Creates or updates the lock file with current timestamp
let updateLockFile (addSeconds: float) =
    let timestamp =
        if addSeconds = 0.0 then
            DateTime.Now
        else
            DateTime.Now.AddSeconds(addSeconds)

    try
        File.WriteAllText(lockFilePath, timestamp.ToString("o"))
    with
    | ex -> printfn $"Warning: Could not update lock file: %s{ex.Message}"

/// Increments the build number in BuildInfo.fs file
let incrementBuildNumber () =
    try
        // Read the current content
        let content = File.ReadAllText(buildInfoPath)

        // Define regex pattern to find the build number
        let pattern = @"let\s+BuildNumber\s+=\s+(\d+)"
        let regex = Regex(pattern)

        // Find and increment the build number
        let match' = regex.Match(content)
        if match'.Success then
            let currentBuildNumber = Int32.Parse(match'.Groups.[1].Value)
            let newBuildNumber = currentBuildNumber + 1

            // Replace with the new build number
            let newContent = regex.Replace(content, $"let BuildNumber = %d{newBuildNumber}")

            // Write the updated content back to the file
            File.WriteAllText(buildInfoPath, newContent)

            printfn $"Build number incremented from %d{currentBuildNumber} to %d{newBuildNumber}"
            Some newBuildNumber
        else
            printfn $"Error: Could not find BuildNumber pattern in file %s{buildInfoPath}"
            None
    with
    | ex ->
        printfn $"Error incrementing build number in %s{buildInfoPath}: %s{ex.Message}"
        None

/// Extracts FSharp.Core version from project file
let extractFSharpCoreVersion (projectXml: XDocument) =
    try
        let ns = XNamespace.None
        // Look for PackageReference for FSharp.Core
        let fsharpCoreElement =
            projectXml.Descendants(ns + "PackageReference")
            |> Seq.tryFind (fun e ->
                let includeAttr = e.Attribute(XName.Get("Include"))
                let updateAttr = e.Attribute(XName.Get("Update"))
                (includeAttr <> null && includeAttr.Value = "FSharp.Core") ||
                (updateAttr <> null && updateAttr.Value = "FSharp.Core"))

        match fsharpCoreElement with
        | Some element ->
            let versionAttr = element.Attribute(XName.Get("Version"))
            if versionAttr <> null then
                let versionValue = versionAttr.Value
                let versionParts = versionValue.Split('.')
                if versionParts.Length >= 3 then
                    // Extract first 3 parts of version
                    Some (versionParts.[0], versionParts.[1], versionParts.[2])
                else
                    None
            else
                None
        | None -> None
    with
    | _ -> None

/// Updates version in a project file based on new build number
let updateProjectVersion (projectFilePath: string) (newBuildNumber: int) =
    try
        if File.Exists(projectFilePath) then
            // Load the project file as XML
            let projectXml = XDocument.Load(projectFilePath)

            // Try to extract FSharp.Core version first
            let fsharpCoreVersion = extractFSharpCoreVersion projectXml

            // Find Version and PackageVersion elements
            let ns = XNamespace.None
            let versionElements =
                projectXml.Descendants(ns + "PropertyGroup")
                    .Descendants()
                    |> Seq.filter (fun e ->
                        e.Name.LocalName = "Version" ||
                        e.Name.LocalName = "PackageVersion")
                    |> Seq.toArray

            if versionElements.Length > 0 then
                // Determine the new version
                let newVersion =
                    match fsharpCoreVersion with
                    | Some (major, minor, patch) ->
                        // Use FSharp.Core version with our build number
                        String.Join(".", [| major; minor; patch; newBuildNumber.ToString() |])
                    | None ->
                        // Fall back to original behavior
                        printfn $"Warning: Could not find FSharp.Core version in %s{projectFilePath}, using current version format"
                        // Extract version pattern (assuming format like 9.0.100.5)
                        let firstVersionValue = versionElements.[0].Value
                        let versionParts = firstVersionValue.Split('.')

                        if versionParts.Length >= 4 then
                            // Keep major.minor.patch but update build number
                            String.Join(".", [|
                                versionParts.[0]  // major
                                versionParts.[1]  // minor
                                versionParts.[2]  // patch
                                newBuildNumber.ToString() // new build number
                            |])
                        else
                            // Unexpected format, append build number
                            $"%s{firstVersionValue}.%d{newBuildNumber}"

                // Check if the version already matches (only needed for fallback case)
                // Check if we need to update when using current implementation
                let skipUpdate =
                    match fsharpCoreVersion with
                    | None ->
                        let firstVersionValue = versionElements.[0].Value
                        let versionParts = firstVersionValue.Split('.')
                        versionParts.Length >= 4 && versionParts.[3] = newBuildNumber.ToString()
                    | Some _ ->
                        false

                if skipUpdate then
                    printfn $"Version in %s{projectFilePath} already up to date, no changes needed"
                    true, None
                else
                    // Update all version elements
                    for element in versionElements do
                        element.Value <- newVersion

                    // Save changes
                    projectXml.Save(projectFilePath)
                    printfn $"Updated Version/PackageVersion to %s{newVersion} in %s{projectFilePath}"
                    true, Some newVersion
            else
                printfn $"Warning: Could not find Version or PackageVersion elements in %s{projectFilePath}"
                false, None
        else
            printfn $"Warning: Project file %s{projectFilePath} not found"
            false, None
    with
    | ex ->
        printfn $"Error updating project version in %s{projectFilePath}: %s{ex.Message}"
        false, None

/// Updates the BuildInfo.ps1 file with the version from Sys.fsproj
let updatePowerShellBuildInfo (version: string) =
    try
        if File.Exists(buildInfoPsPath) then
            // Read current content
            let content = File.ReadAllText(buildInfoPsPath)

            // Define regex pattern for the version string
            let pattern = @"\[string\]\s+\$global:buildNumber\s+=\s+""([^""]+)"""
            let regex = Regex(pattern)

            // Find and update the version
            let match' = regex.Match(content)
            if match'.Success then
                // Replace with the new version
                let newContent = regex.Replace(content, $"[string] $$global:buildNumber = \"{version}\"")

                // Write the updated content
                File.WriteAllText(buildInfoPsPath, newContent)

                printfn $"Updated BuildInfo.ps1 with version {version}"
                true
            else
                printfn $"Error: Could not find version pattern in file {buildInfoPsPath}"
                false
        else
            printfn $"Warning: BuildInfo.ps1 file not found at {buildInfoPsPath}"
            false
    with
    | ex ->
        printfn $"Error updating BuildInfo.ps1: {ex.Message}"
        false

// Main execution
try
    // Check if we're in a loop
    if checkForLoop() then
        exit 0

    // Update lock file
    updateLockFile 0.0

    // Increment build number once
    match incrementBuildNumber() with
    | Some newBuildNumber ->
        // Keep track of Sys project version for PS1 update
        let mutable sysProjectVersion = None

        // Update all project versions
        for (projectName, projectFileRelPath) in projectsToUpdate do
            printfn $"Processing project: %s{projectName}"
            let projectFilePath = Path.Combine(scriptDir, projectFileRelPath)
            let success, version = updateProjectVersion projectFilePath newBuildNumber

            // Save Sys project version for PS1 update
            if success && projectName = "Sys" && version.IsSome then
                sysProjectVersion <- version

            if success then
                printfn $"Successfully updated %s{projectName}"
            else
                printfn $"Failed to update project version for %s{projectName}"

        // Update the PowerShell BuildInfo file if we have a Sys version
        match sysProjectVersion with
        | Some version ->
            let psUpdateSuccess = updatePowerShellBuildInfo version
            if psUpdateSuccess then
                printfn "Successfully updated BuildInfo.ps1"
            else
                printfn "Failed to update BuildInfo.ps1"
        | None ->
            printfn "Warning: Could not determine Sys project version, skipping BuildInfo.ps1 update"

    | None ->
        printfn "Failed to increment build number, skipping project updates"

    // Update the lock file with a "completed" timestamp
    updateLockFile 10.0

    printfn "All projects processed successfully"
with
| ex ->
    // Update the lock file with an "error" timestamp
    updateLockFile 3_600
    printfn $"Error in main execution: %s{ex.Message}"
    exit 1
