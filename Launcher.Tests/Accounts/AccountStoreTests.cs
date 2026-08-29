/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Accounts;
using Launcher.Application.Services;

namespace Launcher.Tests.Accounts;

public sealed class AccountStoreTests
{
    [Fact]
    public async Task LoadCachedReturnsStoredRecordsWithoutCredentialLookupOrPersistence()
    {
        // 首屏预热必须完全离线：读凭据库和写回都可能让窗口显示前多等一次 IO 或网络。
        var storedSkin = new LauncherSkinRecord
        {
            Id = "shared",
            Source = "file:///microsoft/_shared-library/v1-shared-classic.png",
            SkinModel = MinecraftSkinModel.Classic,
            ContentHash = "shared-hash"
        };
        var state = new FakeStateService(new LauncherAccountState
        {
            MicrosoftAccountsImported = true,
            SharedSkinLibraryMigrationVersion = LauncherAccountState.CurrentSharedSkinLibraryMigrationVersion,
            SelectedAccountId = "microsoft-uuid",
            Accounts =
            [
                new LauncherAccountRecord
                {
                    Id = "microsoft-uuid",
                    DisplayName = "Stored Microsoft",
                    Kind = LauncherAccountKind.Microsoft,
                    Uuid = "uuid",
                    SkinSource = storedSkin.Source,
                    SkinModel = storedSkin.SkinModel,
                    ActiveSkinId = storedSkin.Id,
                    Skins = [storedSkin]
                }
            ]
        });
        var microsoftService = new FakeMicrosoftService();
        var store = new AccountStore(
            state,
            microsoftService,
            new FakeOfflineAccountUuidService(),
            new FakeSkinLibraryService());

        var snapshot = await store.LoadCachedAsync();

        var account = Assert.Single(snapshot.Accounts);
        Assert.Equal("microsoft-uuid", snapshot.SelectedAccountId);
        Assert.Equal("Stored Microsoft", account.DisplayName);
        Assert.Equal(storedSkin.Source, account.SkinSource);
        Assert.Equal(storedSkin.Id, account.ActiveSkinId);
        Assert.Equal(0, microsoftService.SavedAccountQueryCount);
        Assert.Equal(0, state.SaveCount);
    }

    [Fact]
    public async Task LoadPreservesOrderAndImportsMicrosoftAccountsOnce()
    {
        var state = new FakeStateService(new LauncherAccountState
        {
            SharedSkinLibraryMigrationVersion = LauncherAccountState.CurrentSharedSkinLibraryMigrationVersion,
            Accounts =
            [
                new() { Id = "offline", DisplayName = "Local", IsOffline = true },
                new() { Id = "ms-1", DisplayName = "Stored", Uuid = "old", IsOffline = false }
            ]
        });
        var store = new AccountStore(state, new FakeMicrosoftService(
            new LauncherAccount { Id = "ms-1", DisplayName = "Live", Uuid = "new", Kind = LauncherAccountKind.Microsoft, HasFreshProfile = true },
            new LauncherAccount { Id = "ms-2", DisplayName = "Imported", Uuid = "uuid", Kind = LauncherAccountKind.Microsoft }),
            new FakeOfflineAccountUuidService(),
            new FakeSkinLibraryService());

        var accounts = (await store.LoadAsync()).Accounts;

        Assert.Equal(["offline", "ms-1", "ms-2"], accounts.Select(account => account.Id));
        Assert.Equal("Live", accounts[1].DisplayName);
        Assert.True(state.State.MicrosoftAccountsImported);
        Assert.Equal(1, state.SaveCount);
    }

