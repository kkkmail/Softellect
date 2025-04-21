// IncrementBuildNumber.fsx
// This script increments the build number in BuildInfo.fs

open System
open System.IO
open System.Text.RegularExpressions

// Get the directory of the script
let scriptDir = __SOURCE_DIRECTORY__

// Path to the BuildInfo.fs file (relative to script location)
let buildInfoPath = Path.Combine(scriptDir, "BuildInfo.fs")

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
        let newContent = regex.Replace(content, sprintf "let BuildNumber = %d" newBuildNumber)
        
        // Write the updated content back to the file
        File.WriteAllText(buildInfoPath, newContent)
        
        printfn "Build number incremented from %d to %d" currentBuildNumber newBuildNumber
    else
        printfn "Error: Could not find BuildNumber pattern in file"
        exit 1
with
| ex ->
    printfn "Error: %s" ex.Message
    exit 1
