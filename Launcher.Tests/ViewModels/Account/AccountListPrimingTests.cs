/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.ViewModels.Account;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.Account;

public sealed class AccountListPrimingTests
{
    [Fact]
    public async Task PrimeShowsStoredAccountsAndSelectionBeforeTheFullLoad()
    {
        var account = new LauncherAccount
        {
            Id = "microsoft-account",
            DisplayName = "Player",
            Uuid = "00000000-0000-0000-0000-000000000001",
            Kind = LauncherAccountKind.Microsoft,
            AvatarSource = "file:///avatars/player.png"
        };
        var store = new FakeAccountStore(new AccountStoreSnapshot([account], account.Id));
        var accountList = new AccountListViewModel(store);

        await accountList.PrimeAsync();

        Assert.Equal(1, store.CachedLoadCount);
        Assert.Equal(0, store.LoadCount);
        var item = Assert.Single(accountList.Accounts);
        Assert.Equal("Player", item.Account.DisplayName);
        Assert.Equal("file:///avatars/player.png", item.Account.AvatarSource);
        Assert.Same(item, accountList.SelectedItem);
    }

    [Fact]
    public async Task FailedPrimeLeavesTheListEmptyInsteadOfBreakingStartup()
    {
        // 预热跑在窗口显示之前，抛出去就是启动失败；读不出记录只能安静地留给完整加载处理。
        var store = new FakeAccountStore(new AccountStoreSnapshot([], null))
        {
            CachedLoadFailure = new IOException("Simulated account state read failure.")
        };
        var accountList = new AccountListViewModel(store);

        await accountList.PrimeAsync();

        Assert.Empty(accountList.Accounts);
        Assert.Null(accountList.SelectedItem);
    }

    private sealed class FakeAccountStore(AccountStoreSnapshot snapshot) : IAccountStore
    {
        public int LoadCount { get; private set; }

        public int CachedLoadCount { get; private set; }

        public Exception? CachedLoadFailure { get; set; }

        public Task<AccountStoreSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(snapshot);
        }

        public Task<AccountStoreSnapshot> LoadCachedAsync(CancellationToken cancellationToken = default)
        {
            CachedLoadCount++;
            return CachedLoadFailure is null
                ? Task.FromResult(snapshot)
                : Task.FromException<AccountStoreSnapshot>(CachedLoadFailure);
        }

        public Task SaveOrderAsync(
            string? selectedAccountId,
            IEnumerable<LauncherAccount> accounts,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
