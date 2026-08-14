/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Launcher.App.Controls.Account;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Tests.Controls.Account;

public sealed class Preview3DBatchingTests
{
    [Fact]
    public void IdleViewportCache_UsesNativeScaleAndNoClearType()
    {
        RunSta(() =>
        {
            var cache = Viewport3DIdleRenderCache.CreateCache();

            Assert.True(cache.IsFrozen);
            Assert.Equal(1d, cache.RenderAtScale);
            Assert.False(cache.EnableClearType);
            Assert.True(cache.SnapsToDevicePixels);
        });
    }

    [Theory]
    [InlineData(540, 206, 1, 1, 444960)]
    [InlineData(540, 206, 2, 2, 1779840)]
    [InlineData(540, 192, 1, 1, 414720)]
    [InlineData(540, 192, 2, 2, 1658880)]
    public void IdleViewportCache_EstimatesDpiScaledTextureBudget(
        double width,
        double height,
        double dpiScaleX,
        double dpiScaleY,
        long expectedBytes)
    {
        var enabled = Viewport3DIdleRenderCache.TryEstimateTextureBytes(
            width,
            height,
            dpiScaleX,
            dpiScaleY,
            0x20000,
            out var estimatedBytes,
            out var reason);

        Assert.True(enabled);
        Assert.Equal(expectedBytes, estimatedBytes);
        Assert.Equal("None", reason);
    }

    [Fact]
    public void IdleViewportCache_RejectsLowTierAndOversizedTextures()
    {
        Assert.False(Viewport3DIdleRenderCache.TryEstimateTextureBytes(
            540,
            206,
            1,
            1,
            0x10000,
            out _,
            out var tierReason));
        Assert.Equal("RenderTierBelow2", tierReason);

        Assert.False(Viewport3DIdleRenderCache.TryEstimateTextureBytes(
            4096,
            4096,
            1,
            1,
            0x20000,
            out var estimatedBytes,
            out var budgetReason));
        Assert.True(estimatedBytes > Viewport3DIdleRenderCache.MaximumTextureBytes);
        Assert.Equal("TextureBudgetExceeded", budgetReason);
    }

    [Fact]
    public void IdleViewportCache_DisableReleasesOnlyViewportCache()
    {
        RunSta(() =>
        {
            var viewport = new Viewport3D { CacheMode = Viewport3DIdleRenderCache.CreateCache() };
            var controller = new Viewport3DIdleRenderCache(viewport, "Test");

            Assert.True(controller.IsActive);
            controller.Disable("TestComplete");
            Assert.False(controller.IsActive);
            Assert.Null(viewport.CacheMode);
        });
    }

    [Theory]
    [InlineData(typeof(SkinCarousel3DControl))]
    [InlineData(typeof(CapeCarousel3DControl))]
    public void Carousel_UsesOuterClipWithoutRedundantViewportClip(Type controlType)
    {
        RunSta(() =>
        {
            var control = Assert.IsAssignableFrom<Grid>(Activator.CreateInstance(controlType));
            var viewport = Assert.Single(control.Children.OfType<Viewport3D>());

            Assert.True(control.ClipToBounds);
            Assert.False(viewport.ClipToBounds);
        });
    }

    [Theory]
    [InlineData(MinecraftSkinModel.Classic, 64, 64, 294, 146)]
    [InlineData(MinecraftSkinModel.Slim, 64, 64, 294, 146)]
    [InlineData(MinecraftSkinModel.Classic, 64, 32, 174, 86)]
    public async Task SkinModel_MergesFacesIntoBaseAndOverlayBatches(
        MinecraftSkinModel skinModel,
        int width,
        int height,
        int expectedPositions,
        int expectedTriangles)
    {
        var texture = CreateTexture(width, height);

        var model = await Task.Run(() =>
            MinecraftSkinPreviewModelBuilder.BuildPlayerModel(texture, skinModel, 0.73));

        Assert.True(model.IsFrozen);
        Assert.Equal(2, CountGeometryModels(model));
        Assert.Equal(expectedPositions, CountPositions(model));
        Assert.Equal(expectedTriangles, CountTriangles(model));
        AssertFrozen(model);
    }

