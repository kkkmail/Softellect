namespace Softellect.Vnc.Viewer

open System
open System.Drawing
open System.Drawing.Imaging
open System.Runtime.InteropServices
open Softellect.Vnc.Core.Primitives

module ScreenRenderer =

    /// Manages the bitmap representing the remote desktop.
    type ScreenBitmap(width: int, height: int) =
        let mutable bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb)
        let syncLock = obj()

        /// Resize the bitmap if the remote screen size changed.
        member _.Resize(newWidth: int, newHeight: int) =
            lock syncLock (fun () ->
                if bitmap.Width <> newWidth || bitmap.Height <> newHeight then
                    let old = bitmap
                    bitmap <- new Bitmap(newWidth, newHeight, PixelFormat.Format32bppArgb)
                    old.Dispose()
            )

        /// Apply a FrameUpdate to the bitmap.
        member _.ApplyFrame(frame: FrameUpdate) =
            lock syncLock (fun () ->
                if bitmap.Width <> frame.screenWidth || bitmap.Height <> frame.screenHeight then
                    let old = bitmap
                    bitmap <- new Bitmap(frame.screenWidth, frame.screenHeight, PixelFormat.Format32bppArgb)
                    old.Dispose()

                // Apply move regions (CopyRect) first
                for moveRegion in frame.moveRegions do
                    let srcRect = Rectangle(moveRegion.sourceX, moveRegion.sourceY, moveRegion.width, moveRegion.height)
                    let temp = bitmap.Clone(srcRect, bitmap.PixelFormat)
                    use g = Graphics.FromImage(bitmap)
                    g.DrawImage(temp, moveRegion.x, moveRegion.y)
                    temp.Dispose()

                // Apply dirty regions
                for region in frame.regions do
                    if region.width > 0 && region.height > 0 && region.data.Length > 0 then
                        let rect = Rectangle(region.x, region.y, region.width, region.height)

                        // Clamp to bitmap bounds
                        let clampedRect =
                            Rectangle.Intersect(rect, Rectangle(0, 0, bitmap.Width, bitmap.Height))

                        if clampedRect.Width > 0 && clampedRect.Height > 0 then
                            let bmpData = bitmap.LockBits(clampedRect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
                            try
                                let bytesPerPixel = 4
                                let srcStride = region.width * bytesPerPixel
                                let dstStride = bmpData.Stride

                                // Copy row by row (source data is BGRA, same as bitmap)
                                let startRow = clampedRect.Y - region.y
                                let startCol = clampedRect.X - region.x
                                for row in 0..clampedRect.Height-1 do
                                    let srcOffset = (startRow + row) * srcStride + startCol * bytesPerPixel
                                    let dstOffset = row * dstStride
                                    let copyLen = min (clampedRect.Width * bytesPerPixel) (region.data.Length - srcOffset)
                                    if copyLen > 0 && srcOffset >= 0 && srcOffset + copyLen <= region.data.Length then
                                        Marshal.Copy(region.data, srcOffset, bmpData.Scan0 + nativeint dstOffset, copyLen)
                            finally
                                bitmap.UnlockBits(bmpData)
            )

        /// Get the current bitmap for rendering.
        member _.GetBitmap() =
            lock syncLock (fun () -> bitmap.Clone() :?> Bitmap)

        /// Draw the bitmap directly to a Graphics context.
        member _.DrawTo(g: Graphics, destRect: Rectangle) =
            lock syncLock (fun () ->
                g.InterpolationMode <- Drawing2D.InterpolationMode.NearestNeighbor
                g.DrawImage(bitmap, destRect, 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel)
            )

        member _.Width = lock syncLock (fun () -> bitmap.Width)
        member _.Height = lock syncLock (fun () -> bitmap.Height)

        interface IDisposable with
            member _.Dispose() =
                lock syncLock (fun () -> bitmap.Dispose())
