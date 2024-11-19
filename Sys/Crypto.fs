namespace Softellect.Sys

open System
open System.IO
open System.Security.Cryptography
open System.Xml.Linq
open Softellect.Sys.Primitives
open Softellect.Sys.Errors
open Softellect.Sys.Core

module Crypto =

    let rsaKeyLength = 4096
    let private publicKeyExtension = FileExtension ".pkx"
    let private toError e = e |> CryptoErr |> Error


    /// Signs the data using the sender's private key.
    let signData (data: byte[]) (PrivateKey privateKey) =
        try
            use rsa = RSA.Create()
            rsa.FromXmlString(privateKey)

            let signature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            Ok signature
        with
        | ex -> ex |> SignDataExn |> CryptoErr |> Error


    /// Verifies the signature using the sender's public key.
    let verifySignature (data: byte[]) (signature: byte[]) (PublicKey publicKey) =
        try
            use rsa = RSA.Create()
            rsa.FromXmlString(publicKey)

            match rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1) with
            | true -> Ok ()
            | false -> VerifySignatureFailedError |> CryptoErr |> Error
        with
        | ex -> ex |> VerifySignatureExn |> CryptoErr |> Error


    /// Encrypts and signs the data.
    let tryEncryptAndSign tryEncrypt data senderPrivateKey recipientPublicKey =
        match signData data senderPrivateKey with
        | Ok signature -> tryEncrypt (Array.concat [signature; data]) recipientPublicKey
        | Error e -> Error e


    /// Decrypts and verifies the signed data.
    let tryDecryptAndVerify tryDecrypt encryptedData recipientPrivateKey (senderPublicKey : PublicKey) =
        match tryDecrypt encryptedData recipientPrivateKey with
        | Ok (combinedData : byte[]) ->
            use rsa = RSA.Create()
            rsa.FromXmlString(senderPublicKey.value)
            let signatureLength = rsa.KeySize / 8
            let signature = combinedData[..signatureLength - 1]
            let originalData = combinedData[signatureLength..]

            match verifySignature originalData signature senderPublicKey with
            | Ok () -> Ok originalData
            | Error e -> Error e
        | Error e -> Error e


    /// Encrypts large data by using RSA for the symmetric key and AES for the data.
    let tryEncryptAes (data: byte[]) (PublicKey publicKey) =
        try
            use rsa = RSA.Create()
            rsa.FromXmlString(publicKey)

            // Generate a symmetric key (AES)
            use aes = Aes.Create()
            aes.GenerateKey()
            aes.GenerateIV()

            // Encrypt the symmetric key using RSA
            let encryptedSymmetricKey = rsa.Encrypt(aes.Key, RSAEncryptionPadding.OaepSHA256)
            let encryptedIV = rsa.Encrypt(aes.IV, RSAEncryptionPadding.OaepSHA256)

            // Encrypt the data using AES
            use encryptor = aes.CreateEncryptor()
            let encryptedData =
                use ms = new IO.MemoryStream()
                use cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write)
                cs.Write(data, 0, data.Length)
                cs.FlushFinalBlock()
                ms.ToArray()

            // Combine encrypted parts: [RSA(SymmetricKey) | RSA(IV) | AES(Data)]
            let result = Array.concat [ encryptedSymmetricKey; encryptedIV; encryptedData ]
            Ok result
        with
        | ex -> ex |> TryEncryptAesExn |> CryptoErr |> Error


    /// Decrypts data by first decrypting the symmetric key and IV with RSA, then decrypting the data with AES.
    let tryDecryptAes (encryptedData: byte[]) (PrivateKey privateKey) =
        try
            use rsa = RSA.Create()
            rsa.FromXmlString(privateKey)

            // Extract RSA encrypted parts (key, IV, and the rest is AES encrypted data)
            let keySize = rsa.KeySize / 8
            let encryptedSymmetricKey = encryptedData[..keySize-1]
            let encryptedIV = encryptedData[keySize..(2*keySize-1)]
            let aesData = encryptedData[(2*keySize)..]

            // Decrypt symmetric key and IV
            let symmetricKey = rsa.Decrypt(encryptedSymmetricKey, RSAEncryptionPadding.OaepSHA256)
            let iv = rsa.Decrypt(encryptedIV, RSAEncryptionPadding.OaepSHA256)

            // Decrypt the data using AES
            use aes = Aes.Create()
            aes.Key <- symmetricKey
            aes.IV <- iv

            use decryptor = aes.CreateDecryptor()
            let decryptedData =
                use ms = new IO.MemoryStream()
                use cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Write)
                cs.Write(aesData, 0, aesData.Length)
                cs.FlushFinalBlock()
                ms.ToArray()

            Ok decryptedData
        with
        | ex -> ex |> TryDecryptAesExn |> CryptoErr |> Error


    /// Helper function to split data into chunks
    let private chunkArray (data: byte[]) (chunkSize: int) : byte[][] =
        [| for i in 0 .. chunkSize .. data.Length - 1 ->
            let size = Math.Min(chunkSize, data.Length - i)
            Array.sub data i size |]


    /// Encrypts data using RSA by splitting it into manageable chunks.
    let tryEncryptRsa (data: byte[]) (PublicKey publicKey) =
        try
            use rsa = RSA.Create()
            rsa.FromXmlString(publicKey)

            // RSA maximum encryptable size
            let maxChunkSize = (rsa.KeySize / 8) - 66  // 66 bytes overhead for OAEP SHA256 padding
            let chunks = chunkArray data maxChunkSize

            // Encrypt each chunk
            let encryptedChunks =
                chunks |> Array.map (fun chunk -> rsa.Encrypt(chunk, RSAEncryptionPadding.OaepSHA256))

            // Combine all encrypted chunks into one byte array
            let result = encryptedChunks |> Array.concat
            Ok result
        with
        | ex -> ex |> TryEncryptRsaExn |> CryptoErr |> Error


    /// Decrypts RSA-encrypted data, assuming it was encrypted in chunks.
    let tryDecryptRsa (encryptedData: byte[]) (PrivateKey privateKey) =
        try
            use rsa = RSA.Create()
            rsa.FromXmlString(privateKey)

            let chunkSize = rsa.KeySize / 8  // Encrypted chunk size equals RSA key size
            let chunks = chunkArray encryptedData chunkSize

            // Decrypt each chunk
            let decryptedChunks =
                chunks |> Array.map (fun chunk -> rsa.Decrypt(chunk, RSAEncryptionPadding.OaepSHA256))

            // Combine decrypted chunks
            let result = decryptedChunks |> Array.concat
            Ok result
        with
        | ex -> ex |> TryDecryptRsaExn |> CryptoErr |> Error


    /// Embeds the Guid in the key as metadata.
    let private embedGuidInKey (rsa: RSA) (KeyId id) =
        let publicKeyXml = rsa.ToXmlString(false)
        let privateKeyXml = rsa.ToXmlString(true)

        let addGuidToXml (xml: string) =
            let doc = XDocument.Parse(xml)
            doc.Root.Add(XElement("Guid", id.ToString()))
            doc.ToString()

        (addGuidToXml publicKeyXml |> PublicKey, privateKeyXml |> PrivateKey)


    /// Extracts the Guid from an XML key.
    let private extractKeyIdFromKey (keyXml: string) =
        try
            let doc = XDocument.Parse(keyXml)
            let guidElement = doc.Root.Element("Guid")

            if guidElement <> null then Guid.Parse(guidElement.Value) |> KeyId |> Some
            else None
        with
        | _ -> None


    /// Generates a public/private key pair with an embedded Guid.
    let generateKey id =
        use rsa = RSA.Create(rsaKeyLength)
        embedGuidInKey rsa id


    /// Verifies if a public key is bound to the given Guid.
    let checkKey id (PublicKey publicKey) : bool =
        match extractKeyIdFromKey publicKey with
        | Some embeddedId -> embeddedId = id
        | None -> false


    let tryExportPublicKey (folderName : FolderName) (PublicKey publicKey) (overwrite : bool) =
        try
            match extractKeyIdFromKey publicKey with
            | Some i ->
                let fileName = (FileName $"{i.value}{publicKeyExtension.value}").combine(folderName)
                let keyValue = publicKey |> zip

                if overwrite || (File.Exists fileName.value |> not)
                then
                    File.WriteAllBytes(fileName.value, keyValue)
                    Ok ()
                else fileName |> KeyFileExistErr |> toError
            | None -> MissingKeyId |> toError
        with
        | e -> e |> KeyExportExn |> toError


    let tryImportPublicKey (fileName : FileName) (ko : KeyId option)=
        try
            match fileName.tryGetFullFileName() with
            | Ok fn ->
                let key = File.ReadAllBytes fn.value |> unZip |> PublicKey

                match ko with
                | Some k ->
                    match checkKey k key with
                    | true -> Ok (k, key)
                    | false -> (k, fileName) |> KeyMismatchErr |> toError
                | None ->
                    match extractKeyIdFromKey key.value with
                    | Some k -> Ok (k, key)
                    | None -> KeyImportMissingIdErr |> toError
            | Error e -> e |> KeyImportFileErr |> toError
        with
        | e -> e |> KeyImportExn |> toError
