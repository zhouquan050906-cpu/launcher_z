/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Reflection;
using Launcher.App.Services;
using Launcher.App.ViewModels.Account;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.Account;

public sealed class AccountSkinLibraryThirdPartyPreviewTests
{
    [Fact]
    public async Task ThirdPartyAccountWithCachedSkinShowsReadOnlyPreview()
    {
        var skin = new LauncherSkinRecord
        {
            Id = "cached-skin",
            Source = "file:///C:/skin-cache/cached-skin.png",
            SkinModel = MinecraftSkinModel.Slim,
            ContentHash = "cached-skin-hash"
        };
        var account = CreateThirdPartyAccount(
            skinSource: skin.Source,
            skinModel: skin.SkinModel,
            skinLibrary: [skin],
            activeSkinId: skin.Id);

        using var appearance = await CreateAppearanceAsync(account);
        var skinLibrary = appearance.SkinLibrary;

        Assert.True(skinLibrary.IsThirdParty);
        Assert.Same(skin, skinLibrary.SelectedSkin);
        Assert.True(skinLibrary.HasPreview);
        Assert.False(skinLibrary.CanShowPreviewEmptyState);
        Assert.False(skinLibrary.CanShowStandardActions);
        Assert.False(skinLibrary.CanChangeSkin);
        Assert.False(skinLibrary.CanManageSkins);
        Assert.False(skinLibrary.CanApplySkin);
    }

    [Fact]
    public async Task ThirdPartyAccountWithoutCachedSkinShowsEmptyState()
    {
        var account = CreateThirdPartyAccount();

        using var appearance = await CreateAppearanceAsync(account);
        var skinLibrary = appearance.SkinLibrary;

        Assert.True(skinLibrary.IsThirdParty);
        Assert.Null(skinLibrary.SelectedSkin);
        Assert.False(skinLibrary.HasPreview);
        Assert.True(skinLibrary.CanShowPreviewEmptyState);
        Assert.False(skinLibrary.CanShowStandardActions);
    }

    private static LauncherAccount CreateThirdPartyAccount(
        string? skinSource = null,
        MinecraftSkinModel? skinModel = null,
        IReadOnlyList<LauncherSkinRecord>? skinLibrary = null,
        string? activeSkinId = null) =>
        new()
        {
            Id = "third-party-account",
            DisplayName = "Third Party Player",
            Uuid = "00000000-0000-0000-0000-000000000001",
            Kind = LauncherAccountKind.ThirdParty,
            AuthenticationServerUrl = "https://littleskin.cn/api/yggdrasil",
            SkinSource = skinSource,
            SkinModel = skinModel,
            SkinLibrary = skinLibrary ?? [],
            ActiveSkinId = activeSkinId
        };

    private static async Task<AccountAppearanceViewModel> CreateAppearanceAsync(LauncherAccount account)
    {
        var accountList = new AccountListViewModel(new SnapshotAccountStore(account));
        await accountList.InitializeAsync();
        return new AccountAppearanceViewModel(
            accountList,
            Stub<IMicrosoftAccountService>(),
            Stub<IThirdPartyAccountService>(),
            Stub<IAccountSkinLibraryService>(),
            new AccountSkinModelDialogViewModel(),
            Stub<IAccountDialogService>(),
            Stub<IFilePickerService>(),
            Stub<IMinecraftSkinFileValidator>());
    }

    private static T Stub<T>() where T : class =>
        DispatchProxy.Create<T, DefaultInterfaceProxy>();

    public class DefaultInterfaceProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returnType = targetMethod?.ReturnType ?? typeof(void);
            if (returnType == typeof(void))
                return null;
            if (returnType == typeof(Task))
                return Task.CompletedTask;
            if (returnType.IsGenericType
                && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var valueType = returnType.GetGenericArguments()[0];
                var value = valueType.IsValueType ? Activator.CreateInstance(valueType) : null;
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(valueType)
                    .Invoke(null, [value]);
            }

            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }

    private sealed class SnapshotAccountStore(LauncherAccount account) : IAccountStore
    {
        private readonly AccountStoreSnapshot snapshot = new([account], account.Id);

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
