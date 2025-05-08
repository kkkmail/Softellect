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



    // Test method to zip, unzip, and verify content
    [<Fact>]
    let zippingAndUnzippingFolderShouldPreserveContents () : unit =
        zippingAndUnzippingShouldPreserveContents zipFolder compareDirectories


    [<Fact>]
    let zipFolderWithAdditionalMappingsAndUnzippingFolderShouldPreserveContents () : unit =
        let zip f = zipFolderWithAdditionalMappings f []
        zippingAndUnzippingShouldPreserveContents zip compareDirectories


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
            match tryEncryptAndSign AES data senderPrivateKey recipientPublicKey with
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
            match tryEncryptAndSign RSA data senderPrivateKey recipientPublicKey with
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
