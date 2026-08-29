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

using Launcher.Application.Accounts;
using Launcher.Domain.Models;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Application.Accounts;

public sealed class AccountStore : IAccountStore
{
    private readonly IAccountStateService accountStateService;
    private readonly IMicrosoftAccountService microsoftAccountService;
    private readonly IOfflineAccountUuidService offlineUuidService;
    private readonly IAccountSkinLibraryService skinLibraryService;
    private readonly ILogger<AccountStore> logger;

    public AccountStore(
        IAccountStateService accountStateService,
        IMicrosoftAccountService microsoftAccountService,
        IOfflineAccountUuidService offlineUuidService,
        IAccountSkinLibraryService skinLibraryService,
        ILogger<AccountStore>? logger = null)
    {
        this.accountStateService = accountStateService;
        this.microsoftAccountService = microsoftAccountService;
        this.offlineUuidService = offlineUuidService;
        this.skinLibraryService = skinLibraryService;
        this.logger = logger ?? NullLogger<AccountStore>.Instance;
    }

    public async Task<AccountStoreSnapshot> LoadCachedAsync(CancellationToken cancellationToken = default)
    {
        // 首屏预热只读 account-state.json：不枚举加密凭据、不跑迁移、不写回，
        // 完整加载随后用同一份记录做在线对账，两次结果的账户身份保持一致。
        var state = await accountStateService.LoadAsync(cancellationToken);
        var accounts = state.Accounts
            .Select(AccountMapper.FromRecord)
            .ToList();
        logger.LogDebug("Cached accounts loaded for priming. AccountCount={AccountCount}", accounts.Count);
        return new AccountStoreSnapshot(accounts, state.SelectedAccountId);
    }

