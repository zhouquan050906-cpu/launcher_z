/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;
using Launcher.App.ViewModels.Account;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;
using Launcher.Tests.Fakes;

namespace Launcher.Tests.ViewModels.Account;

public sealed class AccountOfflineUuidViewModelTests
{
    [Theory]
    [InlineData(OfflineUuidGenerationMode.Standard)]
    [InlineData(OfflineUuidGenerationMode.Manual)]
    public async Task SelectingThenCancellingDoesNotChangeOrSaveTheAccount(OfflineUuidGenerationMode target)
    {
        var originalMode = target == OfflineUuidGenerationMode.Standard
            ? OfflineUuidGenerationMode.Manual : OfflineUuidGenerationMode.Standard;
        var (viewModel, accounts, store) = await CreateAsync(originalMode);
        var original = accounts.SelectedAccount;
        Assert.False(viewModel.IsOfflineUuidModeChangeDialogOpen);

        viewModel.SelectedOfflineUuidOption = viewModel.OfflineUuidOptions.Single(option => option.Mode == target);

        Assert.True(viewModel.IsOfflineUuidModeChangeDialogOpen);
        Assert.Same(original, accounts.SelectedAccount);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(originalMode == OfflineUuidGenerationMode.Manual, viewModel.HasManualUuidEditor);
        Assert.False(viewModel.CanApplyManualUuid);

        viewModel.CancelOfflineUuidModeChangeCommand.Execute(null);
        await viewModel.ConfirmOfflineUuidModeChangeCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsOfflineUuidModeChangeDialogOpen);
        Assert.Equal(originalMode, viewModel.SelectedOfflineUuidOption?.Mode);
        Assert.Same(original, accounts.SelectedAccount);
        Assert.Equal(0, store.SaveCount);
    }

    [Theory]
    [InlineData(OfflineUuidGenerationMode.Manual, OfflineUuidGenerationMode.Standard)]
    [InlineData(OfflineUuidGenerationMode.Random, OfflineUuidGenerationMode.Standard)]
    public async Task ConfirmationAppliesAndPersistsTheRequestedMode(
        OfflineUuidGenerationMode originalMode, OfflineUuidGenerationMode target)
    {
        var (viewModel, accounts, store) = await CreateAsync(originalMode);
        viewModel.SelectedOfflineUuidOption = viewModel.OfflineUuidOptions.Single(option => option.Mode == target);

        await viewModel.ConfirmOfflineUuidModeChangeCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsOfflineUuidModeChangeDialogOpen);
        Assert.Equal(target, accounts.SelectedAccount!.OfflineUuidGenerationMode);
        Assert.Equal($"{target}-Player", accounts.SelectedAccount.Uuid);
        Assert.Same(accounts.SelectedAccount, Assert.Single(store.SavedAccounts));
        Assert.Equal(target, viewModel.SelectedOfflineUuidOption?.Mode);
    }

    [Fact]
    public async Task ManualModeNeedsConfirmationBeforeEditingAndApplicationBeforeSaving()
    {
        var (viewModel, accounts, store) = await CreateAsync();
        var original = accounts.SelectedAccount;
        viewModel.SelectedOfflineUuidOption = viewModel.OfflineUuidOptions.Single(option => option.Mode == OfflineUuidGenerationMode.Manual);
        await viewModel.ApplyManualUuidCommand.ExecuteAsync(null);
        Assert.Same(original, accounts.SelectedAccount);
        Assert.Equal(0, store.SaveCount);

        await viewModel.ConfirmOfflineUuidModeChangeCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasManualUuidEditor);
        Assert.Same(original, accounts.SelectedAccount);
        Assert.Equal(0, store.SaveCount);

        viewModel.ManualUuidText = "01234567-89ab-cdef-0123-456789abcdef";
        await viewModel.ApplyManualUuidCommand.ExecuteAsync(null);
        Assert.Equal(OfflineUuidGenerationMode.Manual, accounts.SelectedAccount!.OfflineUuidGenerationMode);
        Assert.Equal(viewModel.ManualUuidText, accounts.SelectedAccount.Uuid);
        Assert.Same(accounts.SelectedAccount, Assert.Single(store.SavedAccounts));
    }

    [Fact]
    public async Task ChangingAccountsInvalidatesThePendingConfirmation()
    {
        var (viewModel, accounts, store) = await CreateAsync();
        var original = accounts.SelectedAccount;
        viewModel.SelectedOfflineUuidOption = viewModel.OfflineUuidOptions.Single(option => option.Mode == OfflineUuidGenerationMode.Manual);
        var other = new LauncherAccount { Id = "other", DisplayName = "Other", Uuid = "other-uuid" };
        accounts.Accounts.Add(new AccountItemViewModel(other));
        accounts.SelectAccount(other, persistSelection: false);

        await viewModel.ConfirmOfflineUuidModeChangeCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsOfflineUuidModeChangeDialogOpen);
        Assert.Same(other, accounts.SelectedAccount);
        Assert.Same(original, accounts.FindAccount(original!.Id));
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task FailedSaveRestoresTheOriginalAccountAndSelection()
    {
        var (viewModel, accounts, store) = await CreateAsync(OfflineUuidGenerationMode.Manual);
        var original = accounts.SelectedAccount;
        store.FailSave = true;
        viewModel.SelectedOfflineUuidOption = viewModel.OfflineUuidOptions.Single(option => option.Mode == OfflineUuidGenerationMode.Standard);

        await viewModel.ConfirmOfflineUuidModeChangeCommand.ExecuteAsync(null);

        Assert.Same(original, accounts.SelectedAccount);
        Assert.Equal(original!.OfflineUuidGenerationMode, viewModel.SelectedOfflineUuidOption?.Mode);
        Assert.False(viewModel.IsOfflineUuidModeChangeDialogOpen);
        Assert.True(viewModel.CanChangeOfflineUuidMode);
    }

    [Fact]
    public async Task LegacyRandomAccountsUseCustomOptionWithoutChangingTheirUuid()
    {
        var (viewModel, accounts, store) = await CreateAsync(OfflineUuidGenerationMode.Random);

        Assert.Equal(new[] { OfflineUuidGenerationMode.Standard, OfflineUuidGenerationMode.Manual },
            viewModel.OfflineUuidOptions.Select(option => option.Mode));
        Assert.Equal(OfflineUuidGenerationMode.Manual, viewModel.SelectedOfflineUuidOption?.Mode);
        Assert.True(viewModel.HasManualUuidEditor);
        Assert.False(viewModel.IsOfflineUuidModeChangeDialogOpen);
        Assert.Equal("11111111-1111-1111-1111-111111111111", accounts.SelectedAccount!.Uuid);
        Assert.Equal(accounts.SelectedAccount.Uuid, viewModel.ManualUuidText);
        Assert.Equal(0, store.SaveCount);
    }

    private static async Task<(AccountOfflineUuidViewModel, AccountListViewModel, RecordingAccountStore)> CreateAsync(
        OfflineUuidGenerationMode mode = OfflineUuidGenerationMode.Standard)
    {
        var account = new LauncherAccount
        {
            Id = "offline", DisplayName = "Player",
            Uuid = "11111111-1111-1111-1111-111111111111", OfflineUuidGenerationMode = mode
        };
        var store = new RecordingAccountStore(account);
        var accounts = new AccountListViewModel(store);
        await accounts.InitializeAsync();
        return (new AccountOfflineUuidViewModel(accounts, new FakeOfflineAccountUuidService(), new StatusService(), new NullClipboard()), accounts, store);
    }

    private sealed class RecordingAccountStore(LauncherAccount account) : IAccountStore
    {
        public int SaveCount { get; private set; }
        public bool FailSave { get; set; }
        public LauncherAccount[] SavedAccounts { get; private set; } = [];
        public Task<AccountStoreSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountStoreSnapshot([account], account.Id));
        public Task<AccountStoreSnapshot> LoadCachedAsync(CancellationToken cancellationToken = default) => LoadAsync(cancellationToken);
        public Task SaveOrderAsync(string? selectedAccountId, IEnumerable<LauncherAccount> accounts, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (FailSave)
                return Task.FromException(new IOException("Simulated save failure."));
            SavedAccounts = accounts.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class NullClipboard : IClipboardService
    {
        public Task<bool> CopyTextAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }
}