    [Fact]
    public async Task LoadPreservesThirdPartyRecordsWithoutMicrosoftReconciliation()
    {
        var state = new FakeStateService(new LauncherAccountState
        {
            MicrosoftAccountsImported = true,
            SharedSkinLibraryMigrationVersion = LauncherAccountState.CurrentSharedSkinLibraryMigrationVersion,
            Accounts = [new LauncherAccountRecord
            {
                Id = "third-party-id",
                DisplayName = "Player",
                Kind = LauncherAccountKind.ThirdParty,
                IsOffline = false,
                Uuid = "00112233-4455-6677-8899-aabbccddeeff",
                AuthenticationServerUrl = "https://example.test/api/yggdrasil/",
                ThirdPartyPlatformName = "Example Auth",
                ThirdPartyLoginUsername = "player"
            }]
        });
        var store = new AccountStore(
            state,
            new FakeMicrosoftService(),
            new FakeOfflineAccountUuidService(),
            new FakeSkinLibraryService());

        var account = Assert.Single((await store.LoadAsync()).Accounts);

        Assert.True(account.IsThirdParty);
        Assert.Equal("https://example.test/api/yggdrasil/", account.AuthenticationServerUrl);
        Assert.Equal("Example Auth", account.ThirdPartyPlatformName);
        Assert.Equal("player", account.ThirdPartyLoginUsername);
        Assert.Equal(0, state.SaveCount);
    }

    [Fact]
    public async Task LoadKeepsMicrosoftMetadataWhenEncryptedCredentialsAreMissing()
    {
        var state = new FakeStateService(new LauncherAccountState
        {
            MicrosoftAccountsImported = true,
            SharedSkinLibraryMigrationVersion = LauncherAccountState.CurrentSharedSkinLibraryMigrationVersion,
            Accounts = [new LauncherAccountRecord
            {
                Id = "microsoft-uuid",
                DisplayName = "Stored Microsoft",
                Kind = LauncherAccountKind.Microsoft,
                IsOffline = false,
                Uuid = "uuid"
            }]
        });
        var store = new AccountStore(
            state,
            new FakeMicrosoftService(),
            new FakeOfflineAccountUuidService(),
            new FakeSkinLibraryService());

        var account = Assert.Single((await store.LoadAsync()).Accounts);

        Assert.True(account.IsMicrosoft);
        Assert.Equal("Stored Microsoft", account.DisplayName);
        Assert.Equal("uuid", account.Uuid);
        Assert.Equal(0, state.SaveCount);
    }

    [Fact]
    public async Task FailedSharedLibraryMigrationIsRetriedWithoutAdvancingVersion()
    {
        var state = new FakeStateService(new LauncherAccountState
        {
            MicrosoftAccountsImported = true
        });
        var skinLibrary = new FakeSkinLibraryService { ThrowOnMigration = true };
        var store = new AccountStore(
            state,
            new FakeMicrosoftService(),
            new FakeOfflineAccountUuidService(),
            skinLibrary);

        await store.LoadAsync();

        Assert.Equal(0, state.State.SharedSkinLibraryMigrationVersion);
        Assert.Equal(0, state.SaveCount);

        skinLibrary.ThrowOnMigration = false;
        await store.LoadAsync();

        Assert.Equal(2, skinLibrary.MigrationCount);
        Assert.Equal(
            LauncherAccountState.CurrentSharedSkinLibraryMigrationVersion,
            state.State.SharedSkinLibraryMigrationVersion);
        Assert.Equal(1, state.SaveCount);
    }

