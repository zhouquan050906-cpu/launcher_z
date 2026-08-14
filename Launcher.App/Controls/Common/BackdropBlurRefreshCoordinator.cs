/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Launcher.App.Controls;

[Flags]
internal enum BackdropBlurRefreshReason
{
    None = 0,
    Lifecycle = 1,
    Layout = 2,
    Scroll = 4,
    Size = 8,
    Source = 16,
    ContinuousAnimation = 32
}

/// <summary>
/// Coalesces backdrop refreshes per window, observes shared layout sources once,
/// and processes dirty controls at most once for each WPF composition frame.
/// </summary>
internal sealed class BackdropBlurRefreshCoordinator
{
    private const string ImageBackgroundEnabledResourceKey = "Is.ImageBackground.ControlTint.Enabled";
    private const string SurfaceBlurEnabledResourceKey = "Is.Surface.BackdropBlur.Enabled";
    private const string SecondaryMenuBlurEnabledResourceKey = "Is.SecondaryMenu.BackdropBlur.Enabled";
    private static readonly ConditionalWeakTable<Window, BackdropBlurRefreshCoordinator> Coordinators = new();

    private readonly Window window;
    private readonly HashSet<BackdropBlurBorder> registeredControls = [];
    private readonly HashSet<BackdropBlurBorder> dirtyControls = [];
    private readonly HashSet<BackdropBlurBorder> pendingScrollGeometryControls = [];
    private readonly Dictionary<BackdropBlurBorder, Registration> registrations = [];
    private readonly Dictionary<FrameworkElement, HashSet<BackdropBlurBorder>> controlsBySource = [];
    private readonly Dictionary<ScrollViewer, HashSet<BackdropBlurBorder>> controlsByScrollViewer = [];
    private readonly Dictionary<FrameworkElement, int> continuousScopes = [];
    private bool isWindowObservationActive;
    private bool isRenderingSubscribed;
    private bool isClosed;
    private bool isLayoutCheckPending;
    private TimeSpan? lastRenderingTime;
    private long totalBatchCount;
    private long totalRefreshCount;

    private BackdropBlurRefreshCoordinator(Window window)
    {
        this.window = window;
    }

    internal int RegisteredCount => registeredControls.Count;

    internal int PendingCount => dirtyControls.Count;

    internal int ObservedSourceCount => controlsBySource.Count;

    internal int ObservedScrollViewerCount => controlsByScrollViewer.Count;

    internal bool IsWindowLayoutObserved => isWindowObservationActive;

    internal bool IsRenderingActive => isRenderingSubscribed;

    internal bool IsContinuousRenderingActive =>
        isRenderingSubscribed && continuousScopes.Count > 0;

    internal long TotalBatchCount => totalBatchCount;

    internal long TotalRefreshCount => totalRefreshCount;

    internal static BackdropBlurRefreshCoordinator? TryGet(FrameworkElement element)
    {
        var owner = Window.GetWindow(element);
        return owner is null ? null : Coordinators.GetValue(owner, static window => new(window));
    }

    internal static IDisposable BeginContinuousRefresh(params FrameworkElement[] scopes)
    {
        var validScopes = scopes
            .Where(static scope => scope is not null)
            .Distinct()
            .ToArray();
        if (validScopes.Length == 0)
            return EmptyLease.Instance;

        var owner = Window.GetWindow(validScopes[0]);
        if (owner is null)
            return EmptyLease.Instance;

        var coordinator = Coordinators.GetValue(owner, static window => new(window));
        var ownedScopes = validScopes
            .Where(scope => ReferenceEquals(Window.GetWindow(scope), owner))
            .ToArray();
        if (ownedScopes.Length == 0)
            return EmptyLease.Instance;

        coordinator.AcquireContinuousScopes(ownedScopes);
        return new ContinuousRefreshLease(coordinator, ownedScopes);
    }

    internal static bool HasActiveImageBackdropBlur(params FrameworkElement[] scopes)
    {
        var validScopes = scopes
            .Where(static scope => scope is not null)
            .Distinct()
            .ToArray();
        if (validScopes.Length == 0 || !IsImageControlBlurEnabled(validScopes[0]))
            return false;

        var owner = Window.GetWindow(validScopes[0]);
        if (owner is null || !Coordinators.TryGetValue(owner, out var coordinator))
            return false;

        var ownedScopes = validScopes
            .Where(scope => ReferenceEquals(Window.GetWindow(scope), owner))
            .ToArray();
        return ownedScopes.Length > 0
            && coordinator.registeredControls.Any(
                control => control.IsRefreshEligible && IsInsideAnyScope(control, ownedScopes));
    }

