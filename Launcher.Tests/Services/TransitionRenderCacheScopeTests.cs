/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Services;

namespace Launcher.Tests.Services;

[Collection(TransitionRenderingTestCollection.Name)]
public sealed class TransitionRenderCacheScopeTests
{
    private static readonly TransitionRenderCacheCapabilities SupportedCapabilities = new(
        RenderingTier: 2,
        MaximumTextureSize: new Size(8192d, 8192d),
        MaximumEstimatedBytes: TransitionRenderCacheCapabilities.DefaultMaximumEstimatedBytes);

    [Fact]
    public void CacheScopeInstallsTemporaryCacheAndRestoresOriginalState()
    {
        RunOnStaThread(() =>
        {
            var fixture = CreateFixture();
            try
            {
                fixture.Window.Show();
                PumpDispatcher(DispatcherPriority.Render);
                var initialOwnedCount = TransitionRenderCacheScope.ActiveOwnedCacheCount;

                var scope = TransitionRenderCacheScope.TryAcquire(
                    "Test:Single",
                    [fixture.Element],
                    SupportedCapabilities);

                Assert.True(scope.IsActive);
                Assert.Equal(320L * 200L * 4L, scope.EstimatedBytes);
                var cache = Assert.IsType<BitmapCache>(fixture.Element.CacheMode);
                Assert.Equal(1d, cache.RenderAtScale);
                Assert.False(cache.EnableClearType);
                // 过渡缓存必须保持关闭像素对齐，否则平移中的缓存层会被吸附到像素栅格，
                // 释放缓存时内容会横向跳动一格。
                Assert.False(cache.SnapsToDevicePixels);
                Assert.Equal(initialOwnedCount + 1, TransitionRenderCacheScope.ActiveOwnedCacheCount);

                scope.Dispose();

                Assert.False(scope.IsActive);
                Assert.Null(fixture.Element.CacheMode);
                Assert.Equal(initialOwnedCount, TransitionRenderCacheScope.ActiveOwnedCacheCount);
            }
            finally
            {
                fixture.Window.Close();
            }
        });
    }

    [Fact]
    public void NestedScopesShareInstalledCacheUntilLastScopeIsReleased()
    {
        RunOnStaThread(() =>
        {
            var fixture = CreateFixture();
            try
            {
                fixture.Window.Show();
                PumpDispatcher(DispatcherPriority.Render);

                var outer = TransitionRenderCacheScope.TryAcquire(
                    "Test:Outer",
                    [fixture.Element],
                    SupportedCapabilities);
                var installedCache = fixture.Element.CacheMode;
                var inner = TransitionRenderCacheScope.TryAcquire(
                    "Test:Inner",
                    [fixture.Element],
                    SupportedCapabilities);

                Assert.True(outer.IsActive);
                Assert.True(inner.IsActive);
                Assert.Same(installedCache, fixture.Element.CacheMode);

                outer.Dispose();
                Assert.Same(installedCache, fixture.Element.CacheMode);

                inner.Dispose();
                Assert.Null(fixture.Element.CacheMode);
            }
            finally
            {
                fixture.Window.Close();
            }
        });
    }

    [Fact]
    public void ExistingCacheIsPreservedExactly()
    {
        RunOnStaThread(() =>
        {
            var existingCache = new BitmapCache { RenderAtScale = 0.75d };
            var fixture = CreateFixture(existingCache);
            try
            {
                fixture.Window.Show();
                PumpDispatcher(DispatcherPriority.Render);

                using (var scope = TransitionRenderCacheScope.TryAcquire(
                           "Test:Existing",
                           [fixture.Element],
                           SupportedCapabilities))
                {
                    Assert.True(scope.IsActive);
                    Assert.Same(existingCache, fixture.Element.CacheMode);
                }

                Assert.Same(existingCache, fixture.Element.CacheMode);
            }
            finally
            {
                fixture.Window.Close();
            }
        });
    }

