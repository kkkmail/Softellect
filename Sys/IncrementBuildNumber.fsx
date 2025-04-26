// This script increments the build number in BuildInfo.fs
// and updates Version/PackageVersion in the project file
 
open System
open System.IO
open System.Text.RegularExpressions
open System.Xml.Linq
 
// Get the directory of the script
let scriptDir = __SOURCE_DIRECTORY__
 
// Path to the BuildInfo.fs file (relative to script location)
let buildInfoPath = Path.Combine(scriptDir, "BuildInfo.fs")
// Path to the project file
let projectFilePath = Path.Combine(scriptDir, "Sys.fsproj")
// Path to a lock file to prevent infinite loops
let lockFilePath = Path.Combine(scriptDir, ".version-update-lock")
 
try
    // Check if we're in a loop by reading lock file content
    let mutable isInLoop = false
    if File.Exists(lockFilePath) then
        try
            let lockContent = File.ReadAllText(lockFilePath)
            let timestamp = DateTime.Parse(lockContent)
            
            // If lock is less than 60 seconds old, we're in a loop
            if (DateTime.Now - timestamp).TotalSeconds < 60 then
                isInLoop <- true
                printfn "Detected potential loop. Skipping version update."
        with
        | _ -> 
            // If we can't parse the timestamp, consider it invalid
            isInLoop <- false
    
    // If we're in a loop, exit early
    if isInLoop then
        exit 0
        
    // Write current timestamp to lock file
    File.WriteAllText(lockFilePath, DateTime.Now.ToString("o"))
    
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
 
        // Now update the project file
        if File.Exists(projectFilePath) then
            // Load the project file as XML
            let projectXml = XDocument.Load(projectFilePath)
 
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
                // Extract version pattern (assuming format like 9.0.100.5)
                let firstVersionValue = versionElements.[0].Value
                let versionParts = firstVersionValue.Split('.')
 
                if versionParts.Length >= 4 then
                    // Check if the last part of version already matches the new build number
                    if versionParts.[3] = newBuildNumber.ToString() then
                        printfn "Version already up to date, no changes needed"
                    else
                        // Keep major.minor.patch but update build number
                        let newVersion = String.Join(".", [|
                            versionParts.[0]  // major
                            versionParts.[1]  // minor
                            versionParts.[2]  // patch
                            newBuildNumber.ToString() // new build number
                        |])
 
                        // Update all version elements
                        for element in versionElements do
                            element.Value <- newVersion
 
                        // Save changes
                        projectXml.Save(projectFilePath)
                        printfn $"Updated Version/PackageVersion to {newVersion}"
                else
                    printfn "Warning: Version format not as expected, no update made to project file"
            else
                printfn "Warning: Could not find Version or PackageVersion elements in project file"
        else
            printfn $"Warning: Project file not found at %s{projectFilePath}"
    else
        printfn "Error: Could not find BuildNumber pattern in file"
        exit 1
        
    // Update the lock file with a "completed" timestamp instead of deleting it
    File.WriteAllText(lockFilePath, DateTime.Now.AddSeconds(10.0).ToString("o"))
with
| ex ->
    // Update the lock file with an "error" timestamp
    try
        File.WriteAllText(lockFilePath, DateTime.Now.AddSeconds(10.0).ToString("o"))
    with
    | _ -> ()
    
    printfn $"Error: %s{ex.Message}"
    exit 1
