namespace Softellect.Tests.SysTests

open Xunit
open System
open System.IO
open Softellect.Sys.Core
open Softellect.Sys.Primitives
open Softellect.Sys.Crypto
open FluentAssertions

module CoreTests =

    // Helper function to ensure a folder exists and is empty
    let private ensureFolderIsEmpty (FolderName folder) =
        try
            if Directory.Exists(folder) then
                // Recursively delete all files and subdirectories in the folder
                let rec deleteDirectoryContents (dir: string) =
                    // Delete all files in the directory
                    Directory.GetFiles(dir) |> Array.iter File.Delete

                    // Recursively delete subdirectories
                    Directory.GetDirectories(dir)
                    |> Array.iter (fun subDir ->
                        deleteDirectoryContents subDir
                        Directory.Delete(subDir))

                deleteDirectoryContents folder
            else
                // Create the directory if it doesn't exist
                Directory.CreateDirectory(folder) |> ignore
            true
        with
        | ex ->
            printfn $"Error while clearing folder: %s{ex.Message}"
            false


    // Helper function to create a test folder structure
    let private createTestFolderStructure (FolderName inputFolder) =
        let subfolder1 = Path.Combine(inputFolder, "Subfolder1")
        let subfolder2 = Path.Combine(inputFolder, "Subfolder2")

        Directory.CreateDirectory(subfolder1) |> ignore
        Directory.CreateDirectory(subfolder2) |> ignore

        File.WriteAllText(Path.Combine(subfolder1, "file1.txt"), "Content of file 1")
        File.WriteAllText(Path.Combine(subfolder2, "file2.txt"), "Content of file 2")


    // Helper function to compare directory structures
    let rec private compareDirectories (FolderName dir1) (FolderName dir2): bool =
        let files1 = Directory.GetFiles(dir1, "*", SearchOption.AllDirectories)
        let files2 = Directory.GetFiles(dir2, "*", SearchOption.AllDirectories)

        let normalizedFiles1 = files1 |> Array.map (fun f -> f.Replace(dir1, "").ToLowerInvariant())
        let normalizedFiles2 = files2 |> Array.map (fun f -> f.Replace(dir2, "").ToLowerInvariant())

        if normalizedFiles1.Length <> normalizedFiles2.Length then
            false
        else
            normalizedFiles1
            |> Array.forall (fun file ->
                let path1 = Path.Combine(dir1, file.TrimStart(Path.DirectorySeparatorChar))
                let path2 = Path.Combine(dir2, file.TrimStart(Path.DirectorySeparatorChar))
                File.ReadAllText(path1) = File.ReadAllText(path2))

    let private zippingAndUnzippingShouldPreserveContents zip compare : unit =
        let inputFolder = FolderName "C:\\Temp\\Input"
        let outputFolder = FolderName "C:\\Temp\\Output"

        // Ensure both folders exist and are empty
        Assert.True(ensureFolderIsEmpty inputFolder, $"Failed to clear/create input folder: {inputFolder}")
        Assert.True(ensureFolderIsEmpty outputFolder, $"Failed to clear/create output folder: {outputFolder}")

        // Create test folder structure in Input
        createTestFolderStructure inputFolder

        // Zip the Input folder
        let zipResult = zip inputFolder

        match zipResult with
        | Ok zipBytes ->
            // Unzip to the Output folder
            let unzipResult = unzipToFolder zipBytes outputFolder true

            match unzipResult with
            | Ok () ->
                // Verify that the unzipped contents match the original Input folder
                Assert.True(compare inputFolder outputFolder, "Unzipped folder content does not match original.")
            | Error err ->
                Assert.True(false, $"Unzipping failed: {err}")
        | Error err ->
            Assert.True(false, $"Zipping failed: {err}")


    // New helper function to create a test folder structure with specific content
    let private createTestFolderWithContent (FolderName folder) (files: (string * string) list) =
        // Create the main folder if it doesn't exist
        if not (Directory.Exists(folder)) then
            Directory.CreateDirectory(folder) |> ignore

        // Create files and subdirectories as needed
        for (path, content) in files do
            let fullPath = Path.Combine(folder, path)
            let directory = Path.GetDirectoryName(fullPath)

            // Create directory if it doesn't exist
            if not (Directory.Exists(directory)) then
                Directory.CreateDirectory(directory) |> ignore

            // Write content to file
            File.WriteAllText(fullPath, content)

    // Enhanced directory comparison function that checks expected file paths and contents
    let private compareDirectoryWithExpectedContent (FolderName folder) (expectedFiles: (string * string) list) =
        try
            let allChecks =
                expectedFiles
                |> List.map (fun (relativePath, expectedContent) ->
                    let fullPath = Path.Combine(folder, relativePath)

                    // Check if file exists
                    if not (File.Exists(fullPath)) then
                        printfn $"File not found: {fullPath}"
                        false
                    else
                        // Check file content
                        let actualContent = File.ReadAllText(fullPath)
                        if actualContent <> expectedContent then
                            printfn $"Content mismatch for {fullPath}"
                            printfn $"Expected: {expectedContent}"
                            printfn $"Actual: {actualContent}"
                            false
                        else
                            true
                )

            // Check if all files match expectations
            allChecks |> List.forall (fun result -> result)
        with
        | ex ->
            printfn $"Error during comparison: {ex.Message}"
            false


    // Test method to zip, unzip, and verify content
    [<Fact>]
    let zippingAndUnzippingFolderShouldPreserveContents () : unit =
        zippingAndUnzippingShouldPreserveContents zipFolder compareDirectories


    [<Fact>]
    let zipFolderWithAdditionalMappingsAndUnzippingFolderShouldPreserveContents () : unit =
        let zip f = zipFolderWithAdditionalMappings f []
        zippingAndUnzippingShouldPreserveContents zip compareDirectories


    [<Fact>]
    let ``zipFolderWithAdditionalMappings should include mappings in correct structure`` () =
        // Set up test folders
        let mainFolder = FolderName "C:\\Temp\\MainFolder"
        let additionalFolder1 = FolderName "C:\\Temp\\AdditionalFolder1"
        let additionalFolder2 = FolderName "C:\\Temp\\AdditionalFolder2"
        let outputFolder = FolderName "C:\\Temp\\MappingOutput"

        // Ensure all folders are empty
        [mainFolder; additionalFolder1; additionalFolder2; outputFolder]
        |> List.iter (fun folder ->
            Assert.True(ensureFolderIsEmpty folder, $"Failed to clear/create folder: {folder}"))

        // Create content in main folder
        createTestFolderWithContent mainFolder [
            "main.txt", "Main folder content"
            "subfolder\\main-sub.txt", "Main subfolder content"
        ]

        // Create content in additional folder 1
        createTestFolderWithContent additionalFolder1 [
            "add1.txt", "Additional folder 1 content"
            "nested\\add1-nested.txt", "Additional folder 1 nested content"
        ]

        // Create content in additional folder 2
        createTestFolderWithContent additionalFolder2 [
            "add2.txt", "Additional folder 2 content"
            "deep\\deeper\\add2-deep.txt", "Additional folder 2 deep content"
        ]

        let mappings = [
            { FolderPath = additionalFolder1; ArchiveSubfolder = "Extra1" }
            { FolderPath = additionalFolder2; ArchiveSubfolder = "Extra2\\Data" }
        ]

        // Zip everything
        let zipResult = zipFolderWithAdditionalMappings mainFolder mappings

        match zipResult with
        | Error err -> Assert.True(false, $"Zipping failed: {err}")
        | Ok zipBytes ->
            // Unzip to output folder
            let unzipResult = unzipToFolder zipBytes outputFolder true

            match unzipResult with
            | Error err -> Assert.True(false, $"Unzipping failed: {err}")
            | Ok () ->
                // Define expected file structure after unzipping
                let expectedFiles = [
                    // Main folder contents
                    "main.txt", "Main folder content"
                    "subfolder\\main-sub.txt", "Main subfolder content"

                    // Additional folder 1 contents (in Extra1)
                    "Extra1\\add1.txt", "Additional folder 1 content"
                    "Extra1\\nested\\add1-nested.txt", "Additional folder 1 nested content"

                    // Additional folder 2 contents (in Extra2\Data)
                    "Extra2\\Data\\add2.txt", "Additional folder 2 content"
                    "Extra2\\Data\\deep\\deeper\\add2-deep.txt", "Additional folder 2 deep content"
                ]

                // Verify output structure matches expectations
                let result = compareDirectoryWithExpectedContent outputFolder expectedFiles
                Assert.True(result, "Unzipped folder content does not match expected structure")


    [<Fact>]
    let encryptDecryptAesShouldWork () : unit =
        let rnd = Random(1)
        let len = 1_000_000
        let id = Guid.NewGuid() |> KeyId
        let senderPublicKey, senderPrivateKey = generateKey id
        let recipientPublicKey, recipientPrivateKey = generateKey id

        let isGuidEmbedded = checkKey id senderPublicKey
        isGuidEmbedded.Should().BeTrue() |> ignore
        let data = Array.zeroCreate<byte> len
        rnd.NextBytes(data)

        let encryptedData =
            match trySignAndEncrypt AES data senderPrivateKey recipientPublicKey with
            | Ok d -> d
            | Error e -> failwith $"Error: %A{e}"

        match tryDecryptAndVerify AES encryptedData recipientPrivateKey senderPublicKey with
        | Ok decryptedData -> decryptedData.Should().BeEquivalentTo(data) |> ignore
        | Error e -> failwith $"Error: %A{e}"


    [<Fact>]
    let encryptDecryptRsaShouldWork () : unit =
        let rnd = Random(1)
        let len = 1_000_000
        let id = Guid.NewGuid() |> KeyId
        let senderPublicKey, senderPrivateKey = generateKey id
        let recipientPublicKey, recipientPrivateKey = generateKey id

        let isGuidEmbedded = checkKey id senderPublicKey
        isGuidEmbedded.Should().BeTrue() |> ignore
        let data = Array.zeroCreate<byte> len
        rnd.NextBytes(data)

        let encryptedData =
            match trySignAndEncrypt RSA data senderPrivateKey recipientPublicKey with
            | Ok d -> d
            | Error e -> failwith $"Error: %A{e}"

        match tryDecryptAndVerify RSA encryptedData recipientPrivateKey senderPublicKey with
        | Ok decryptedData -> decryptedData.Should().BeEquivalentTo(data) |> ignore
        | Error e -> failwith $"Error: %A{e}"

    [<Fact>]
    let ``Array comparison should be by value not by reference`` () =
        // Arrange
        let a1 = [|1; 2; 3|]
        let a2 = [|1; 2; 3|]  // Different instance, same values

        // Act - the arrays should be different references but equal values
        let sameReference = Object.ReferenceEquals(a1, a2)
        let equalValues = (a1 = a2)

        // Assert
        sameReference.Should().BeFalse("arrays should be different instances") |> ignore
        equalValues.Should().BeTrue("arrays with same values should be equal") |> ignore