    [Fact]
    public void UnsupportedOrOversizedElementsUseLiveRenderingFallback()
    {
        RunOnStaThread(() =>
        {
            var fixture = CreateFixture();
            try
            {
                fixture.Window.Show();
                PumpDispatcher(DispatcherPriority.Render);

                using var lowTier = TransitionRenderCacheScope.TryAcquire(
                    "Test:LowTier",
                    [fixture.Element],
                    SupportedCapabilities with { RenderingTier = 1 });
                using var oversized = TransitionRenderCacheScope.TryAcquire(
                    "Test:Oversized",
                    [fixture.Element],
                    SupportedCapabilities with { MaximumTextureSize = new Size(64d, 64d) });
                using var overBudget = TransitionRenderCacheScope.TryAcquire(
                    "Test:Budget",
                    [fixture.Element],
                    SupportedCapabilities with { MaximumEstimatedBytes = 1024L });

                Assert.False(lowTier.IsActive);
                Assert.Equal(TransitionRenderCacheFallbackReason.RenderingTierTooLow, lowTier.FallbackReason);
                Assert.False(oversized.IsActive);
                Assert.Equal(TransitionRenderCacheFallbackReason.TextureTooLarge, oversized.FallbackReason);
                Assert.False(overBudget.IsActive);
                Assert.Equal(TransitionRenderCacheFallbackReason.MemoryBudgetExceeded, overBudget.FallbackReason);
                Assert.Null(fixture.Element.CacheMode);
            }
            finally
            {
                fixture.Window.Close();
            }
        });
    }

