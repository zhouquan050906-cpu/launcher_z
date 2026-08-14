/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Launcher.App.Controls.Account;

internal sealed record PreviewTextureAtlas(
    BitmapSource Bitmap,
    IReadOnlyDictionary<Int32Rect, Rect> TextureCoordinates,
    int SourcePixelWidth,
    int SourcePixelHeight);

internal static class PreviewTextureAtlasBuilder
{
    private const int GutterPixels = 1;

    internal static PreviewTextureAtlas Build(
        BitmapSource source,
        IEnumerable<Int32Rect> requestedRegions,
        int pixelScale,
        double brightness,
        int maximumSourceRowWidth,
        bool opaqueUnusedPixels = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (pixelScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelScale));

        brightness = Math.Clamp(brightness, 0d, 1d);
        var converted = EnsureBgra32(source);
        var regions = requestedRegions.Distinct().ToArray();
        if (regions.Length == 0)
            throw new ArgumentException("At least one texture region is required.", nameof(requestedRegions));

        var placements = Pack(regions, converted.PixelWidth, converted.PixelHeight, maximumSourceRowWidth);
        var sourceWidth = Math.Max(1, placements.Max(item => item.OuterRect.X + item.OuterRect.Width));
        var sourceHeight = Math.Max(1, placements.Max(item => item.OuterRect.Y + item.OuterRect.Height));
        var sourceStride = converted.PixelWidth * 4;
        var sourcePixels = new byte[sourceStride * converted.PixelHeight];
        converted.CopyPixels(sourcePixels, sourceStride, 0);

        var atlasStride = sourceWidth * 4;
        var atlasPixels = new byte[atlasStride * sourceHeight];
        if (opaqueUnusedPixels)
        {
            for (var index = 3; index < atlasPixels.Length; index += 4)
                atlasPixels[index] = byte.MaxValue;
        }
        foreach (var placement in placements)
        {
            for (var y = 0; y < placement.OuterRect.Height; y++)
            {
                var sourceY = placement.ClampedRegion.Y
                    + Math.Clamp(y - GutterPixels, 0, placement.ClampedRegion.Height - 1);
                for (var x = 0; x < placement.OuterRect.Width; x++)
                {
                    var sourceX = placement.ClampedRegion.X
                        + Math.Clamp(x - GutterPixels, 0, placement.ClampedRegion.Width - 1);
                    var sourceIndex = (sourceY * sourceStride) + (sourceX * 4);
                    var atlasIndex = ((placement.OuterRect.Y + y) * atlasStride)
                        + ((placement.OuterRect.X + x) * 4);
                    atlasPixels[atlasIndex] = ApplyBrightness(sourcePixels[sourceIndex], brightness);
                    atlasPixels[atlasIndex + 1] = ApplyBrightness(sourcePixels[sourceIndex + 1], brightness);
                    atlasPixels[atlasIndex + 2] = ApplyBrightness(sourcePixels[sourceIndex + 2], brightness);
                    atlasPixels[atlasIndex + 3] = sourcePixels[sourceIndex + 3];
                }
            }
        }

        var outputWidth = sourceWidth * pixelScale;
        var outputHeight = sourceHeight * pixelScale;
        var outputStride = outputWidth * 4;
        var outputPixels = new byte[outputStride * outputHeight];
        for (var y = 0; y < outputHeight; y++)
        {
            var sourceY = y / pixelScale;
            for (var x = 0; x < outputWidth; x++)
            {
                var sourceIndex = (sourceY * atlasStride) + ((x / pixelScale) * 4);
                var outputIndex = (y * outputStride) + (x * 4);
                outputPixels[outputIndex] = atlasPixels[sourceIndex];
                outputPixels[outputIndex + 1] = atlasPixels[sourceIndex + 1];
                outputPixels[outputIndex + 2] = atlasPixels[sourceIndex + 2];
                outputPixels[outputIndex + 3] = atlasPixels[sourceIndex + 3];
            }
        }

        var bitmap = BitmapSource.Create(
            outputWidth,
            outputHeight,
            96d,
            96d,
            PixelFormats.Bgra32,
            null,
            outputPixels,
            outputStride);
        bitmap.Freeze();

