/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.Xml.Linq;
using Launcher.Application.Accounts;

namespace Launcher.App.Controls.Account;

public static class MinecraftCapePreviewModelBuilder
{
    // Minecraft 披风纹理使用像素坐标裁剪；NearestNeighbor 保持预览边缘清晰。
    private const int PixelScale = 8;
    private const string NoneCapeIconResource = "/Assets/Icons/account_page/account_page_forbid.svg";

    public static AmbientLight CreateAmbientLight()
    {
        return new AmbientLight(Color.FromRgb(168, 168, 168));
    }

    public static DirectionalLight CreateDirectionalLight()
    {
        return new DirectionalLight(Color.FromRgb(132, 132, 132), new Vector3D(-0.2, -0.35, -0.9));
    }

    /// <summary>
    /// 从 Minecraft 披风纹理构建保持像素锐利的双层长方体预览模型。
    /// </summary>
    public static Model3DGroup BuildCapeModel(
        AccountCapeOption cape,
        double brightness = 1,
        BitmapSource? texture = null)
    {
        ArgumentNullException.ThrowIfNull(cape);
        if (cape.IsNone)
            return BuildNoneCapeModel(brightness);

        var model = new Model3DGroup();
        var baseMaterial = CreateSolidMaterial(Color.FromArgb(
            160,
            ApplyBrightness(74, brightness),
            ApplyBrightness(92, brightness),
            ApplyBrightness(118, brightness)));
        var baseMesh = new PreviewMeshBuilder();
        AddBatchedCapeFace(baseMesh, new Rect3D(-5.15, -0.15, 0.42, 10.3, 16.3, 0), new Rect(0, 0, 1, 1));
        AddBatchedCapeFace(baseMesh, new Rect3D(-5.15, -0.15, -0.58, 10.3, 16.3, 0), new Rect(0, 0, 1, 1));
        AddBatchedCapeMesh(model, baseMesh, baseMaterial);

        if (!string.IsNullOrWhiteSpace(cape.ImageUrl) && texture is not null)
        {
            try
            {
                var faces = new[]
                {
                    new BatchedCapeFace(new Rect3D(-5, 0, 0.5, 10, 16, 0), new Int32Rect(1, 1, 10, 16), true),
                    new BatchedCapeFace(new Rect3D(-5, 0, -0.5, 10, 16, 0), new Int32Rect(12, 1, 10, 16), false),
                    new BatchedCapeFace(new Rect3D(-5, 16, -0.5, 10, 0, 1), new Int32Rect(1, 0, 10, 1), true),
                    new BatchedCapeFace(new Rect3D(-5, 0, -0.5, 10, 0, 1), new Int32Rect(11, 0, 10, 1), false),
                    new BatchedCapeFace(new Rect3D(-5, 0, -0.5, 0, 16, 1), new Int32Rect(0, 1, 1, 16), false),
                    new BatchedCapeFace(new Rect3D(5, 0, -0.5, 0, 16, 1), new Int32Rect(11, 1, 1, 16), true)
                };
                var atlas = PreviewTextureAtlasBuilder.Build(
                    texture,
                    faces.Select(item => item.TextureRect),
                    PixelScale,
                    brightness,
                    maximumSourceRowWidth: 64);
                var brush = new ImageBrush(atlas.Bitmap)
                {
                    Stretch = Stretch.Fill,
                    TileMode = TileMode.None
                };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
                brush.Freeze();
                var material = new DiffuseMaterial(brush);
                material.Freeze();
                var textureMesh = new PreviewMeshBuilder();
                foreach (var face in faces)
                    AddBatchedCapeFace(textureMesh, face.Bounds, atlas.TextureCoordinates[face.TextureRect], face.ReverseWinding);
                AddBatchedCapeMesh(
                    model,
                    textureMesh,
                    material,
                    doubleSided: false,
                    anchorUnitTextureCoordinates: true);
            }
            catch
            {
                // Keep the stable backing surface for malformed or undersized textures.
            }
        }

        ApplyCapeRotationAndFreeze(model);
        return model;
    }

#if false
    private static Model3DGroup BuildCapeModelLegacy(
        AccountCapeOption cape,
        double brightness = 1,
        BitmapSource? texture = null)
    {
        if (cape.IsNone)
            // “无披风”使用主题兼容的占位牌，而不是尝试加载不存在的纹理。
            return BuildNoneCapeModel(brightness);

        var model = new Model3DGroup();
        model.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), -6), 0, 8, 0);
        var baseMaterial = CreateSolidMaterial(Color.FromArgb(
            160,
            ApplyBrightness(74, brightness),
            ApplyBrightness(92, brightness),
            ApplyBrightness(118, brightness)));

        // 先绘制稍大的半透明底板，纹理尚未下载时仍能保持轮播尺寸和深度轮廓。
        AddSolidFace(model, new Rect3D(-5.15, -0.15, 0.42, 10.3, 16.3, 0), baseMaterial);
        AddSolidFace(model, new Rect3D(-5.15, -0.15, -0.58, 10.3, 16.3, 0), baseMaterial);

        if (string.IsNullOrWhiteSpace(cape.ImageUrl) || texture is null)
            return model;

        try
        {
            // 每个面使用官方纹理布局中的独立像素矩形，避免依赖插值或整图 UV 计算。
            AddFace(model, texture, new Rect3D(-5, 0, 0.5, 10, 16, 0), new Int32Rect(1, 1, 10, 16), brightness);
            AddFace(model, texture, new Rect3D(-5, 0, -0.5, 10, 16, 0), new Int32Rect(12, 1, 10, 16), brightness);
            AddFace(model, texture, new Rect3D(-5, 16, -0.5, 10, 0, 1), new Int32Rect(1, 0, 10, 1), brightness);
            AddFace(model, texture, new Rect3D(-5, 0, -0.5, 10, 0, 1), new Int32Rect(11, 0, 10, 1), brightness);
            AddFace(model, texture, new Rect3D(-5, 0, -0.5, 0, 16, 1), new Int32Rect(0, 1, 1, 16), brightness);
            AddFace(model, texture, new Rect3D(5, 0, -0.5, 0, 16, 1), new Int32Rect(11, 1, 1, 16), brightness);
        }
        catch
        {
            // 非标准或尺寸不足的纹理退回底板模型，账户页面仍保持可用。
        }

        return model;
    }