    internal static void RequestScopeRefresh(params FrameworkElement[] scopes)
    {
        var validScopes = scopes
            .Where(static scope => scope is not null)
            .Distinct()
            .ToArray();
        if (validScopes.Length == 0)
            return;

        var owner = Window.GetWindow(validScopes[0]);
        if (owner is null || !Coordinators.TryGetValue(owner, out var coordinator))
            return;

        coordinator.QueueScopeRefresh(
            validScopes.Where(scope => ReferenceEquals(Window.GetWindow(scope), owner)));
    }

    internal void Register(
        BackdropBlurBorder control,
        FrameworkElement source,
        ScrollViewer? scrollViewer)
    {
        if (isClosed)
            return;

        if (registrations.TryGetValue(control, out var previous))
        {
            if (ReferenceEquals(previous.Source, source)
                && ReferenceEquals(previous.ScrollViewer, scrollViewer))
            {
                return;
            }

            RemoveRegistration(control, previous);
        }
        else
        {
            registeredControls.Add(control);
        }

        var registration = new Registration(source, scrollViewer);
        registrations[control] = registration;
        AddSourceRegistration(source, control);
        if (scrollViewer is not null)
            AddScrollViewerRegistration(scrollViewer, control);

        EnsureWindowObservation();
        UpdateRenderingSubscription();
    }

    internal void Unregister(BackdropBlurBorder control)
    {
        if (registrations.Remove(control, out var registration))
            RemoveRegistration(control, registration);

        registeredControls.Remove(control);
        dirtyControls.Remove(control);
        pendingScrollGeometryControls.Remove(control);

        if (registeredControls.Count == 0)
        {
            isLayoutCheckPending = false;
            pendingScrollGeometryControls.Clear();
            StopWindowObservation();
        }

        UpdateRenderingSubscription();
    }

    internal void RequestRefresh(BackdropBlurBorder control, BackdropBlurRefreshReason reason)
    {
        if (!registeredControls.Contains(control))
            return;

        AddDirty(control);
        UpdateRenderingSubscription();
    }

    internal void ProcessPendingBatchForTesting()
    {
        ProcessRenderFrame(GetNextTestingRenderingTime());
    }

    internal void ProcessContinuousFrameForTesting()
    {
        ProcessRenderFrame(GetNextTestingRenderingTime());
    }

    internal void ProcessRenderFrameForTesting(TimeSpan renderingTime)
    {
        ProcessRenderFrame(renderingTime);
    }

    internal void ProcessLayoutUpdatedForTesting()
    {
        ProcessLayoutUpdated();
    }

    internal void ProcessScrollChangedForTesting(ScrollViewer scrollViewer)
    {
        ProcessScrollChanged(scrollViewer);
    }

    private TimeSpan GetNextTestingRenderingTime()
    {
        return (lastRenderingTime ?? TimeSpan.Zero) + TimeSpan.FromTicks(1);
    }

    private void AddDirty(BackdropBlurBorder control)
    {
        dirtyControls.Add(control);
    }

    private void AddDirtyRange(IEnumerable<BackdropBlurBorder> controls)
    {
        foreach (var control in controls)
        {
            if (registeredControls.Contains(control))
                AddDirty(control);
        }

        UpdateRenderingSubscription();
    }

    private void QueueScopeRefresh(IEnumerable<FrameworkElement> scopes)
    {
        var ownedScopes = scopes.ToArray();
        if (ownedScopes.Length == 0 || registeredControls.Count == 0)
            return;

        foreach (var control in registeredControls)
        {
            if (!control.IsRefreshEligible || !IsInsideAnyScope(control, ownedScopes))
                continue;

            control.InvalidatePreparedGeometry();
            AddDirty(control);
        }

        UpdateRenderingSubscription();
    }