    [Fact]
    public void CacheInstallationFailureFallsBackWithoutLeavingOwnedCache()
    {
        RunOnStaThread(() =>
        {
            var element = new ThrowingCacheElement { Width = 320d, Height = 200d };
            var window = new Window
            {
                Width = 320d,
                Height = 200d,
                Left = -10000d,
                Top = -10000d,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = element
            };
            try
            {
                window.Show();
                PumpDispatcher(DispatcherPriority.Render);
                var initialOwnedCount = TransitionRenderCacheScope.ActiveOwnedCacheCount;

                using var scope = TransitionRenderCacheScope.TryAcquire(
                    "Test:CreationFailure",
                    [element],
                    SupportedCapabilities);

                Assert.False(scope.IsActive);
                Assert.Equal(TransitionRenderCacheFallbackReason.CacheCreationFailed, scope.FallbackReason);
                Assert.Null(element.CacheMode);
                Assert.Equal(initialOwnedCount, TransitionRenderCacheScope.ActiveOwnedCacheCount);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void UnloadingElementReleasesOwnedCache()
    {
        RunOnStaThread(() =>
        {
            var fixture = CreateFixture();
            fixture.Window.Show();
            PumpDispatcher(DispatcherPriority.Render);
            var scope = TransitionRenderCacheScope.TryAcquire(
                "Test:Unload",
                [fixture.Element],
                SupportedCapabilities);

            fixture.Window.Close();
            PumpDispatcher(DispatcherPriority.Background);

            Assert.False(scope.IsActive);
            Assert.Null(fixture.Element.CacheMode);
        });
    }

    [Fact]
    public void AnimationContractsRemainUnchanged()
    {
        Assert.Equal(22d, PageTransitionService.TransitionOffset);
        Assert.Equal(TimeSpan.FromMilliseconds(240), PageTransitionService.TransitionDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(240), SlidingContentTransitionCoordinator.StepTransitionDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(180), SlidingContentTransitionCoordinator.FloatingElementFadeDuration);
        Assert.Equal(0.985d, SlidingContentTransitionCoordinator.DefaultTransitionScale);
    }

    [Fact]
    public void RapidPageNavigationRestoresPreviousCacheBeforeCachingNextTarget()
    {
        RunOnStaThread(() =>
        {
            var firstTarget = new Border { Width = 320d, Height = 200d };
            var secondTarget = new Border { Width = 320d, Height = 200d };
            var host = new Grid();
            host.Children.Add(firstTarget);
            host.Children.Add(secondTarget);
            var window = new Window
            {
                Width = 320d,
                Height = 200d,
                Left = -10000d,
                Top = -10000d,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = host
            };
            try
            {
                window.Show();
                PumpDispatcher(DispatcherPriority.Render);
                var service = new PageTransitionService(
                    Dispatcher.CurrentDispatcher,
                    page => page switch
                    {
                        "Settings" => firstTarget,
                        "Resources" => secondTarget,
                        _ => null
                    },
                    "Home",
                    ["Home", "Settings", "Resources"],
                    AcquireSupportedCache);

                service.MoveTo("Settings");
                PumpDispatcher(DispatcherPriority.Render);
                Assert.IsType<BitmapCache>(firstTarget.CacheMode);

                service.MoveTo("Resources");
                Assert.Null(firstTarget.CacheMode);
                Assert.Equal(1d, firstTarget.Opacity);
                Assert.Equal(0d, Assert.IsType<TranslateTransform>(firstTarget.RenderTransform).Y);
                PumpDispatcher(DispatcherPriority.Render);

                Assert.IsType<BitmapCache>(secondTarget.CacheMode);
                service.SyncTo("Resources");
                Assert.Null(secondTarget.CacheMode);
                Assert.Equal(1d, secondTarget.Opacity);
                Assert.Equal(0d, Assert.IsType<TranslateTransform>(secondTarget.RenderTransform).Y);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void NavigationBeforeRenderRestoresPreparedTargetBeforePreparingNextTarget()
    {
        RunOnStaThread(() =>
        {
            var firstTarget = new Border { Width = 320d, Height = 200d };
            var secondTarget = new Border { Width = 320d, Height = 200d };
            var host = new Grid();
            host.Children.Add(firstTarget);
            host.Children.Add(secondTarget);
            var window = new Window
            {
                Width = 320d,
                Height = 200d,
                Left = -10000d,
                Top = -10000d,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = host
            };
            try
            {
                window.Show();
                PumpDispatcher(DispatcherPriority.Render);
                var service = new PageTransitionService(
                    Dispatcher.CurrentDispatcher,
                    page => page switch
                    {
                        "Settings" => firstTarget,
                        "Resources" => secondTarget,
                        _ => null
                    },
                    "Home",
                    ["Home", "Settings", "Resources"],
                    AcquireSupportedCache);

                service.MoveTo("Settings");
                // 预热阶段必须严格大于 0，否则渲染层会剔除透明子树，首次栅格化就会落进动画里；
                // 同时必须远低于一个 8 位色阶，保证肉眼不可见。
                Assert.Equal(PageTransitionService.WarmupOpacity, firstTarget.Opacity);
                Assert.InRange(firstTarget.Opacity, double.Epsilon, 1d / 255d);

                service.MoveTo("Resources");

                Assert.Equal(1d, firstTarget.Opacity);
                Assert.Equal(0d, Assert.IsType<TranslateTransform>(firstTarget.RenderTransform).Y);
                Assert.Equal(PageTransitionService.WarmupOpacity, secondTarget.Opacity);
                Assert.Null(firstTarget.CacheMode);

                service.SyncTo("Resources");
                Assert.Equal(1d, secondTarget.Opacity);
                Assert.Equal(0d, Assert.IsType<TranslateTransform>(secondTarget.RenderTransform).Y);
                PumpDispatcher(DispatcherPriority.Render);
                Assert.Null(firstTarget.CacheMode);
                Assert.Null(secondTarget.CacheMode);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static CacheFixture CreateFixture(CacheMode? cacheMode = null)
    {
        var element = new Border
        {
            Width = 320d,
            Height = 200d,
            CacheMode = cacheMode
        };
        var window = new Window
        {
            Width = 320d,
            Height = 200d,
            Left = -10000d,
            Top = -10000d,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = element
        };
        return new CacheFixture(window, element);
    }

    private static TransitionRenderCacheScope AcquireSupportedCache(
        string transitionKind,
        IReadOnlyList<FrameworkElement> elements)
    {
        return TransitionRenderCacheScope.TryAcquire(
            transitionKind,
            elements,
            SupportedCapabilities);
    }

    private static void PumpDispatcher(DispatcherPriority priority)
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            priority,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed record CacheFixture(Window Window, Border Element);

    private sealed class ThrowingCacheElement : Border
    {
        private bool rejectNextCache = true;

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (rejectNextCache
                && e.Property == UIElement.CacheModeProperty
                && e.NewValue is BitmapCache)
            {
                rejectNextCache = false;
                throw new InvalidOperationException("Simulated cache installation failure.");
            }
        }
    }
}