#endif
    private static void AddBatchedCapeFace(
        PreviewMeshBuilder mesh,
        Rect3D bounds,
        Rect textureCoordinates,
        bool reverseWinding = false)
    {
        mesh.AddQuad(
            new Point3D(bounds.X, bounds.Y + bounds.SizeY, bounds.Z),
            new Point3D(bounds.X + bounds.SizeX, bounds.Y + bounds.SizeY, bounds.Z + bounds.SizeZ),
            new Point3D(bounds.X + bounds.SizeX, bounds.Y, bounds.Z + bounds.SizeZ),
            new Point3D(bounds.X, bounds.Y, bounds.Z),
            textureCoordinates,
            reverseWinding);
    }

    private static void AddBatchedCapeMesh(
        Model3DGroup target,
        PreviewMeshBuilder mesh,
        Material material,
        bool doubleSided = true,
        bool anchorUnitTextureCoordinates = false)
    {
        if (mesh.QuadCount == 0)
            return;

        var geometryModel = new GeometryModel3D
        {
            Geometry = mesh.Build(anchorUnitTextureCoordinates),
            Material = material,
            BackMaterial = doubleSided ? material : null
        };
        geometryModel.Freeze();
        target.Children.Add(geometryModel);
    }

    private static void ApplyCapeRotationAndFreeze(Model3DGroup model)
    {
        var rotation = new AxisAngleRotation3D(new Vector3D(1, 0, 0), -6);
        rotation.Freeze();
        var transform = new RotateTransform3D(rotation, 0, 8, 0);
        transform.Freeze();
        model.Transform = transform;
        model.Freeze();
    }

    private sealed record BatchedCapeFace(Rect3D Bounds, Int32Rect TextureRect, bool ReverseWinding);

    private static Model3DGroup BuildNoneCapeModel(double brightness)
    {
        var model = new Model3DGroup();
        model.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), -6), 0, 8, 0);
        var surface = CreateSolidMaterial(Color.FromArgb(
            42,
            ApplyBrightness(255, brightness),
            ApplyBrightness(255, brightness),
            ApplyBrightness(255, brightness)));
        var sign = CreateSvgIconMaterial(NoneCapeIconResource, brightness);

        AddSolidFace(model, new Rect3D(-5, 0, 0, 10, 16, 0), surface);
        if (sign is not null)
            AddSolidFace(model, new Rect3D(-2.2, 5.25, 0.08, 4.4, 4.4, 0), sign);
        ApplyCapeRotationAndFreeze(model);
        return model;
    }

    private static void AddFace(
        Model3DGroup group,
        BitmapSource texture,
        Rect3D bounds,
        Int32Rect textureRect,
        double brightness)
    {
        var material = CreateImageMaterial(texture, textureRect, brightness);
        group.Children.Add(new GeometryModel3D
        {
            Geometry = CreateFaceMesh(bounds),
            Material = material,
            BackMaterial = material
        });
    }

    private static void AddSolidFace(Model3DGroup group, Rect3D bounds, Material material, double rotationDegrees = 0)
    {
        var model = new GeometryModel3D
        {
            Geometry = CreateFaceMesh(bounds),
            Material = material,
            BackMaterial = material
        };

        if (Math.Abs(rotationDegrees) > double.Epsilon)
            model.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), rotationDegrees));

        group.Children.Add(model);
    }

    private static Material CreateImageMaterial(BitmapSource texture, Int32Rect textureRect, double brightness)
    {
        var brush = new ImageBrush(CreatePixelSharpFaceBitmap(texture, textureRect, brightness))
        {
            Stretch = Stretch.Fill,
            TileMode = TileMode.None
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        material.Freeze();
        return material;
    }

    private static Material CreateSolidMaterial(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        material.Freeze();
        return material;
    }

    private static Material? CreateSvgIconMaterial(string resourcePath, double brightness)
    {
        try
        {
            var iconBitmap = RenderSvgIconBitmap(resourcePath, brightness);
            var brush = new ImageBrush(iconBitmap)
            {
                Stretch = Stretch.Fill,
                TileMode = TileMode.None
            };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
            brush.Freeze();
            var material = new DiffuseMaterial(brush);
            material.Freeze();
            return material;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource RenderSvgIconBitmap(string resourcePath, double brightness)
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri(resourcePath, UriKind.Relative));
        if (resource is null)
            throw new InvalidOperationException("SVG resource was not found.");

        using var stream = resource.Stream;
        var document = XDocument.Load(stream);
        var root = document.Root ?? throw new InvalidOperationException("SVG root was not found.");
        var viewBox = ParseViewBox(root.Attribute("viewBox")?.Value);
        var geometries = root
            .Descendants()
            .Where(element => element.Name.LocalName == "path")
            .Select(element => (
                Geometry: Geometry.Parse(element.Attribute("d")?.Value ?? string.Empty),
                StrokeWidth: ParseDouble(element.Attribute("stroke-width")?.Value, 4)))
            .ToList();

        const int size = 128;
        var colorValue = ApplyBrightness(255, brightness);
        var brush = new SolidColorBrush(Color.FromArgb(230, colorValue, colorValue, colorValue));
        brush.Freeze();
        var drawingVisual = new DrawingVisual();
        using (var context = drawingVisual.RenderOpen())
        {
            var scale = Math.Min(size / viewBox.Width, size / viewBox.Height);
            var offsetX = (size - viewBox.Width * scale) / 2;
            var offsetY = (size - viewBox.Height * scale) / 2;
            context.PushTransform(new TranslateTransform(offsetX, offsetY));
            context.PushTransform(new ScaleTransform(scale, scale));
            context.PushTransform(new TranslateTransform(-viewBox.X, -viewBox.Y));
            foreach (var item in geometries)
            {
                item.Geometry.Freeze();
                var pen = new Pen(brush, item.StrokeWidth)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                pen.Freeze();
                context.DrawGeometry(null, pen, item.Geometry);
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawingVisual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Rect ParseViewBox(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new Rect(0, 0, 48, 48);

        var parts = value
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => double.Parse(part, CultureInfo.InvariantCulture))
            .ToArray();

        return parts.Length == 4
            ? new Rect(parts[0], parts[1], parts[2], parts[3])
            : new Rect(0, 0, 48, 48);
    }

    private static double ParseDouble(string? value, double fallback)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;
    }

    private static MeshGeometry3D CreateFaceMesh(Rect3D b)
    {
        var p0 = new Point3D(b.X, b.Y + b.SizeY, b.Z);
        var p1 = new Point3D(b.X + b.SizeX, b.Y + b.SizeY, b.Z + b.SizeZ);
        var p2 = new Point3D(b.X + b.SizeX, b.Y, b.Z + b.SizeZ);
        var p3 = new Point3D(b.X, b.Y, b.Z);
        var mesh = new MeshGeometry3D
        {
            Positions = [p0, p1, p2, p3],
            TextureCoordinates =
            [
                new Point(0, 0),
                new Point(1, 0),
                new Point(1, 1),
                new Point(0, 1)
            ],
            TriangleIndices = [0, 1, 2, 0, 2, 3]
        };
        mesh.Freeze();
        return mesh;
    }

    public static BitmapSource CreatePixelSharpFaceBitmap(
        BitmapSource texture,
        Int32Rect textureRect,
        double brightness)
    {
        brightness = Math.Clamp(brightness, 0, 1);
        var source = EnsureBgra32(texture);
        var x = Math.Clamp(textureRect.X, 0, source.PixelWidth - 1);
        var y = Math.Clamp(textureRect.Y, 0, source.PixelHeight - 1);
        var rect = new Int32Rect(
            x,
            y,
            Math.Max(1, Math.Min(textureRect.Width, source.PixelWidth - x)),
            Math.Max(1, Math.Min(textureRect.Height, source.PixelHeight - y)));
        var stride = rect.Width * 4;
        var pixels = new byte[stride * rect.Height];
        source.CopyPixels(rect, pixels, stride, 0);

        var outputWidth = rect.Width * PixelScale;
        var outputHeight = rect.Height * PixelScale;
        var outputStride = outputWidth * 4;
        var output = new byte[outputStride * outputHeight];
        for (var outputY = 0; outputY < outputHeight; outputY++)
        {
            var sourceY = outputY / PixelScale;
            for (var outputX = 0; outputX < outputWidth; outputX++)
            {
                var sourceX = outputX / PixelScale;
                var sourceIndex = sourceY * stride + sourceX * 4;
                var outputIndex = outputY * outputStride + outputX * 4;
                output[outputIndex] = ApplyBrightness(pixels[sourceIndex], brightness);
                output[outputIndex + 1] = ApplyBrightness(pixels[sourceIndex + 1], brightness);
                output[outputIndex + 2] = ApplyBrightness(pixels[sourceIndex + 2], brightness);
                output[outputIndex + 3] = pixels[sourceIndex + 3];
            }
        }

        var bitmap = BitmapSource.Create(
            outputWidth,
            outputHeight,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            output,
            outputStride);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte ApplyBrightness(byte value, double brightness)
    {
        return (byte)Math.Round(value * brightness);
    }

    private static BitmapSource EnsureBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32)
            return source;

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }
}
