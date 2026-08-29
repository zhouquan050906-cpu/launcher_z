/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;
using Launcher.App.ViewModels.Account;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.Account;

public sealed class MicrosoftAccountOperationRetryHandlerTests
{
    [Fact]
    public async Task ExpiredSessionReauthenticatesAndRetriesOnceWithRefreshedAccount()
    {
        var original = CreateAccount("Original");
        var refreshed = CreateAccount("Refreshed");
        var accountList = await CreateAccountListAsync(original);
        var dialog = new FakeReauthenticationDialog(account =>
        {
            accountList.ReplaceSelectedAccount(account, refreshed);
            return Task.FromResult(true);
        });
        var handler = new MicrosoftAccountOperationRetryHandler(accountList, dialog);
        var attempts = new List<LauncherAccount>();

        var result = await handler.ExecuteAsync(
            original,
            account =>
            {
                attempts.Add(account);
                if (attempts.Count == 1)
                {
                    throw new MicrosoftAccountSessionExpiredException(
                        "Microsoft account session expired.");
                }

                return Task.FromResult(account.DisplayName);
            });

        Assert.Equal(1, dialog.CallCount);
        Assert.Equal([original, refreshed], attempts);
        Assert.Same(refreshed, result.Account);
        Assert.Equal("Refreshed", result.Value);
    }

    [Fact]
    public async Task CanceledReauthenticationDoesNotRetryOperation()
    {
        var original = CreateAccount("Original");
        var accountList = await CreateAccountListAsync(original);
        var dialog = new FakeReauthenticationDialog(_ => Task.FromResult(false));
        var handler = new MicrosoftAccountOperationRetryHandler(accountList, dialog);
        var attemptCount = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handler.ExecuteAsync(
                original,
                _ =>
                {
                    attemptCount++;
                    throw new MicrosoftAccountSessionExpiredException(
                        "Microsoft account session expired.");
                }));

        Assert.Equal(1, dialog.CallCount);
        Assert.Equal(1, attemptCount);
    }

    [Fact]
    public void ReplacingSameLogicalAccountKeepsCurrentAppearanceOperation()
    {
        var original = CreateAccount("Original");
        var refreshed = CreateAccount("Refreshed");
        using var operations = new AccountAppearanceOperationCoordinator();
        operations.SetAccount(original);
        var operation = operations.Begin(original);

        operations.SetAccount(refreshed);

        Assert.True(operations.IsCurrent(refreshed, operation));
        Assert.True(operations.IsBusy);
    }

    private static LauncherAccount CreateAccount(string displayName) => new()
    {
        Id = "microsoft-account",
        DisplayName = displayName,
        Uuid = "00000000-0000-0000-0000-000000000001",
        Kind = LauncherAccountKind.Microsoft
    };

    private static async Task<AccountListViewModel> CreateAccountListAsync(LauncherAccount account)
    {
        var accountList = new AccountListViewModel(
            new FakeAccountStore(new AccountStoreSnapshot([account], account.Id)));
        await accountList.InitializeAsync();
        return accountList;
    }

    private sealed class FakeReauthenticationDialog(
        Func<LauncherAccount, Task<bool>> show) : IMicrosoftAccountReauthenticationDialogService
    {
        public int CallCount { get; private set; }

        public Task<bool> ShowMicrosoftReauthenticationDialogAsync(LauncherAccount account)
        {
            CallCount++;
            return show(account);
        }
    }

    private sealed class FakeAccountStore(AccountStoreSnapshot snapshot) : IAccountStore
    {
        public Task<AccountStoreSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task<AccountStoreSnapshot> LoadCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task SaveOrderAsync(
            string? selectedAccountId,
            IEnumerable<LauncherAccount> accounts,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
