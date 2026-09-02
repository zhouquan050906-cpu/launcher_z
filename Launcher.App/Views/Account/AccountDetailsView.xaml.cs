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

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Launcher.App.Controls;
using Launcher.App.Services;
using Launcher.App.ViewModels.Account;

namespace Launcher.App.Views.Account;

public partial class AccountDetailsView : UserControl
{
    public static readonly DependencyProperty IsProgressiveBlurEnabledProperty =
        DependencyProperty.Register(
            nameof(IsProgressiveBlurEnabled),
            typeof(bool),
            typeof(AccountDetailsView),
            new PropertyMetadata(false, OnProgressiveBlurEnabledChanged));

    private readonly PageTransitionService accountTransitionService;
    private readonly ProgressiveBlurBandController? progressiveBlurController;
    private INotifyPropertyChanged? currentViewModelNotifier;
    private string? currentAccountToken;

    public AccountDetailsView()
    {
        InitializeComponent();

        progressiveBlurController = new ProgressiveBlurBandController(
            new ProgressiveBlurVisualParts(
                this,
                PART_ProgressiveBlurLayer,
                PART_ProgressiveBlurVisualSource,
                PART_ProgressiveBlurDirectHost,
                PART_ProgressiveBlurViewport,
                PART_ProgressiveBlurUpscaleHost,
                PART_ProgressiveBlurUpscaleTransform,
                PART_ProgressiveBlurHorizontalHost,
                PART_ProgressiveBlurVerticalHost,
                PART_ProgressiveBlurBrush),
            () => IsVisible && IsProgressiveBlurEnabled);

        // 方向按账户在左侧列表里的上下位置来定，列表会随增删改序，因此每次过渡现取一遍。
        accountTransitionService = PageTransitionService.CreateWithDynamicOrder(
            Dispatcher,
            _ => DetailsContentRoot,
            GetCurrentAccountToken(),
            GetAccountOrder);

        Loaded += AccountDetailsView_Loaded;
        Unloaded += AccountDetailsView_Unloaded;
        DataContextChanged += AccountDetailsView_DataContextChanged;
        PART_ProgressiveBlurLayer.MouseWheel += ProgressiveBlurLayer_MouseWheel;
    }

    public bool IsProgressiveBlurEnabled
    {
        get => (bool)GetValue(IsProgressiveBlurEnabledProperty);
        set => SetValue(IsProgressiveBlurEnabledProperty, value);
    }

    internal FrameworkElement ProgressiveBlurLayerElement => PART_ProgressiveBlurLayer;

    internal FrameworkElement ProgressiveBlurVisualSourceElement => PART_ProgressiveBlurVisualSource;

    internal FrameworkElement ProgressiveBlurDirectHostElement => PART_ProgressiveBlurDirectHost;

    internal FrameworkElement ProgressiveBlurViewportElement => PART_ProgressiveBlurViewport;

    internal FrameworkElement ProgressiveBlurUpscaleHostElement => PART_ProgressiveBlurUpscaleHost;

    internal ScaleTransform ProgressiveBlurUpscaleTransform => PART_ProgressiveBlurUpscaleTransform;

    internal FrameworkElement ProgressiveBlurHorizontalHostElement => PART_ProgressiveBlurHorizontalHost;

    internal FrameworkElement ProgressiveBlurVerticalHostElement => PART_ProgressiveBlurVerticalHost;

    internal VisualBrush ProgressiveBlurBrush => PART_ProgressiveBlurBrush;

    internal ScrollViewer DetailsScrollViewerElement => AccountDetailsScrollViewer;

    public void ScrollToTop()
    {
        AccountDetailsScrollViewer.ScrollToTop();
    }

    private void AccountDetailsView_Loaded(object sender, RoutedEventArgs e)
    {
        progressiveBlurController?.OnLoaded();
        currentAccountToken = GetCurrentAccountToken();
        accountTransitionService.SyncTo(currentAccountToken);
        ResetContentPresentation();
    }

    private void AccountDetailsView_Unloaded(object sender, RoutedEventArgs e)
    {
        progressiveBlurController?.OnUnloaded();
    }

    private static void OnProgressiveBlurEnabledChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not AccountDetailsView view)
            return;

        var becameEnabled = !(bool)e.OldValue && (bool)e.NewValue;
        view.progressiveBlurController?.OnEnabledChanged(becameEnabled);
    }

    private void ProgressiveBlurLayer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled
            || !IsProgressiveBlurEnabled
            || !ReferenceEquals(e.OriginalSource, PART_ProgressiveBlurLayer))
        {
            return;
        }

        var forwardedEvent = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = AccountDetailsScrollViewer
        };
        AccountDetailsScrollViewer.RaiseEvent(forwardedEvent);
        e.Handled = forwardedEvent.Handled;
    }

    private void AccountDetailsView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged -= AccountDetailsViewModel_PropertyChanged;

        currentViewModelNotifier = e.NewValue as INotifyPropertyChanged;
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged += AccountDetailsViewModel_PropertyChanged;

        currentAccountToken = GetCurrentAccountToken();
        accountTransitionService.SyncTo(currentAccountToken);
        ResetContentPresentation();
    }

    private void AccountDetailsViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(AccountDetailsViewModel.SelectedAccount))
            return;

        var accountToken = GetCurrentAccountToken();
        if (string.IsNullOrWhiteSpace(accountToken))
        {
            currentAccountToken = null;
            accountTransitionService.SyncTo(null);
            ResetContentPresentation();
            return;
        }

        if (string.Equals(currentAccountToken, accountToken, StringComparison.Ordinal))
            return;

        currentAccountToken = accountToken;
        ScrollToTop();
        // 过渡必须和数据更新落在同一个 dispatcher 周期里。推迟到 Loaded 优先级会排在
        // 渲染之后，新账户的内容先按最终位置整幅画出来一帧，再被过渡压回几乎全透明重走一遍
        // 淡入——看起来就是"先闪一下再重新进场"。其余分区视图都是就地同步调用。
        AccountDetailsScrollViewer.UpdateLayout();
        DetailsContentRoot.UpdateLayout();
        accountTransitionService.MoveTo(accountToken);
    }

    private string? GetCurrentAccountToken()
    {
        return (DataContext as AccountDetailsViewModel)?.SelectedAccount?.Id;
    }

    private IReadOnlyList<string> GetAccountOrder()
    {
        if (DataContext is not AccountDetailsViewModel viewModel)
            return [];

        return [.. viewModel.AccountList.Accounts.Select(account => account.Id)];
    }

    private void ResetContentPresentation()
    {
        DetailsContentRoot.BeginAnimation(OpacityProperty, null);
        DetailsContentRoot.Opacity = 1;

        var transform = EnsureTranslateTransform();
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = 0;
    }

    private TranslateTransform EnsureTranslateTransform()
    {
        if (DetailsContentRoot.RenderTransform is TranslateTransform transform)
            return transform;

        transform = new TranslateTransform();
        DetailsContentRoot.RenderTransform = transform;
        return transform;
    }

}