    [Fact]
    public async Task LocalSkinLoadAndModelBuild_RunEntirelyOffUiThread()
    {
        var path = Path.Combine(Path.GetTempPath(), $"launcher-skin-{Guid.NewGuid():N}.png");
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(CreateTexture(64, 64)));
            await using (var stream = File.Create(path))
                encoder.Save(stream);

            var model = await Task.Run(() =>
            {
                var bitmap = MinecraftSkinPreviewModelBuilder.LoadSkinBitmap(path);
                return MinecraftSkinPreviewModelBuilder.BuildPlayerModel(bitmap, MinecraftSkinModel.Classic);
            });

            Assert.True(model.IsFrozen);
            Assert.Equal(2, CountGeometryModels(model));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SkinAndCapeSlots_KeepCompleteGeometryWithinTwelveBatches()
    {
        var skinTexture = CreateTexture(64, 64);
        var capeTexture = CreateTexture(64, 32);
        var cape = new AccountCapeOption
        {
            Id = "test-cape",
            DisplayName = "Test",
            ImageUrl = "memory://test-cape"
        };

        var models = await Task.Run(() =>
        {
            var result = new List<Model3DGroup>();
            for (var index = 0; index < 3; index++)
                result.Add(MinecraftSkinPreviewModelBuilder.BuildPlayerModel(skinTexture, MinecraftSkinModel.Classic, index == 1 ? 1 : 0.48));
            for (var index = 0; index < 3; index++)
                result.Add(MinecraftCapePreviewModelBuilder.BuildCapeModel(cape, index == 1 ? 1 : 0.48, capeTexture));
            return result;
        });

        Assert.Equal(12, models.Sum(CountGeometryModels));
        Assert.Equal(987, models.Sum(CountPositions));
        Assert.Equal(489, models.Sum(CountTriangles));
        Assert.All(models, AssertFrozen);
    }

    [Fact]
    public void SkinBatches_KeepLegacyDoubleSidedMaterialsAndDoNotShareOverlayMaterial()
    {
        var model = MinecraftSkinPreviewModelBuilder.BuildPlayerModel(
            CreateOpaqueTexture(64, 64),
            MinecraftSkinModel.Classic);
        var batches = model.Children.OfType<GeometryModel3D>().ToArray();

        Assert.Equal(2, batches.Length);
        Assert.NotSame(batches[0].Material, batches[1].Material);
        Assert.Same(batches[0].Material, batches[0].BackMaterial);
        Assert.Same(batches[1].Material, batches[1].BackMaterial);
        Assert.All(batches, AssertAtlasCoordinatesAreAnchored);
        Assert.All(batches, batch =>
        {
            var brush = Assert.IsType<ImageBrush>(Assert.IsType<DiffuseMaterial>(batch.Material).Brush);
            var atlas = Assert.IsAssignableFrom<BitmapSource>(brush.ImageSource);
            Assert.InRange(atlas.PixelWidth, 1, 1024);
            Assert.InRange(atlas.PixelHeight, 1, 512);
        });
        var baseBrush = Assert.IsType<ImageBrush>(Assert.IsType<DiffuseMaterial>(batches[0].BackMaterial).Brush);
        var baseAtlas = Assert.IsAssignableFrom<BitmapSource>(baseBrush.ImageSource);
        var pixels = new byte[baseAtlas.PixelWidth * baseAtlas.PixelHeight * 4];
        baseAtlas.CopyPixels(pixels, baseAtlas.PixelWidth * 4, 0);
        for (var index = 3; index < pixels.Length; index += 4)
            Assert.Equal(byte.MaxValue, pixels[index]);
    }