    public async Task<AccountStoreSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var state = await accountStateService.LoadAsync(cancellationToken);
        var shouldPersistSkinLibraryMigration = await TryMigrateSharedSkinLibraryAsync(
            state,
            cancellationToken);
        var accounts = new List<LauncherAccount>();
        var microsoftAccounts = new Dictionary<string, LauncherAccount>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in await microsoftAccountService.GetSavedAccountsAsync(cancellationToken))
        {
            if (!microsoftAccounts.ContainsKey(account.Id))
                microsoftAccounts.Add(account.Id, account);
        }

        var shouldImportMicrosoftAccounts = !state.MicrosoftAccountsImported;
        var shouldPersistOrder = false;

        foreach (var account in state.Accounts)
        {
            var kind = account.Kind ?? (account.IsOffline
                ? LauncherAccountKind.Offline
                : LauncherAccountKind.Microsoft);
            if (kind == LauncherAccountKind.Offline)
            {
                shouldPersistOrder |= EnsureOfflineUuid(account);
                accounts.Add(AccountMapper.FromOfflineRecord(account));
                continue;
            }

            if (kind == LauncherAccountKind.ThirdParty)
            {
                accounts.Add(AccountMapper.FromRecord(account));
                continue;
            }

            if (microsoftAccounts.Remove(account.Id, out var microsoftAccount))
            {
                var mergedAccount = AccountMapper.MergeStoredRecord(microsoftAccount, account);
                accounts.Add(mergedAccount);
                shouldPersistOrder |= ShouldPersistMergedMicrosoftAccount(account, mergedAccount);
            }
            else
            {
                // Account metadata is independent from the encrypted Microsoft credential store.
                // Missing credentials must keep the account visible so launch can request reauthentication.
                accounts.Add(AccountMapper.FromRecord(account));
            }
        }

        foreach (var account in microsoftAccounts.Values)
        {
            if (shouldImportMicrosoftAccounts && accounts.All(item => item.Id != account.Id))
            {
                accounts.Add(account);
                shouldPersistOrder = true;
            }
        }

        if (state.SharedSkinLibraryMigrationVersion
            >= LauncherAccountState.CurrentSharedSkinLibraryMigrationVersion)
        {
            for (var index = 0; index < accounts.Count; index++)
            {
                var account = accounts[index];
                var normalized = AccountMapper.WithCurrentSkinReferenceOnly(account);
                if (!AccountSkinStateEqual(account, normalized))
                    shouldPersistOrder = true;
                accounts[index] = normalized;
            }
        }

        shouldPersistOrder |= await TrySyncMicrosoftAccountSkinsAsync(
            accounts,
            cancellationToken);

        if (shouldPersistOrder || shouldImportMicrosoftAccounts || shouldPersistSkinLibraryMigration)
        {
            logger.LogDebug(
                "Persisting account order after load. AccountCount={AccountCount} ImportedMicrosoftAccounts={ImportedMicrosoftAccounts}",
                accounts.Count,
                shouldImportMicrosoftAccounts);
            await SaveOrderCoreAsync(state, state.SelectedAccountId, accounts, cancellationToken);
            state = await accountStateService.LoadAsync(cancellationToken);
        }

        logger.LogInformation(
            "Accounts loaded. AccountCount={AccountCount} MicrosoftAccountCount={MicrosoftAccountCount}",
            accounts.Count,
            accounts.Count(account => account.IsMicrosoft));
        return new AccountStoreSnapshot(accounts, state.SelectedAccountId);
    }

    public async Task SaveOrderAsync(
        string? selectedAccountId,
        IEnumerable<LauncherAccount> accounts,
        CancellationToken cancellationToken = default)
    {
        var state = await accountStateService.LoadAsync(cancellationToken);
        await SaveOrderCoreAsync(state, selectedAccountId, accounts, cancellationToken);
    }

    private async Task SaveOrderCoreAsync(
        LauncherAccountState state,
        string? selectedAccountId,
        IEnumerable<LauncherAccount> accounts,
        CancellationToken cancellationToken)
    {
        state.AccountsInitialized = true;
        state.MicrosoftAccountsImported = true;
        var records = accounts
            .Select(AccountMapper.ToRecord)
            .ToList();
        foreach (var account in records.Where(account => account.IsOffline))
            EnsureOfflineUuid(account);

        state.Accounts = records;
        state.SelectedAccountId = selectedAccountId;

        if (!string.IsNullOrWhiteSpace(state.SelectedAccountId)
            && state.Accounts.All(account => !string.Equals(account.Id, state.SelectedAccountId, StringComparison.Ordinal)))
        {
            state.SelectedAccountId = null;
        }

        var firstOfflineAccount = state.Accounts.FirstOrDefault(account => account.IsOffline);
        if (firstOfflineAccount is not null)
            state.OfflineUsername = firstOfflineAccount.DisplayName;

        await accountStateService.SaveAsync(state, cancellationToken);
        logger.LogDebug(
            "Account order saved. AccountCount={AccountCount} SelectedAccountId={SelectedAccountId}",
            state.Accounts.Count,
            state.SelectedAccountId);
    }

    private async Task<bool> TryMigrateSharedSkinLibraryAsync(
        LauncherAccountState state,
        CancellationToken cancellationToken)
    {
        if (state.SharedSkinLibraryMigrationVersion
            >= LauncherAccountState.CurrentSharedSkinLibraryMigrationVersion)
        {
            return false;
        }

        try
        {
            var legacyAccounts = state.Accounts
                .Select(AccountMapper.FromRecord)
                .Where(account => !account.IsThirdParty)
                .ToList();
            await skinLibraryService.MigrateLegacySkinsAsync(legacyAccounts, cancellationToken);
            state.SharedSkinLibraryMigrationVersion =
                LauncherAccountState.CurrentSharedSkinLibraryMigrationVersion;
            logger.LogInformation(
                "Legacy account skin libraries migrated to the shared library. AccountCount={AccountCount}",
                legacyAccounts.Count);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Legacy account skin library migration failed and will be retried on the next startup.");
            return false;
        }
    }

    private async Task<bool> TrySyncMicrosoftAccountSkinsAsync(
        List<LauncherAccount> accounts,
        CancellationToken cancellationToken)
    {
        try
        {
            var synchronized = await skinLibraryService.SyncMicrosoftAccountSkinsAsync(
                accounts,
                cancellationToken);
            var changed = false;
            for (var index = 0; index < accounts.Count; index++)
            {
                var account = accounts[index];
                if (!synchronized.TryGetValue(account.Id, out var sharedSkin))
                    continue;

                var updated = AccountMapper.WithSkinLibrary(
                    account,
                    [sharedSkin],
                    sharedSkin.Id,
                    sharedSkin.Source,
                    sharedSkin.SkinModel);
                if (!AccountSkinStateEqual(account, updated))
                    changed = true;
                accounts[index] = updated;
            }

            return changed;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Current Microsoft account skins could not be synchronized to the shared library.");
            return false;
        }
    }

    private bool EnsureOfflineUuid(LauncherAccountRecord account)
    {
        var uuid = offlineUuidService.CreateUuid(
            account.DisplayName,
            account.OfflineUuidGenerationMode,
            account.Uuid);

        if (string.Equals(account.Uuid, uuid, StringComparison.Ordinal))
            return false;

        account.Uuid = uuid;
        return true;
    }

    private static bool ShouldPersistMergedMicrosoftAccount(
        LauncherAccountRecord storedAccount,
        LauncherAccount mergedAccount)
    {
        var mergedRecord = AccountMapper.ToRecord(mergedAccount);
        return !string.Equals(storedAccount.DisplayName, mergedRecord.DisplayName, StringComparison.Ordinal)
            || !string.Equals(storedAccount.Uuid, mergedRecord.Uuid, StringComparison.Ordinal)
            || !string.Equals(storedAccount.AvatarSource, mergedRecord.AvatarSource, StringComparison.Ordinal)
            || !string.Equals(storedAccount.SkinSource, mergedRecord.SkinSource, StringComparison.Ordinal)
            || storedAccount.SkinModel != mergedRecord.SkinModel
            || !string.Equals(storedAccount.ActiveSkinId, mergedRecord.ActiveSkinId, StringComparison.Ordinal)
            || !SkinRecordsEqual(storedAccount.Skins, mergedRecord.Skins)
            || !CapeRecordsEqual(storedAccount.Capes, mergedRecord.Capes);
    }

    private static bool SkinRecordsEqual(
        IReadOnlyList<LauncherSkinRecord> left,
        IReadOnlyList<LauncherSkinRecord> right)
    {
        if (left.Count != right.Count)
            return false;

        return left
            .Zip(right)
            .All(pair => string.Equals(pair.First.Id, pair.Second.Id, StringComparison.Ordinal)
                && string.Equals(pair.First.Source, pair.Second.Source, StringComparison.Ordinal)
                && pair.First.SkinModel == pair.Second.SkinModel
                && string.Equals(pair.First.ContentHash, pair.Second.ContentHash, StringComparison.Ordinal)
                && pair.First.AddedAtUtc == pair.Second.AddedAtUtc);
    }

    private static bool AccountSkinStateEqual(LauncherAccount left, LauncherAccount right)
    {
        return string.Equals(left.SkinSource, right.SkinSource, StringComparison.Ordinal)
            && left.SkinModel == right.SkinModel
            && string.Equals(left.ActiveSkinId, right.ActiveSkinId, StringComparison.Ordinal)
            && SkinRecordsEqual(left.SkinLibrary, right.SkinLibrary);
    }

    private static bool CapeRecordsEqual(
        IReadOnlyList<LauncherCapeRecord> left,
        IReadOnlyList<LauncherCapeRecord> right)
    {
        if (left.Count != right.Count)
            return false;

        return left
            .Zip(right)
            .All(pair => string.Equals(pair.First.Id, pair.Second.Id, StringComparison.Ordinal)
                && string.Equals(pair.First.DisplayName, pair.Second.DisplayName, StringComparison.Ordinal)
                && string.Equals(pair.First.ImageUrl, pair.Second.ImageUrl, StringComparison.Ordinal)
                && pair.First.IsActive == pair.Second.IsActive
                && pair.First.IsNone == pair.Second.IsNone);
    }
}
