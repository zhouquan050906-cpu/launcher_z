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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Launcher.App.Behaviors;
using Launcher.App.Controls;
using Launcher.App.Effects;
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

    public static readonly DependencyProperty IsProgressiveBlurEnabledProperty =
        DependencyProperty.Register(
            nameof(IsProgressiveBlurEnabled),
            typeof(bool),
            typeof(HomeLaunchGameListView),
            new PropertyMetadata(false, OnProgressiveBlurEnabledChanged));

    private const double FallbackPanelWidth = 224;
    // 折叠态下面板底边被裁掉，可见底边的圆角由裁剪几何提供，半径必须与面板一致。
    private const double MenuPanelCornerRadius = 14;
    // 侧边条上下各让开一个圆角，剩下的才是可缩放的均匀段。
    private const double MenuShadowSideInset = MenuPanelCornerRadius * 2;
    // 面板向下多出这么多，保证它自带的底边描边在任何状态下都在裁剪线之下。
    private const double MenuPanelBottomOverhang = 2;
    private const double FallbackCollapsedHeight = 72;
    private const double FallbackItemHeight = 54;
    private const double FallbackAnimationDurationMilliseconds = 380;
    private const double FallbackAnimationEasePower = 3.2;
    private const double FallbackCollapseDelayMilliseconds = 110;
    private static readonly Thickness FallbackPanelMargin = new(24, 24, 0, 24);

    private HomeLaunchGameListViewModel? attachedViewModel;
    private bool isApplyQueued;
    private bool isPointerExpanded;
    private bool pendingAnimate;
    private int animationGeneration;
    private int measureRetryCount;
    private bool? appliedExpandedState;
    private bool isProgressiveBlurActive;
    private readonly ProgressiveBlurBandController? progressiveBlurController;
    private DispatcherTimer? collapseDelayTimer;

    public HomeLaunchGameListView()
    {
        InitializeComponent();

        progressiveBlurController = new ProgressiveBlurBandController(
            new ProgressiveBlurVisualParts(
                this,
                HomeLaunchProgressiveBlurLayer,
                HomeLaunchProgressiveBlurVisualSource,
                HomeLaunchProgressiveBlurDirectHost,
                HomeLaunchProgressiveBlurViewport,
                HomeLaunchProgressiveBlurUpscaleHost,
                HomeLaunchProgressiveBlurUpscaleTransform,
                HomeLaunchProgressiveBlurHorizontalHost,
                HomeLaunchProgressiveBlurVerticalHost,
                HomeLaunchProgressiveBlurBrush),
            () => IsVisible && isProgressiveBlurActive);
        SetResourceReference(
            IsProgressiveBlurEnabledProperty,
            ProgressiveBlurResourceKeys.IsEnabled);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => QueueApplyMenuState(animate: IsLoaded);
        AttachHoverInput();
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

    public bool IsProgressiveBlurEnabled
    {
        get => (bool)GetValue(IsProgressiveBlurEnabledProperty);
        set => SetValue(IsProgressiveBlurEnabledProperty, value);
    }

    internal void SetPointerExpandedForTest(bool value)
    {
        // 测试要的是状态本身，不走收起延迟，否则断言得等定时器。
        collapseDelayTimer?.Stop();
        if (isPointerExpanded == value)
            return;

        isPointerExpanded = value;
        QueueApplyMenuState(animate: IsLoaded);
    }

    private TimeSpan GetCollapseDelay()
    {
        return TimeSpan.FromMilliseconds(Math.Max(0d, GetResourceDouble(
            "HomeLaunchMenuCollapseDelayMilliseconds",
            FallbackCollapseDelayMilliseconds)));
    }

    private void ScheduleDelayedCollapse()
    {
        collapseDelayTimer ??= new DispatcherTimer(DispatcherPriority.Input, Dispatcher);
        collapseDelayTimer.Interval = GetCollapseDelay();
        collapseDelayTimer.Tick -= CollapseDelayTimer_Tick;
        collapseDelayTimer.Tick += CollapseDelayTimer_Tick;
        collapseDelayTimer.Start();
    }

    private void CollapseDelayTimer_Tick(object? sender, EventArgs e)
    {
        collapseDelayTimer?.Stop();
        if (!isPointerExpanded)
            return;

        if (IsPointerOverMenu())
        {
            // 指针又回来了，或者停在被标题栏拖拽区盖住的顶部那一段。后者收不到新的 MouseLeave，
            // 所以这里继续观察而不是直接作废；指针真正离开菜单范围后，下一拍就会收起。
            collapseDelayTimer?.Start();
            return;
        }

        isPointerExpanded = false;
        QueueApplyMenuState(animate: IsLoaded);
    }

    /// <summary>
    /// 除了命中测试，再按几何范围复核一次。展开态菜单的顶边在内容区 y=24，而窗口标题栏的
    /// 拖拽区覆盖 y=0~48 且渲染在页面之上，两者重叠 24px：指针停在那一段时命中的是拖拽区，
    /// WPF 会发出 MouseLeave，但视觉上指针仍在菜单里。只靠 IsMouseOver 会导致还没离开就收起。
    /// </summary>
    private bool IsPointerOverMenu()
    {
        if (HomeLaunchMenuPanelShadow.IsMouseOver)
            return true;

        // 指针已经离开窗口时不再做几何判断：那种情况下取到的位置是最后一次的残留值，
        // 会把菜单永久留在展开态。
        if (!IsLoaded
            || HomeLaunchMenuClipHost.ActualWidth <= 0
            || Window.GetWindow(this)?.IsMouseOver != true)
        {
            return false;
        }

        var position = Mouse.GetPosition(HomeLaunchMenuClipHost);
        // 裁剪宿主固定在展开尺寸上，可见区域的顶边由当前平移量决定。
        return position.X >= 0
            && position.X <= HomeLaunchMenuClipHost.ActualWidth
            && position.Y >= HomeLaunchMenuPanelTranslate.Y
            && position.Y <= HomeLaunchMenuClipHost.ActualHeight;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 进入视觉树后容器和资源尺寸才可用，此时附加集合监听并同步无动画初始状态。
        AttachViewModel(DataContext as HomeLaunchGameListViewModel);
        HomeLaunchMenuClipHost.Width = GetResourceDouble("HomeLaunchMenuPanelWidth", FallbackPanelWidth);
        HomeLaunchMenuShadowHost.Width = HomeLaunchMenuClipHost.Width;
        HomeLaunchMenuShadowHost.Margin = GetPanelMargin();
        HomeLaunchMenuClipHost.Height = GetCollapsedHeight();
        HomeLaunchMenuClipHost.Margin = GetPanelMargin();
        progressiveBlurController?.OnLoaded();
        QueueApplyMenuState(animate: false, DispatcherPriority.Loaded);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        isProgressiveBlurActive = false;
        VerticalEdgeOpacityMask.SetIsEnabled(HomeLaunchProgressiveBlurLayer, false);
        progressiveBlurController?.OnUnloaded();
        DetachViewModel(attachedViewModel);
        collapseDelayTimer?.Stop();
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

    /// <summary>
    /// 指针离开后不立即收起，既避免短暂划出边界时误收，也让动画提交避开当前 MouseLeave 输入阶段。
    /// </summary>
    private void SetPointerExpanded(bool expanded)
    {
        collapseDelayTimer?.Stop();
        if (isPointerExpanded == expanded)
            return;

        if (!expanded && IsLoaded && GetCollapseDelay() > TimeSpan.Zero)
        {
            ScheduleDelayedCollapse();
            return;
        }

        isPointerExpanded = expanded;
        QueueApplyMenuState(animate: IsLoaded);
    }

    private void AttachHoverInput()
    {
        HomeLaunchMenuPanelShadow.MouseEnter += HoverInputElement_OnMouseEnter;
        HomeLaunchMenuPanelShadow.MouseLeave += HoverInputElement_OnMouseLeave;
    }

    private void HoverInputElement_OnMouseEnter(object sender, MouseEventArgs e) =>
        SetPointerExpanded(true);

    private void HoverInputElement_OnMouseLeave(object sender, MouseEventArgs e) =>
        SetPointerExpanded(false);

    /// <summary>
    /// 默认用 <see cref="DispatcherPriority.Loaded"/> 而不是 Background：Background 低于 Input，
    /// 指针还在移动时输入消息不断，这个任务会被饿上几十毫秒，动画迟迟不开始。
    /// Loaded 高于 Input 且排在本轮布局之后，既不会被饿死，读到的 ActualHeight 也仍然是最新的。
    /// </summary>
    private void QueueApplyMenuState(bool animate, DispatcherPriority priority = DispatcherPriority.Loaded)
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
        UpdateMenuClipHost(expandedHeight);

        var shouldExpand = ShouldUseExpandedState();
        var selectedItemWasVisible = false;
        SuppressSelectedItemBackground = !shouldExpand;
        HomeLaunchHeaderOverlay.IsHitTestVisible = shouldExpand;
        // 展开动画期间先不挂渐进模糊：透明度遮罩和两趟着色器会显著增加每帧重绘成本，
        // 而面板停稳之前这条模糊带本来也看不清。收起时立即卸掉，与原先一致。
        var deferProgressiveBlur = shouldExpand && animate;
        UpdateProgressiveBlurState(shouldExpand && !deferProgressiveBlur);

        var requiresMeasurement = !shouldExpand && attachedViewModel?.SelectedLaunchInstanceItem is not null;
        var isMeasured = false;
        if (requiresMeasurement)
            isMeasured = PrepareSelectedItemForMeasurement(out selectedItemWasVisible);

        if (requiresMeasurement && !isMeasured)
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
        // 面板高度固定，收起就是把它整体下移，露出的部分由裁剪宿主决定。
        var targetPanelOffset = Math.Max(0d, expandedHeight - targetHeight);
        var targetTranslate = shouldExpand ? 0 : CalculateCollapsedListTranslate();
        var targetEmptyStateTranslate = CalculateEmptyStateTranslate(shouldExpand, expandedHeight);
        var targetHeaderOpacity = shouldExpand ? 1 : 0;

        var isPanelAnimated = AnimateDouble(
            HomeLaunchMenuPanelTranslate,
            TranslateTransform.YProperty,
            targetPanelOffset,
            animate,
            generation,
            OnMenuHeightAnimationCompleted);
        AnimateDouble(HomeLaunchListTranslate, TranslateTransform.YProperty, targetTranslate, animate, generation);
        AnimateDouble(HomeLaunchEmptyStateTranslate, TranslateTransform.YProperty, targetEmptyStateTranslate, animate, generation);
        AnimateDouble(HomeLaunchHeaderOverlay, OpacityProperty, targetHeaderOpacity, animate, generation);
        // 投影三段与面板用同一组动画参数、在同一拍提交，因此逐帧严格对齐。
        // 顶端帽走与面板相同的平移；侧边条的缩放是可见高度的线性函数，同样的缓动作用在两端点上，
        // 中间每一帧的取值都彼此一致。
        AnimateDouble(HomeLaunchMenuShadowTopTranslate, TranslateTransform.YProperty, targetPanelOffset, animate, generation);
        var targetSideScale = CalculateShadowSideScale(targetHeight, expandedHeight);
        AnimateDouble(HomeLaunchMenuShadowLeftScale, ScaleTransform.ScaleYProperty, targetSideScale, animate, generation);
        AnimateDouble(HomeLaunchMenuShadowRightScale, ScaleTransform.ScaleYProperty, targetSideScale, animate, generation);

        // 高度没有真正动起来（目标一致或本次不做动画）时不会有 Completed，就地收尾。
        if (!isPanelAnimated)
            OnMenuHeightAnimationCompleted();
    }

    /// <summary>
    /// 侧边条在布局上占满两端圆角之间的高度，锚在底部缩放。缩放比就是可见高度扣掉两端圆角
    /// 之后与展开态的比值 —— 它是可见高度的线性函数，才能和面板平移共用同一条缓动而不脱节。
    /// </summary>
    private static double CalculateShadowSideScale(double targetHeight, double expandedHeight)
    {
        var expandedSpan = expandedHeight - MenuShadowSideInset;
        if (expandedSpan <= 0d)
            return 0d;

        return Math.Clamp((targetHeight - MenuShadowSideInset) / expandedSpan, 0d, 1d);
    }

    private void UpdateMenuClipHost(double expandedHeight)
    {
        var width = GetResourceDouble("HomeLaunchMenuPanelWidth", FallbackPanelWidth);
        if (HomeLaunchMenuClipHost.Clip is not null
            && Math.Abs(HomeLaunchMenuClipHost.Width - width) < 0.1
            && Math.Abs(HomeLaunchMenuClipHost.Height - expandedHeight) < 0.1)
        {
            return;
        }

        HomeLaunchMenuClipHost.Width = width;
        HomeLaunchMenuClipHost.Height = expandedHeight;
        HomeLaunchMenuClipHost.Margin = GetPanelMargin();
        // 投影宿主与裁剪宿主同尺寸同位置，但不裁剪，让九宫格向外溢出。
        HomeLaunchMenuShadowHost.Width = width;
        HomeLaunchMenuShadowHost.Height = expandedHeight;
        HomeLaunchMenuShadowHost.Margin = GetPanelMargin();
        // 面板比裁剪宿主高一点，自带的底边描边因此永远落在裁剪线之下，由静态端帽统一绘制。
        HomeLaunchMenuPanelShadow.Height = expandedHeight + MenuPanelBottomOverhang;
        HomeLaunchMenuBottomEdge.Width = width;
        HomeLaunchMenuBottomEdge.Margin = GetPanelMargin();
        var clip = new RectangleGeometry(
            new Rect(0, 0, width, expandedHeight),
            MenuPanelCornerRadius,
            MenuPanelCornerRadius);
        // 几何在动画期间不再变化，冻结后渲染线程可以直接复用。
        clip.Freeze();
        HomeLaunchMenuClipHost.Clip = clip;
    }

    /// <summary>
    /// 面板停稳之后再补上被推迟的渐进模糊，它的挂载开销就落在没有动画的那一帧上。
    /// </summary>
    private void OnMenuHeightAnimationCompleted()
    {
        UpdateProgressiveBlurState(ShouldUseExpandedState());
    }

    private static void OnProgressiveBlurEnabledChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not HomeLaunchGameListView view)
            return;

        var becameEnabled = !(bool)e.OldValue && (bool)e.NewValue;
        view.UpdateProgressiveBlurState(view.ShouldUseExpandedState());
        if (becameEnabled)
            view.progressiveBlurController?.OnEnabledChanged(true);
    }

    private void UpdateProgressiveBlurState(bool shouldExpand)
    {
        isProgressiveBlurActive = IsProgressiveBlurEnabled
            && shouldExpand
            && attachedViewModel?.HasLaunchInstances == true;
        VerticalEdgeOpacityMask.SetIsEnabled(
            HomeLaunchProgressiveBlurLayer,
            isProgressiveBlurActive);
        progressiveBlurController?.Update();
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

    private bool AnimateDouble(
        DependencyObject target,
        DependencyProperty property,
        double to,
        bool animate,
        int generation,
        Action? onCompleted = null)
    {
        // 开始新动画前以当前呈现值为起点，快速进出菜单时不会跳回上次目标值。
        if (target is not IAnimatable animatable)
        {
            target.SetValue(property, to);
            return false;
        }

        var from = GetCurrentDouble(target, property);
        animatable.BeginAnimation(property, null);
        target.SetValue(property, from);

        if (!animate || Math.Abs(from - to) < 0.1)
        {
            target.SetValue(property, to);
            return false;
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
            onCompleted?.Invoke();
        };

        animatable.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        return true;
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