    [Fact]
    public async Task CapeModel_MergesBackingAndTextureFacesAndCanBuildOffUiThread()
    {
        var texture = CreateTexture(64, 32);
        var cape = new AccountCapeOption
        {
            Id = "test-cape",
            DisplayName = "Test",
            ImageUrl = "memory://test-cape"
        };

        var model = await Task.Run(() => MinecraftCapePreviewModelBuilder.BuildCapeModel(cape, 0.8, texture));

        Assert.True(model.IsFrozen);
        Assert.Equal(2, CountGeometryModels(model));
        Assert.Equal(35, CountPositions(model));
        Assert.Equal(17, CountTriangles(model));
        AssertAtlasCoordinatesAreAnchored(model.Children.OfType<GeometryModel3D>().Last());
        AssertFrozen(model);
    }

    [Fact]
    public async Task NoneCape_CanBuildAndFreezeOffUiThread()
    {
        var cape = new AccountCapeOption
        {
            Id = "none",
            DisplayName = "None",
            IsNone = true
        };

        var model = await Task.Run(() => MinecraftCapePreviewModelBuilder.BuildCapeModel(cape));

        Assert.True(model.IsFrozen);
        Assert.InRange(CountGeometryModels(model), 1, 2);
        AssertFrozen(model);
    }