    private void ProcessRenderFrame(TimeSpan renderingTime)
    {
        if (lastRenderingTime == renderingTime)
            return;

        lastRenderingTime = renderingTime;

        if (isLayoutCheckPending || pendingScrollGeometryControls.Count > 0)
        {
            var includeRemainingLayoutControls = isLayoutCheckPending;
            isLayoutCheckPending = false;
            ProcessPendingGeometryChecks(includeRemainingLayoutControls);
        }

        if (continuousScopes.Count > 0 && registeredControls.Count > 0)
        {
            foreach (var control in registeredControls)
            {
                if (!control.IsRefreshEligible || !IsInsideContinuousScope(control))
                    continue;

                control.InvalidatePreparedGeometry();
                AddDirty(control);
            }
        }

        if (dirtyControls.Count > 0)
        {
            var snapshot = dirtyControls.ToArray();
            dirtyControls.Clear();
            ProcessBatch(snapshot);
        }

        UpdateRenderingSubscription();
    }

    private void ProcessBatch(IReadOnlyList<BackdropBlurBorder> controls)
    {
        totalBatchCount++;
        foreach (var control in controls)
        {
            if (!registeredControls.Contains(control) || !control.IsRefreshEligible)
                continue;

            control.RefreshBackdrop();
            totalRefreshCount++;
        }
    }

    private void AcquireContinuousScopes(IEnumerable<FrameworkElement> scopes)
    {
        if (isClosed)
            return;

        foreach (var scope in scopes)
        {
            continuousScopes.TryGetValue(scope, out var count);
            continuousScopes[scope] = count + 1;
        }

        UpdateRenderingSubscription();
    }

    private void ReleaseContinuousScopes(IEnumerable<FrameworkElement> scopes)
    {
        foreach (var scope in scopes)
        {
            if (!continuousScopes.TryGetValue(scope, out var count))
                continue;

            if (count <= 1)
                continuousScopes.Remove(scope);
            else
                continuousScopes[scope] = count - 1;
        }

        UpdateRenderingSubscription();
    }

    private void UpdateRenderingSubscription()
    {
        var shouldSubscribe = !isClosed
            && registeredControls.Count > 0
            && (dirtyControls.Count > 0
                || continuousScopes.Count > 0
                || isLayoutCheckPending
                || pendingScrollGeometryControls.Count > 0);
        if (shouldSubscribe == isRenderingSubscribed)
            return;

        if (shouldSubscribe)
            CompositionTarget.Rendering += CompositionTarget_Rendering;
        else
            CompositionTarget.Rendering -= CompositionTarget_Rendering;

        isRenderingSubscribed = shouldSubscribe;
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        var renderingTime = e is RenderingEventArgs renderingEventArgs
            ? renderingEventArgs.RenderingTime
            : GetNextTestingRenderingTime();
        ProcessRenderFrame(renderingTime);
    }

    private bool IsInsideContinuousScope(BackdropBlurBorder control)
    {
        foreach (var scope in continuousScopes.Keys)
        {
            if (!scope.IsVisible)
                continue;

            if (ReferenceEquals(scope, control) || scope.IsAncestorOf(control))
                return true;
        }

        return false;
    }

    private static bool IsInsideAnyScope(
        BackdropBlurBorder control,
        IReadOnlyList<FrameworkElement> scopes)
    {
        foreach (var scope in scopes)
        {
            if (!scope.IsVisible)
                continue;

            if (ReferenceEquals(scope, control) || scope.IsAncestorOf(control))
                return true;
        }

        return false;
    }

    private static bool IsImageControlBlurEnabled(FrameworkElement scope)
    {
        return scope.TryFindResource(ImageBackgroundEnabledResourceKey) is true
            && (scope.TryFindResource(SurfaceBlurEnabledResourceKey) is true
                || scope.TryFindResource(SecondaryMenuBlurEnabledResourceKey) is true);
    }

    private void EnsureWindowObservation()
    {
        if (isWindowObservationActive)
            return;

        window.LayoutUpdated += Window_LayoutUpdated;
        window.Closed += Window_Closed;
        isWindowObservationActive = true;
    }

    private void StopWindowObservation()
    {
        if (!isWindowObservationActive)
            return;

        window.LayoutUpdated -= Window_LayoutUpdated;
        window.Closed -= Window_Closed;
        isWindowObservationActive = false;
    }

    private void Window_LayoutUpdated(object? sender, EventArgs e)
    {
        ProcessLayoutUpdated();
    }

    private void ProcessLayoutUpdated()
    {
        if (registeredControls.Count == 0)
            return;

        isLayoutCheckPending = true;
        UpdateRenderingSubscription();
    }

