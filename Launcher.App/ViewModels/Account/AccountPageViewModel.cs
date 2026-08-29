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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Application.Accounts;
using Launcher.App.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Account;

public sealed partial class AccountPageViewModel : ObservableObject
{
    private readonly IAccountDialogService dialogService;

    public AccountPageViewModel(
        AccountListViewModel accountList,
        AccountDialogViewModel dialog,
        AccountAppearanceViewModel appearance,
        AccountOfflineUuidViewModel offlineUuid,
        AccountPurchaseViewModel purchase,
        IAccountDialogService dialogService)
    {
        AccountList = accountList;
        Dialog = dialog;
        Appearance = appearance;
        OfflineUuid = offlineUuid;
        Purchase = purchase;
        this.dialogService = dialogService;
        Details = new AccountDetailsViewModel(this);

        AccountList.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccountListViewModel.SelectedAccount))
                OnPropertyChanged(nameof(SelectedAccount));
        };
    }

    public AccountListViewModel AccountList { get; }

    public AccountDialogViewModel Dialog { get; }

    public AccountAppearanceViewModel Appearance { get; }

    public AccountOfflineUuidViewModel OfflineUuid { get; }

    public AccountPurchaseViewModel Purchase { get; }

    public AccountDetailsViewModel Details { get; }

    public LauncherAccount? SelectedAccount
    {
        get => AccountList.SelectedAccount;
        set
        {
            if (value is null)
                AccountList.ClearSelectedAccount();
            else
                AccountList.SelectAccount(value);
        }
    }

    public async Task InitializeAsync()
    {
        await AccountList.InitializeAsync();
        _ = Appearance.RefreshAccountsSilentlyAsync();
    }

    public Task PrimeAsync()
    {
        return AccountList.PrimeAsync();
    }

    public void SelectAccount(LauncherAccount account)
    {
        AccountList.SelectAccount(account);
    }

    [RelayCommand]
    private void RequestAddAccount()
    {
        dialogService.ShowAddAccountDialog();
    }

    [RelayCommand]
    private void RequestDeleteAccount(AccountItemViewModel item)
    {
        dialogService.ShowDeleteAccountDialog(item.Account);
    }

    [RelayCommand]
    private void RequestRenameAccount()
    {
        dialogService.ShowRenameAccountDialog();
    }

    [RelayCommand]
    private void RequestCancelAddAccountDialog()
    {
        dialogService.CancelAddAccountDialog();
    }

    [RelayCommand]
    private void RequestBackAddAccountDialog()
    {
        dialogService.BackAddAccountDialog();
    }

    [RelayCommand]
    private Task RequestConfirmAddAccountDialogAsync()
    {
        return dialogService.ConfirmAddAccountDialogAsync();
    }

    [RelayCommand]
    private void RequestSelectAllThirdPartyProfiles() => dialogService.SelectAllThirdPartyProfiles();

    [RelayCommand]
    private Task RequestRetryThirdPartyProfileImportAsync() => dialogService.RetryThirdPartyProfileImportAsync();

    [RelayCommand]
    private void RequestCancelDeleteAccountDialog()
    {
        dialogService.CancelDeleteAccountDialog();
    }

    [RelayCommand]
    private Task RequestConfirmDeleteAccountDialogAsync()
    {
        return dialogService.ConfirmDeleteAccountDialogAsync();
    }

    [RelayCommand]
    private void RequestCancelRenameAccountDialog()
    {
        dialogService.CancelRenameAccountDialog();
    }

    [RelayCommand]
    private Task RequestConfirmRenameAccountDialogAsync()
    {
        return dialogService.ConfirmRenameAccountDialogAsync();
    }
}