    [Fact]
    public void CapeCacheKey_ChangesWhenSynchronouslyLoadedTextureBecomesAvailable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"launcher-cape-{Guid.NewGuid():N}.png");
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(CreateTexture(64, 32)));
            using (var stream = File.Create(path))
                encoder.Save(stream);

            RunSta(() =>
            {
                var control = new CapeCarousel3DControl();
                var cape = new AccountCapeOption
                {
                    Id = "local-cape",
                    DisplayName = "Local",
                    ImageUrl = path
                };
                var method = typeof(CapeCarousel3DControl).GetMethod(
                    "CreateCapeBuildRequest",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.NotNull(method);
                var first = method!.Invoke(control, [CapeCarouselSlot.Center, cape, 1d]);
                var second = method.Invoke(control, [CapeCarouselSlot.Center, cape, 1d]);
                Assert.NotNull(first);
                Assert.NotNull(second);
                var requestType = first!.GetType();
                var textureProperty = requestType.GetProperty("Texture");
                var keyProperty = requestType.GetProperty("Key");
                Assert.NotNull(textureProperty);
                Assert.NotNull(keyProperty);
                Assert.Null(textureProperty!.GetValue(first));
                Assert.NotNull(textureProperty.GetValue(second));
                Assert.NotEqual(keyProperty!.GetValue(first), keyProperty.GetValue(second));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TextureAtlas_ReplicatesEdgePixelsIntoGutterAndPreservesBrightness()
    {
        var pixels = new byte[]
        {
            10, 20, 30, 255,
            100, 120, 140, 128
        };
        var source = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        source.Freeze();

        var atlas = PreviewTextureAtlasBuilder.Build(
            source,
            [new Int32Rect(0, 0, 2, 1)],
            pixelScale: 1,
            brightness: 0.5,
            maximumSourceRowWidth: 8);

        Assert.Equal(4, atlas.SourcePixelWidth);
        Assert.Equal(3, atlas.SourcePixelHeight);
        var output = new byte[atlas.Bitmap.PixelWidth * atlas.Bitmap.PixelHeight * 4];
        atlas.Bitmap.CopyPixels(output, atlas.Bitmap.PixelWidth * 4, 0);
        AssertPixel(output, atlas.Bitmap.PixelWidth, 0, 1, 5, 10, 15, 255);
        AssertPixel(output, atlas.Bitmap.PixelWidth, 1, 1, 5, 10, 15, 255);
        AssertPixel(output, atlas.Bitmap.PixelWidth, 2, 1, 50, 60, 70, 128);
        AssertPixel(output, atlas.Bitmap.PixelWidth, 3, 1, 50, 60, 70, 128);
    }

    [Fact]
    public void CarouselAnimationLifecycle_WritesFinalValuesAndRemovesEveryClock()
    {
        RunSta(() =>
        {
            var scale = new ScaleTransform3D(0.21, 0.21, 0.21);
            var translate = new TranslateTransform3D(-10, 0, 0);
            var animation = new DoubleAnimation(10, TimeSpan.FromSeconds(10))
            {
                FillBehavior = FillBehavior.HoldEnd
            };
            translate.BeginAnimation(TranslateTransform3D.OffsetXProperty, animation);
            scale.BeginAnimation(ScaleTransform3D.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform3D.ScaleYProperty, animation);
            scale.BeginAnimation(ScaleTransform3D.ScaleZProperty, animation);

            CarouselAnimationLifecycle.CompleteAndRemoveClocks(scale, translate, 0, 0.3);

            Assert.False(translate.HasAnimatedProperties);
            Assert.False(scale.HasAnimatedProperties);
            Assert.Equal(0, translate.OffsetX);
            Assert.Equal(0.3, scale.ScaleX);
            Assert.Equal(0.3, scale.ScaleY);
            Assert.Equal(0.3, scale.ScaleZ);
        });
    }

    [Theory]
    [InlineData(typeof(SkinCarousel3DControl))]
    [InlineData(typeof(CapeCarousel3DControl))]
    public void CarouselAnimationContract_RemainsSixHundredMilliseconds(Type controlType)
    {
        var field = controlType.GetField(
            "AnimationMilliseconds",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(field);
        Assert.Equal(600, field!.GetRawConstantValue());
    }

    private static BitmapSource CreateTexture(int width, int height)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * stride) + (x * 4);
                pixels[offset] = (byte)(x * 3);
                pixels[offset + 1] = (byte)(y * 3);
                pixels[offset + 2] = (byte)(x + y);
                pixels[offset + 3] = (byte)((x + y) % 5 == 0 ? 96 : 255);
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource CreateOpaqueTexture(int width, int height)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 80;
            pixels[index + 1] = 150;
            pixels[index + 2] = 220;
            pixels[index + 3] = byte.MaxValue;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static int CountGeometryModels(Model3D model) =>
        model is GeometryModel3D
            ? 1
            : model is Model3DGroup group
                ? group.Children.Sum(CountGeometryModels)
                : 0;

    private static int CountPositions(Model3D model) =>
        model is GeometryModel3D geometry
            ? ((MeshGeometry3D)geometry.Geometry).Positions.Count
            : model is Model3DGroup group
                ? group.Children.Sum(CountPositions)
                : 0;

    private static int CountTriangles(Model3D model) =>
        model is GeometryModel3D geometry
            ? ((MeshGeometry3D)geometry.Geometry).TriangleIndices.Count / 3
            : model is Model3DGroup group
                ? group.Children.Sum(CountTriangles)
                : 0;

    private static void AssertFrozen(Model3D model)
    {
        Assert.True(model.IsFrozen);
        if (model is GeometryModel3D geometry)
        {
            Assert.True(geometry.Geometry.IsFrozen);
            if (geometry.Material is not null)
                Assert.True(geometry.Material.IsFrozen);
            if (geometry.BackMaterial is not null)
                Assert.True(geometry.BackMaterial.IsFrozen);
        }
        else if (model is Model3DGroup group)
        {
            Assert.All(group.Children, AssertFrozen);
        }
    }

    private static void AssertAtlasCoordinatesAreAnchored(GeometryModel3D model)
    {
        var mesh = Assert.IsType<MeshGeometry3D>(model.Geometry);
        Assert.True(mesh.Positions.Count >= 3);
        Assert.Equal(new Point(0, 0), mesh.TextureCoordinates[^3]);
        Assert.Equal(new Point(1, 0), mesh.TextureCoordinates[^2]);
        Assert.Equal(new Point(1, 1), mesh.TextureCoordinates[^1]);
        Assert.Equal(mesh.Positions[^3], mesh.Positions[^2]);
        Assert.Equal(mesh.Positions[^2], mesh.Positions[^1]);
    }

    private static void AssertPixel(
        byte[] pixels,
        int width,
        int x,
        int y,
        byte blue,
        byte green,
        byte red,
        byte alpha)
    {
        var offset = ((y * width) + x) * 4;
        Assert.Equal(blue, pixels[offset]);
        Assert.Equal(green, pixels[offset + 1]);
        Assert.Equal(red, pixels[offset + 2]);
        Assert.Equal(alpha, pixels[offset + 3]);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw failure;
    }
}