    private void ProcessPendingGeometryChecks(bool includeRemainingLayoutControls)
    {
        var scrollControls = pendingScrollGeometryControls.ToHashSet();
        pendingScrollGeometryControls.Clear();

        foreach (var control in scrollControls)
            CheckGeometryAndQueueRefresh(control);

        if (!includeRemainingLayoutControls)
            return;

        foreach (var control in registeredControls)
        {
            if (scrollControls.Contains(control))
                continue;

            CheckGeometryAndQueueRefresh(control);
        }
    }

    private void CheckGeometryAndQueueRefresh(BackdropBlurBorder control)
    {
        if (!registeredControls.Contains(control) || !control.IsRefreshEligible)
            return;

        if (!control.PrepareLayoutGeometryRefresh())
            return;

        AddDirty(control);
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        Shutdown();
    }

    private void Shutdown()
    {
        if (isClosed)
            return;

        isClosed = true;
        StopWindowObservation();
        if (isRenderingSubscribed)
        {
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            isRenderingSubscribed = false;
        }

        foreach (var source in controlsBySource.Keys)
        {
            source.SizeChanged -= Source_SizeChanged;
        }
        foreach (var scrollViewer in controlsByScrollViewer.Keys)
            scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;

        controlsBySource.Clear();
        controlsByScrollViewer.Clear();
        registrations.Clear();
        registeredControls.Clear();
        dirtyControls.Clear();
        pendingScrollGeometryControls.Clear();
        continuousScopes.Clear();
        isLayoutCheckPending = false;
        Coordinators.Remove(window);
    }

    private void AddSourceRegistration(
        FrameworkElement source,
        BackdropBlurBorder control)
    {
        if (!controlsBySource.TryGetValue(source, out var controls))
        {
            controls = [];
            controlsBySource[source] = controls;
            source.SizeChanged += Source_SizeChanged;
        }

        controls.Add(control);
    }

    private void RemoveSourceRegistration(
        FrameworkElement source,
        BackdropBlurBorder control)
    {
        if (!controlsBySource.TryGetValue(source, out var controls))
            return;

        controls.Remove(control);
        if (controls.Count > 0)
            return;

        source.SizeChanged -= Source_SizeChanged;
        controlsBySource.Remove(source);
    }

    private void AddScrollViewerRegistration(
        ScrollViewer scrollViewer,
        BackdropBlurBorder control)
    {
        if (!controlsByScrollViewer.TryGetValue(scrollViewer, out var controls))
        {
            controls = [];
            controlsByScrollViewer[scrollViewer] = controls;
            scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
        }

        controls.Add(control);
    }

    private void RemoveScrollViewerRegistration(
        ScrollViewer scrollViewer,
        BackdropBlurBorder control)
    {
        if (!controlsByScrollViewer.TryGetValue(scrollViewer, out var controls))
            return;

        controls.Remove(control);
        if (controls.Count > 0)
            return;

        scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
        controlsByScrollViewer.Remove(scrollViewer);
    }

    private void RemoveRegistration(
        BackdropBlurBorder control,
        Registration registration)
    {
        RemoveSourceRegistration(registration.Source, control);
        if (registration.ScrollViewer is not null)
            RemoveScrollViewerRegistration(registration.ScrollViewer, control);
    }

    private void Source_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement source
            || !controlsBySource.TryGetValue(source, out var controls))
        {
            return;
        }

        foreach (var control in controls)
            control.InvalidatePreparedGeometry();
        AddDirtyRange(controls);
    }

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
            ProcessScrollChanged(scrollViewer);
    }

    private void ProcessScrollChanged(ScrollViewer scrollViewer)
    {
        if (!controlsByScrollViewer.TryGetValue(scrollViewer, out var controls))
            return;

        foreach (var control in controls)
        {
            if (!registeredControls.Contains(control))
                continue;

            pendingScrollGeometryControls.Add(control);
        }

        UpdateRenderingSubscription();
    }

    private sealed class ContinuousRefreshLease(
        BackdropBlurRefreshCoordinator coordinator,
        FrameworkElement[] scopes) : IDisposable
    {
        private BackdropBlurRefreshCoordinator? owner = coordinator;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref owner, null);
            current?.ReleaseContinuousScopes(scopes);
        }
    }

    private sealed class EmptyLease : IDisposable
    {
        internal static readonly EmptyLease Instance = new();

        public void Dispose()
        {
        }
    }

    private sealed record Registration(
        FrameworkElement Source,
        ScrollViewer? ScrollViewer);
}