    [Fact]
    public async Task LoadReplacesLegacyMicrosoftSkinReferenceWithSharedRecord()
    {
        var legacySkin = new LauncherSkinRecord
        {
            Id = "legacy",
            Source = "file:///microsoft/uuid/v1-legacy.png",
            SkinModel = MinecraftSkinModel.Slim,
            ContentHash = "legacy-hash"
        };
        var sharedSkin = new LauncherSkinRecord
        {
            Id = "shared",
            Source = "file:///microsoft/_shared-library/v1-shared-slim.png",
            SkinModel = MinecraftSkinModel.Slim,
            ContentHash = "shared-hash"
        };
        var state = new FakeStateService(new LauncherAccountState
        {
            MicrosoftAccountsImported = true,
            SharedSkinLibraryMigrationVersion = LauncherAccountState.CurrentSharedSkinLibraryMigrationVersion,
            Accounts =
            [
                new LauncherAccountRecord
                {
                    Id = "microsoft",
                    DisplayName = "Microsoft",
                    Kind = LauncherAccountKind.Microsoft,
                    Uuid = "uuid",
                    SkinSource = legacySkin.Source,
                    SkinModel = legacySkin.SkinModel,
                    ActiveSkinId = legacySkin.Id,
                    Skins = [legacySkin]
                }
            ]
        });
        var skinLibrary = new FakeSkinLibraryService
        {
            SynchronizedSkins = new Dictionary<string, LauncherSkinRecord>
            {
                ["microsoft"] = sharedSkin
            }
        };
        var store = new AccountStore(
            state,
            new FakeMicrosoftService(),
            new FakeOfflineAccountUuidService(),
            skinLibrary);

        var account = Assert.Single((await store.LoadAsync()).Accounts);

        Assert.Equal(sharedSkin.Id, account.ActiveSkinId);
        Assert.Equal(sharedSkin.Source, account.SkinSource);
        Assert.Equal(sharedSkin.Id, Assert.Single(account.SkinLibrary).Id);
        Assert.Equal(1, state.SaveCount);
        Assert.Equal(sharedSkin.Source, Assert.Single(state.State.Accounts).SkinSource);

        await store.LoadAsync();

        Assert.Equal(1, state.SaveCount);
    }

    private sealed class FakeStateService(LauncherAccountState state) : IAccountStateService
    {
        public LauncherAccountState State { get; private set; } = state;
        public int SaveCount { get; private set; }
        public Task<LauncherAccountState> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(State);
        public Task SaveAsync(LauncherAccountState value, CancellationToken cancellationToken = default)
        { State = value; SaveCount++; return Task.CompletedTask; }
    }

    private sealed class FakeMicrosoftService(params LauncherAccount[] accounts) : IMicrosoftAccountService
    {
        public int SavedAccountQueryCount { get; private set; }

        public Task<IReadOnlyList<LauncherAccount>> GetSavedAccountsAsync(CancellationToken cancellationToken = default)
        {
            SavedAccountQueryCount++;
            return Task.FromResult<IReadOnlyList<LauncherAccount>>(accounts);
        }

        public Task<LauncherAccount> LoginInteractivelyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LauncherAccount> ReauthenticateInteractivelyAsync(LauncherAccount account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAccountAsync(LauncherAccount account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccountCapeOption>> GetCapesAsync(LauncherAccount account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LauncherAccount> RefreshAccountProfileAsync(LauncherAccount account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LauncherAccount> UploadSkinAsync(LauncherAccount account, string path, MinecraftSkinModel model,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetActiveCapeAsync(LauncherAccount account, string? capeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LauncherAccount> ChangeNameAsync(LauncherAccount account, string name,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeSkinLibraryService : IAccountSkinLibraryService
    {
        public int MigrationCount { get; private set; }
        public IReadOnlyDictionary<string, LauncherSkinRecord> SynchronizedSkins { get; set; } =
            new Dictionary<string, LauncherSkinRecord>();
        public bool ThrowOnMigration { get; set; }

        public IReadOnlyList<LauncherSkinRecord> GetAvailableSkins(LauncherAccount account) => [];

        public IReadOnlyList<LauncherSkinRecord> GetSharedSkins() => [];

        public Task MigrateLegacySkinsAsync(
            IReadOnlyList<LauncherAccount> accounts,
            CancellationToken cancellationToken = default)
        {
            MigrationCount++;
            if (ThrowOnMigration)
                throw new IOException("Simulated migration failure.");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, LauncherSkinRecord>> SyncMicrosoftAccountSkinsAsync(
            IReadOnlyList<LauncherAccount> accounts,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SynchronizedSkins);
        }

        public Task<LauncherSkinRecord> ImportSkinAsync(
            LauncherAccount account,
            string skinFilePath,
            MinecraftSkinModel skinModel,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> CreateAvatarSourceAsync(
            LauncherAccount account,
            LauncherSkinRecord skin,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteSkinAsync(
            LauncherAccount account,
            LauncherSkinRecord skin,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
