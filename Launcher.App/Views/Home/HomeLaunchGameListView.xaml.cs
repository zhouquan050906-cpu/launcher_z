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

using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Launcher.App.Behaviors;
using Launcher.App.Utilities;
using Launcher.App.ViewModels.Home;

namespace Launcher.App.Views.Home;

/// <summary>
/// 根据固定状态、指针位置和实例数量协调首页启动菜单的折叠测量与过渡动画。
/// </summary>
public partial class HomeLaunchGameListView : UserControl
{
    // 这是纯 UI 协调器；实例选择和固定偏好仍由绑定的 ViewModel 管理。
    public static readonly DependencyProperty SuppressSelectedItemBackgroundProperty =
        DependencyProperty.Register(
            nameof(SuppressSelectedItemBackground),
            typeof(bool),
            typeof(HomeLaunchGameListView),
            new PropertyMetadata(false));

    private const double FallbackPanelWidth = 224;
    private const double FallbackCollapsedHeight = 72;
    private const double FallbackItemHeight = 54;
    private const double FallbackAnimationDurationMilliseconds = 320;
    private const double FallbackAnimationEasePower = 2.4;
    private static readonly Thickness FallbackPanelMargin = new(24, 24, 0, 24);

    private HomeLaunchGameListViewModel? attachedViewModel;
    private bool isApplyQueued;
    private bool isPointerExpanded;
    private bool pendingAnimate;
    private int animationGeneration;
    private int measureRetryCount;
    private bool? appliedExpandedState;