        var coordinates = placements.ToDictionary(
            item => item.RequestedRegion,
            item => new Rect(
                (item.OuterRect.X + GutterPixels) / (double)sourceWidth,
                (item.OuterRect.Y + GutterPixels) / (double)sourceHeight,
                item.ClampedRegion.Width / (double)sourceWidth,
                item.ClampedRegion.Height / (double)sourceHeight));
        return new PreviewTextureAtlas(bitmap, coordinates, sourceWidth, sourceHeight);
    }

    private static IReadOnlyList<Placement> Pack(
        IReadOnlyList<Int32Rect> requestedRegions,
        int sourceWidth,
        int sourceHeight,
        int maximumSourceRowWidth)
    {
        var maximumWidth = Math.Max(8, maximumSourceRowWidth);
        var result = new List<Placement>(requestedRegions.Count);
        var x = 0;
        var y = 0;
        var rowHeight = 0;
        foreach (var requested in requestedRegions)
        {
            var clamped = ClampRegion(requested, sourceWidth, sourceHeight);
            var outerWidth = clamped.Width + (GutterPixels * 2);
            var outerHeight = clamped.Height + (GutterPixels * 2);
            if (x > 0 && x + outerWidth > maximumWidth)
            {
                x = 0;
                y += rowHeight;
                rowHeight = 0;
            }

            var outer = new Int32Rect(x, y, outerWidth, outerHeight);
            result.Add(new Placement(requested, clamped, outer));
            x += outerWidth;
            rowHeight = Math.Max(rowHeight, outerHeight);
        }

        return result;
    }

    private static Int32Rect ClampRegion(Int32Rect region, int width, int height)
    {
        var x = Math.Clamp(region.X, 0, width - 1);
        var y = Math.Clamp(region.Y, 0, height - 1);
        return new Int32Rect(
            x,
            y,
            Math.Max(1, Math.Min(region.Width, width - x)),
            Math.Max(1, Math.Min(region.Height, height - y)));
    }

    private static BitmapSource EnsureBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32)
            return source;

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0d);
        converted.Freeze();
        return converted;
    }

    private static byte ApplyBrightness(byte value, double brightness) =>
        (byte)Math.Round(value * brightness);

    private sealed record Placement(
        Int32Rect RequestedRegion,
        Int32Rect ClampedRegion,
        Int32Rect OuterRect);
}

internal sealed class PreviewMeshBuilder
{
    private readonly Point3DCollection positions = [];
    private readonly PointCollection textureCoordinates = [];
    private readonly Int32Collection triangleIndices = [];

    internal int QuadCount => positions.Count / 4;

    internal void AddQuad(
        Point3D p0,
        Point3D p1,
        Point3D p2,
        Point3D p3,
        Rect textureRect,
        bool reverseWinding = false)
    {
        var start = positions.Count;
        positions.Add(p0);
        positions.Add(p1);
        positions.Add(p2);
        positions.Add(p3);
        textureCoordinates.Add(new Point(textureRect.Left, textureRect.Top));
        textureCoordinates.Add(new Point(textureRect.Right, textureRect.Top));
        textureCoordinates.Add(new Point(textureRect.Right, textureRect.Bottom));
        textureCoordinates.Add(new Point(textureRect.Left, textureRect.Bottom));
        if (reverseWinding)
        {
            triangleIndices.Add(start);
            triangleIndices.Add(start + 2);
            triangleIndices.Add(start + 1);
            triangleIndices.Add(start);
            triangleIndices.Add(start + 3);
            triangleIndices.Add(start + 2);
        }
        else
        {
            triangleIndices.Add(start);
            triangleIndices.Add(start + 1);
            triangleIndices.Add(start + 2);
            triangleIndices.Add(start);
            triangleIndices.Add(start + 2);
            triangleIndices.Add(start + 3);
        }
    }

    internal MeshGeometry3D Build(bool anchorUnitTextureCoordinates = false)
    {
        if (anchorUnitTextureCoordinates && positions.Count > 0)
            AddTextureCoordinateAnchor();
        positions.Freeze();
        textureCoordinates.Freeze();
        triangleIndices.Freeze();
        var mesh = new MeshGeometry3D
        {
            Positions = positions,
            TextureCoordinates = textureCoordinates,
            TriangleIndices = triangleIndices
        };
        mesh.Freeze();
        return mesh;
    }

    private void AddTextureCoordinateAnchor()
    {
        // WPF normalizes an ImageBrush against the texture-coordinate bounds used by a
        // mesh. A zero-area triangle spanning 0..1 keeps atlas coordinates absolute
        // without producing pixels or changing the visible model geometry.
        var start = positions.Count;
        var anchor = positions[0];
        positions.Add(anchor);
        positions.Add(anchor);
        positions.Add(anchor);
        textureCoordinates.Add(new Point(0, 0));
        textureCoordinates.Add(new Point(1, 0));
        textureCoordinates.Add(new Point(1, 1));
        triangleIndices.Add(start);
        triangleIndices.Add(start + 1);
        triangleIndices.Add(start + 2);
    }
}
