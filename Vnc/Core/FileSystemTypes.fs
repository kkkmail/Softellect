namespace Softellect.Vnc.Core

open System

module FileSystemTypes =
    type FileEntryKind =
        | FileEntry
        | DirectoryEntry
        | ParentDirectory

    type FileEntry =
        {
            name : string
            kind : FileEntryKind
            size : int64
            lastModified : DateTime
            isSelected : bool
        }

    type DirectoryListing =
        {
            path : string
            entries : FileEntry[]
            error : string option
        }

    type FileTransferId =
        | FileTransferId of Guid
        member this.value = let (FileTransferId v) = this in v
        static member create() = Guid.NewGuid() |> FileTransferId

    type FileTransferDirection =
        | LocalToRemote
        | RemoteToLocal

    type FileChunk =
        {
            transferId : FileTransferId
            filePath : string
            chunkIndex : int
            totalChunks : int
            data : byte[]
        }

    type FileTransferRequest =
        {
            transferId : FileTransferId
            direction : FileTransferDirection
            files : string list
            destinationPath : string
        }