    public HomeLaunchGameListView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => QueueApplyMenuState(animate: IsLoaded);
        HomeLaunchMenuPanelShadow.MouseEnter += (_, _) => SetPointerExpanded(true);
        HomeLaunchMenuPanelShadow.MouseLeave += (_, _) => SetPointerExpanded(false);
    }

    internal FrameworkElement FloatingLayerElement => HomeLaunchFloatingLayer;

    internal FrameworkElement MenuPanelShadowElement => HomeLaunchMenuPanelShadow;

    internal FrameworkElement HeaderOverlayElement => HomeLaunchHeaderOverlay;

    internal FrameworkElement EmptyStateTextElement => HomeLaunchEmptyStateText;

    internal ToggleButton PinButtonElement => HomeLaunchMenuPinButton;

    internal FrameworkElement MenuViewportElement => HomeLaunchMenuViewport;

    internal ListBox LaunchInstanceListBox => HomeLaunchInstanceListBox;

    internal TranslateTransform ListTranslateTransform => HomeLaunchListTranslate;

    internal TranslateTransform EmptyStateTranslateTransform => HomeLaunchEmptyStateTranslate;

    internal bool IsMenuExpanded => ShouldUseExpandedState();

    internal bool IsSelectedItemBackgroundSuppressed => SuppressSelectedItemBackground;

    internal double CollapsedMenuHeight => GetResourceDouble("HomeLaunchMenuCollapsedHeight", FallbackCollapsedHeight);

    public bool SuppressSelectedItemBackground
    {
        get => (bool)GetValue(SuppressSelectedItemBackgroundProperty);
        set => SetValue(SuppressSelectedItemBackgroundProperty, value);
    }

    internal void SetPointerExpandedForTest(bool value)
    {
        SetPointerExpanded(value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 进入视觉树后容器和资源尺寸才可用，此时附加集合监听并同步无动画初始状态。
        AttachViewModel(DataContext as HomeLaunchGameListViewModel);
        HomeLaunchMenuPanelShadow.Width = GetResourceDouble("HomeLaunchMenuPanelWidth", FallbackPanelWidth);
        HomeLaunchMenuPanelShadow.Height = GetCollapsedHeight();
        HomeLaunchMenuPanelShadow.Margin = GetPanelMargin();
        QueueApplyMenuState(animate: false, DispatcherPriority.Loaded);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachViewModel(attachedViewModel);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel(e.OldValue as HomeLaunchGameListViewModel);
        AttachViewModel(e.NewValue as HomeLaunchGameListViewModel);
        QueueApplyMenuState(animate: IsLoaded);
    }

    private void AttachViewModel(HomeLaunchGameListViewModel? viewModel)
    {
        // DataContext 可在窗口复用时变化，属性与集合事件必须一起成对订阅。
        if (viewModel is null || ReferenceEquals(attachedViewModel, viewModel))
            return;

        attachedViewModel = viewModel;
        viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        viewModel.LaunchInstances.CollectionChanged += LaunchInstances_OnCollectionChanged;
    }

    private void DetachViewModel(HomeLaunchGameListViewModel? viewModel)
    {
        if (viewModel is null || !ReferenceEquals(attachedViewModel, viewModel))
            return;

        viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        viewModel.LaunchInstances.CollectionChanged -= LaunchInstances_OnCollectionChanged;
        attachedViewModel = null;
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HomeLaunchGameListViewModel.SelectedLaunchInstanceItem)
            or nameof(HomeLaunchGameListViewModel.HasSelectedLaunchInstance)
            or nameof(HomeLaunchGameListViewModel.HasLaunchInstances)
            or nameof(HomeLaunchGameListViewModel.HasNoLaunchInstances)
            or nameof(HomeLaunchGameListViewModel.IsLaunchMenuPinned))
        {
            measureRetryCount = 0;
            QueueApplyMenuState(animate: IsLoaded);
        }
    }

    private void LaunchInstances_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        measureRetryCount = 0;
        QueueApplyMenuState(animate: IsLoaded);
    }

    private void SetPointerExpanded(bool expanded)
    {
        if (isPointerExpanded == expanded)
            return;

        isPointerExpanded = expanded;
        QueueApplyMenuState(animate: IsLoaded);
    }

    private void QueueApplyMenuState(bool animate, DispatcherPriority priority = DispatcherPriority.Background)
    {
        // 多个属性和集合事件常在同一轮触发，合并成一次布局读取，避免重复 Measure。
        pendingAnimate |= animate;
        if (isApplyQueued || !Dispatcher.CheckAccess())
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => QueueApplyMenuState(animate, priority), priority);
            }

            return;
        }

        isApplyQueued = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                isApplyQueued = false;
                var animateNow = pendingAnimate;
                pendingAnimate = false;
                ApplyMenuState(animateNow);
            },
            priority);
    }

    private void ApplyMenuState(bool animate)
    {
        // 先计算目标高度和列表偏移，再同时启动动画，确保选中项在折叠态仍保持可见。
        var expandedHeight = GetExpandedHeight();
        HomeLaunchMenuViewport.Height = expandedHeight;

        var shouldExpand = ShouldUseExpandedState();
        var selectedItemWasVisible = false;
        SuppressSelectedItemBackground = !shouldExpand;
        HomeLaunchHeaderOverlay.IsHitTestVisible = shouldExpand;
        if (!shouldExpand
            && attachedViewModel?.SelectedLaunchInstanceItem is not null
            && !PrepareSelectedItemForMeasurement(out selectedItemWasVisible))
        {
            if (measureRetryCount++ < 4)
            {
                QueueApplyMenuState(animate, DispatcherPriority.ApplicationIdle);
                return;
            }
        }
        else
        {
            measureRetryCount = 0;
        }

        if (!shouldExpand
            && animate
            && appliedExpandedState == true
            && !selectedItemWasVisible)
        {
            NormalizeSelectedItemCollapseStart();
        }

        appliedExpandedState = shouldExpand;
        var generation = ++animationGeneration;
        var targetHeight = shouldExpand ? expandedHeight : GetCollapsedHeight();
        var targetTranslate = shouldExpand ? 0 : CalculateCollapsedListTranslate();
        var targetEmptyStateTranslate = CalculateEmptyStateTranslate(shouldExpand, expandedHeight);
        var targetHeaderOpacity = shouldExpand ? 1 : 0;

        AnimateDouble(HomeLaunchMenuPanelShadow, HeightProperty, targetHeight, animate, generation);
        AnimateDouble(HomeLaunchListTranslate, TranslateTransform.YProperty, targetTranslate, animate, generation);
        AnimateDouble(HomeLaunchEmptyStateTranslate, TranslateTransform.YProperty, targetEmptyStateTranslate, animate, generation);
        AnimateDouble(HomeLaunchHeaderOverlay, OpacityProperty, targetHeaderOpacity, animate, generation);
    }

    private bool ShouldUseExpandedState()
    {
        if (!CanUseCollapsedState())
            return true;

        if (attachedViewModel?.IsLaunchMenuPinned == true)
            return true;

        return isPointerExpanded && attachedViewModel?.HasLaunchInstances == true;
    }

    private bool CanUseCollapsedState()
    {
        return attachedViewModel?.SelectedLaunchInstanceItem is not null
            || attachedViewModel?.HasNoLaunchInstances == true;
    }

    private bool PrepareSelectedItemForMeasurement(out bool wasVisible)
    {
        wasVisible = false;
        // 虚拟化容器可能尚未生成，通过 UpdateLayout 请求当前选中项容器后再读取位置。
        var selectedItem = attachedViewModel?.SelectedLaunchInstanceItem;
        if (selectedItem is null)
            return false;

        HomeLaunchInstanceListBox.ApplyTemplate();
        HomeLaunchInstanceListBox.UpdateLayout();
        SmoothScrollBehavior.CancelAnimationFromDescendant(HomeLaunchInstanceListBox);
        wasVisible = IsWithinScrollViewport(GetSelectedItemContainer(selectedItem));
        if (!wasVisible)
        {
            HomeLaunchInstanceListBox.ScrollIntoView(selectedItem);
            HomeLaunchInstanceListBox.UpdateLayout();
        }

        return GetSelectedItemContainer(selectedItem) is { ActualHeight: > 0 };
    }

    private bool IsWithinScrollViewport(FrameworkElement? container)
    {
        if (container is null
            || container.ActualHeight <= 0
            || VisualTreeSearch.FindDescendant<ScrollViewer>(
                HomeLaunchInstanceListBox,
                _ => true) is not { ActualHeight: > 0 } scrollViewer)
        {
            return false;
        }

        try
        {
            var top = container
                .TransformToAncestor(scrollViewer)
                .Transform(new Point(0, 0))
                .Y;
            var bottom = top + container.ActualHeight;
            return bottom > 0 && top < scrollViewer.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void NormalizeSelectedItemCollapseStart()
    {
        var selectedItem = attachedViewModel?.SelectedLaunchInstanceItem;
        var container = selectedItem is null ? null : GetSelectedItemContainer(selectedItem);
        if (container is null)
            return;

        try
        {
            var currentTop = container
                .TransformToAncestor(HomeLaunchMenuPanel)
                .Transform(new Point(0, 0))
                .Y;
            var configuredHeaderHeight = HomeLaunchHeaderOverlay.Height;
            var headerHeight = HomeLaunchHeaderOverlay.ActualHeight > 0
                ? HomeLaunchHeaderOverlay.ActualHeight
                : double.IsNaN(configuredHeaderHeight)
                    ? 0
                    : Math.Max(0, configuredHeaderHeight);
            var anchorTop = HomeLaunchMenuPanel.BorderThickness.Top + headerHeight;
            var currentTranslate = HomeLaunchListTranslate.Y;
            var normalizedTranslate = currentTranslate + anchorTop - currentTop;

            HomeLaunchListTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            HomeLaunchListTranslate.Y = normalizedTranslate;
        }
        catch (InvalidOperationException)
        {
        }
    }

    private double CalculateCollapsedListTranslate()
    {
        var selectedItem = attachedViewModel?.SelectedLaunchInstanceItem;
        var container = selectedItem is null ? null : GetSelectedItemContainer(selectedItem);
        if (container is null)
            return 0;

        try
        {
            var currentTop = container
                .TransformToAncestor(HomeLaunchMenuPanel)
                .Transform(new Point(0, 0))
                .Y;
            var baseTop = currentTop - HomeLaunchListTranslate.Y;
            var itemHeight = container.ActualHeight > 0 ? container.ActualHeight : GetItemHeight();
            var slotTop = Math.Max(0, (GetCollapsedHeight() - itemHeight) / 2);
            return slotTop - baseTop;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private double CalculateEmptyStateTranslate(bool shouldExpand, double expandedHeight)
    {
        var textHeight = GetEmptyStateTextHeight();
        var targetHeight = shouldExpand ? expandedHeight : GetCollapsedHeight();
        return Math.Max(0, (targetHeight - textHeight) / 2);
    }

    private double GetEmptyStateTextHeight()
    {
        if (HomeLaunchEmptyStateText.ActualHeight > 0)
            return HomeLaunchEmptyStateText.ActualHeight;

        var availableWidth = Math.Max(
            0,
            GetResourceDouble("HomeLaunchMenuPanelWidth", FallbackPanelWidth)
            - HomeLaunchEmptyStateText.Margin.Left
            - HomeLaunchEmptyStateText.Margin.Right);
        HomeLaunchEmptyStateText.Measure(new Size(availableWidth, double.PositiveInfinity));
        return HomeLaunchEmptyStateText.DesiredSize.Height;
    }

    private FrameworkElement? GetSelectedItemContainer(HomeLaunchInstanceItem selectedItem)
    {
        return HomeLaunchInstanceListBox.ItemContainerGenerator.ContainerFromItem(selectedItem) as FrameworkElement;
    }

    private void AnimateDouble(
        DependencyObject target,
        DependencyProperty property,
        double to,
        bool animate,
        int generation)
    {
        // 开始新动画前以当前呈现值为起点，快速进出菜单时不会跳回上次目标值。
        if (target is not IAnimatable animatable)
        {
            target.SetValue(property, to);
            return;
        }

        var from = GetCurrentDouble(target, property);
        animatable.BeginAnimation(property, null);
        target.SetValue(property, from);

        if (!animate || Math.Abs(from - to) < 0.1)
        {
            target.SetValue(property, to);
            return;
        }

        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = GetAnimationDuration(),
            FillBehavior = FillBehavior.Stop,
            EasingFunction = CreateAnimationEasing()
        };
        animation.Completed += (_, _) =>
        {
            if (generation != animationGeneration)
                return;

            animatable.BeginAnimation(property, null);
            target.SetValue(property, to);
        };

        animatable.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static double GetCurrentDouble(DependencyObject target, DependencyProperty property)
    {
        var value = (double)target.GetValue(property);
        if (!double.IsNaN(value))
            return value;

        return target is FrameworkElement element ? element.ActualHeight : 0;
    }

    private double GetExpandedHeight()
    {
        var margin = GetPanelMargin();
        var layerHeight = HomeLaunchFloatingLayer.ActualHeight > 0
            ? HomeLaunchFloatingLayer.ActualHeight
            : ActualHeight;
        var expandedHeight = layerHeight - margin.Top - margin.Bottom;
        return Math.Max(GetCollapsedHeight(), expandedHeight);
    }

    private double GetCollapsedHeight()
    {
        return GetResourceDouble("HomeLaunchMenuCollapsedHeight", FallbackCollapsedHeight);
    }

    private double GetItemHeight()
    {
        return GetResourceDouble("HomeLaunchMenuItemHeight", FallbackItemHeight);
    }

    private Duration GetAnimationDuration()
    {
        return new Duration(TimeSpan.FromMilliseconds(GetResourceDouble(
            "HomeLaunchMenuAnimationDurationMilliseconds",
            FallbackAnimationDurationMilliseconds)));
    }

    private IEasingFunction CreateAnimationEasing()
    {
        return new PowerEase
        {
            Power = GetResourceDouble("HomeLaunchMenuAnimationEasePower", FallbackAnimationEasePower),
            EasingMode = EasingMode.EaseOut
        };
    }

    private Thickness GetPanelMargin()
    {
        return TryFindResource("HomeLaunchMenuPanelMargin") is Thickness margin
            ? margin
            : FallbackPanelMargin;
    }

    private double GetResourceDouble(string key, double fallback)
    {
        return TryFindResource(key) is double value ? value : fallback;
    }
}
