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

public sealed class ThirdPartyAccountDropDialogTests
{
    [Fact]
    public void OpenThirdPartyDialogResetsPreviousStateAndPrefillsOnlyServer()
    {
        var viewModel = CreateViewModel();
        viewModel.OpenAddAccountDialog();
        viewModel.NewOfflineAccountName = "Old offline name";
        viewModel.IsNewOfflineAccountNameInvalid = true;
        viewModel.IsAddAccountDialogBusy = true;
        viewModel.ThirdParty.AuthenticationServer = "https://old.example/api";
        viewModel.ThirdParty.UsernameOrEmail = "old@example.com";
        viewModel.ThirdParty.UpdatePasswordState(hasPassword: true);
        viewModel.ThirdParty.AuthenticationServerError = "old server error";
        viewModel.ThirdParty.UsernameError = "old username error";
        viewModel.ThirdParty.PasswordError = "old password error";

        viewModel.OpenThirdPartyAddAccountDialog("https://littleskin.cn/api/yggdrasil");

        Assert.True(viewModel.IsAddAccountDialogOpen);
        Assert.True(viewModel.IsThirdPartyCredentialsStep);
        Assert.False(viewModel.IsAddAccountDialogBusy);
        Assert.Equal("https://littleskin.cn/api/yggdrasil", viewModel.ThirdParty.AuthenticationServer);
        Assert.Equal(string.Empty, viewModel.ThirdParty.UsernameOrEmail);
        Assert.False(viewModel.ThirdParty.HasPassword);
        Assert.Equal(string.Empty, viewModel.ThirdParty.AuthenticationServerError);
        Assert.Equal(string.Empty, viewModel.ThirdParty.UsernameError);
        Assert.Equal(string.Empty, viewModel.ThirdParty.PasswordError);
        Assert.Equal(string.Empty, viewModel.NewOfflineAccountName);
        Assert.False(viewModel.IsNewOfflineAccountNameInvalid);
        Assert.Null(viewModel.SelectedAccountTypeOption);
        Assert.False(viewModel.CanShowAddAccountBackButton);
        Assert.True(viewModel.CanShowAddAccountCancelButton);
        Assert.False(viewModel.CanConfirmAddAccountDialog);
    }

    [Fact]
    public async Task DirectThirdPartyEntryConfirmsWithoutSelectedAccountType()
    {
        var accountService = new RecordingThirdPartyAccountService();
        var viewModel = CreateViewModel(out var accountList, accountService);
        var completionCount = 0;
        viewModel.DroppedThirdPartyAccountAdditionCompleted += () => completionCount++;
        viewModel.OpenThirdPartyAddAccountDialog("https://littleskin.cn/api/yggdrasil");
        viewModel.ThirdParty.UsernameOrEmail = "DropPlayer";
        viewModel.ThirdParty.UpdatePasswordState(hasPassword: true);

        await viewModel.ConfirmAddAccountDialogAsync("secret");

        Assert.Equal(1, accountService.UsernameLoginCount);
        Assert.Equal("https://littleskin.cn/api/yggdrasil", accountService.AuthenticationServer);
        Assert.Equal("DropPlayer", accountService.Username);
        Assert.Equal("secret", accountService.Password);
        Assert.Equal("third-party-account", accountList.SelectedAccount?.Id);
        Assert.False(viewModel.IsAddAccountDialogOpen);
        Assert.False(viewModel.IsAddAccountDialogBusy);

        // 页面切换要等收起动画播完，否则会和弹窗淡出叠在一起。
        Assert.Equal(0, completionCount);
        viewModel.ResetAddAccountDialog();
        Assert.Equal(1, completionCount);
    }

    [Fact]
    public async Task DirectEmailEntrySelectsLastImportedProfileAndSignalsCompletion()
    {
        var accountService = new RecordingThirdPartyAccountService();
        var viewModel = CreateViewModel(out var accountList, accountService);
        var completionCount = 0;
        viewModel.DroppedThirdPartyAccountAdditionCompleted += () => completionCount++;
        viewModel.OpenThirdPartyAddAccountDialog("https://littleskin.cn/api/yggdrasil");
        viewModel.ThirdParty.UsernameOrEmail = "player@example.com";
        viewModel.ThirdParty.UpdatePasswordState(hasPassword: true);

        await viewModel.ConfirmAddAccountDialogAsync("secret");
        viewModel.SelectAllThirdPartyProfiles();
        await viewModel.ConfirmAddAccountDialogAsync("secret");

        Assert.Equal(2, accountService.ImportedProfileUuids.Count);
        Assert.Equal("profile-two", accountList.SelectedAccount?.Id);
        Assert.False(viewModel.IsAddAccountDialogOpen);

        Assert.Equal(0, completionCount);
        viewModel.ResetAddAccountDialog();
        Assert.Equal(1, completionCount);
    }

    // 中途取消但已成功导入若干角色时，行为与结果页一致：选中最后一个成功账户并跳转。
    [Fact]
    public async Task CancelledImportWithSuccessfulProfilesStillNavigates()
    {
        var accountService = new RecordingThirdPartyAccountService();
        var viewModel = CreateViewModel(out var accountList, accountService);
        var completionCount = 0;
        viewModel.DroppedThirdPartyAccountAdditionCompleted += () => completionCount++;
        viewModel.OpenThirdPartyAddAccountDialog("https://littleskin.cn/api/yggdrasil");
        viewModel.ThirdParty.UsernameOrEmail = "player@example.com";
        viewModel.ThirdParty.UpdatePasswordState(hasPassword: true);
        await viewModel.ConfirmAddAccountDialogAsync("secret");
        viewModel.SelectAllThirdPartyProfiles();
        // 第一个角色导入完成后取消，第二个角色不会再导入。
        var importCalls = 0;
        accountService.OnImportProfile = () =>
        {
            if (++importCalls == 2)
                viewModel.CancelAddAccountDialog();
        };

        await viewModel.ConfirmAddAccountDialogAsync("secret");

        Assert.Single(accountService.ImportedProfileUuids);
        Assert.Equal("profile-one", accountList.SelectedAccount?.Id);
        Assert.False(viewModel.IsAddAccountDialogOpen);
        Assert.False(viewModel.IsAddAccountDialogBusy);

        Assert.Equal(0, completionCount);
        viewModel.ResetAddAccountDialog();
        Assert.Equal(1, completionCount);
    }

    // 一个角色都没导入成功时不该跳转。
    [Fact]
    public async Task CancelledImportWithoutAnySuccessDoesNotNavigate()
    {
        var accountService = new RecordingThirdPartyAccountService();
        var viewModel = CreateViewModel(accountService);
        var completionCount = 0;
        viewModel.DroppedThirdPartyAccountAdditionCompleted += () => completionCount++;
        viewModel.OpenThirdPartyAddAccountDialog("https://littleskin.cn/api/yggdrasil");
        viewModel.ThirdParty.UsernameOrEmail = "player@example.com";
        viewModel.ThirdParty.UpdatePasswordState(hasPassword: true);
        await viewModel.ConfirmAddAccountDialogAsync("secret");
        viewModel.SelectAllThirdPartyProfiles();
        // 第一个角色都还没导入完就取消。
        accountService.OnImportProfile = () => viewModel.CancelAddAccountDialog();

        await viewModel.ConfirmAddAccountDialogAsync("secret");
        viewModel.ResetAddAccountDialog();

        Assert.Empty(accountService.ImportedProfileUuids);
        Assert.False(viewModel.IsAddAccountDialogOpen);
        Assert.Equal(0, completionCount);
    }

    [Fact]
    public void ExistingManualAddEntryStillStartsAtAccountTypeSelection()
    {
        var viewModel = CreateViewModel();

        viewModel.OpenAddAccountDialog();

        Assert.True(viewModel.IsAddAccountDialogOpen);
        Assert.True(viewModel.IsAccountTypeStep);
        Assert.False(viewModel.IsAddAccountDialogBusy);
    }

    [Fact]
    public async Task ManualThirdPartyEntryStillShowsBackButton()
    {
        var viewModel = CreateViewModel();
        viewModel.OpenAddAccountDialog();
        viewModel.SelectedAccountTypeOption = viewModel.AccountTypeOptions.Single(
            option => option.Kind == AccountTypeKinds.ThirdParty);

        await viewModel.ConfirmAddAccountDialogAsync();

        Assert.True(viewModel.IsThirdPartyCredentialsStep);
        Assert.True(viewModel.CanShowAddAccountBackButton);
    }

    [Fact]
    public async Task DropAtAccountTypeStepFillsServerAndKeepsExistingDialogNavigation()
    {
        var accountService = new RecordingThirdPartyAccountService();
        var viewModel = CreateViewModel(accountService);
        var completionCount = 0;
        viewModel.DroppedThirdPartyAccountAdditionCompleted += () => completionCount++;
        viewModel.OpenAddAccountDialog();

        viewModel.ApplyThirdPartyAuthenticationServer("https://littleskin.cn/api/yggdrasil");

        Assert.True(viewModel.IsThirdPartyCredentialsStep);
        Assert.True(viewModel.CanShowAddAccountBackButton);
        Assert.Equal("https://littleskin.cn/api/yggdrasil", viewModel.ThirdParty.AuthenticationServer);

        viewModel.ThirdParty.UsernameOrEmail = "ExistingPlayer";
        viewModel.ThirdParty.UpdatePasswordState(hasPassword: true);
        await viewModel.ConfirmAddAccountDialogAsync("secret");

        Assert.Equal(1, accountService.UsernameLoginCount);
        Assert.Equal(0, completionCount);
        viewModel.ResetAddAccountDialog();
        Assert.Equal(1, completionCount);
    }

    [Fact]
    public void CancelledDropEntryNeverNavigatesToTheAccountPage()
    {
        var viewModel = CreateViewModel();
        var completionCount = 0;
        viewModel.DroppedThirdPartyAccountAdditionCompleted += () => completionCount++;
        viewModel.OpenThirdPartyAddAccountDialog("https://littleskin.cn/api/yggdrasil");

        viewModel.CancelAddAccountDialog();
        viewModel.ResetAddAccountDialog();

        Assert.False(viewModel.IsAddAccountDialogOpen);
        Assert.Equal(0, completionCount);
    }

    [Fact]
    public async Task ReopeningTheDialogDoesNotReplayAPendingNavigation()
    {
        var accountService = new RecordingThirdPartyAccountService();
        var viewModel = CreateViewModel(accountService);
        var completionCount = 0;
        viewModel.DroppedThirdPartyAccountAdditionCompleted += () => completionCount++;
        viewModel.OpenThirdPartyAddAccountDialog("https://littleskin.cn/api/yggdrasil");
        viewModel.ThirdParty.UsernameOrEmail = "DropPlayer";
        viewModel.ThirdParty.UpdatePasswordState(hasPassword: true);
        await viewModel.ConfirmAddAccountDialogAsync("secret");

        // 收起动画未回调就重新打开对话框：登记的导航意图必须随状态一起作废。
        viewModel.OpenAddAccountDialog();
        viewModel.ResetAddAccountDialog();

        Assert.Equal(0, completionCount);
    }

    // 换服务器必须丢弃已输入的凭据，否则用户可能没注意到地址已变就把密码提交给另一个服务器。
    [Fact]
    public async Task DropAtThirdPartyCredentialsStepDiscardsCredentialsForThePreviousServer()
    {
        var viewModel = CreateViewModel();
        viewModel.OpenAddAccountDialog();
        viewModel.SelectedAccountTypeOption = viewModel.AccountTypeOptions.Single(
            option => option.Kind == AccountTypeKinds.ThirdParty);
        await viewModel.ConfirmAddAccountDialogAsync();
        viewModel.ThirdParty.AuthenticationServer = "https://old.example/api";
        viewModel.ThirdParty.UsernameOrEmail = "ExistingPlayer";
        viewModel.ThirdParty.UpdatePasswordState(hasPassword: true);
        viewModel.ThirdParty.UsernameError = "existing username error";
        viewModel.ThirdParty.PasswordError = "existing password error";

        var clearedCredentials = viewModel.ApplyThirdPartyAuthenticationServer(
            "https://littleskin.cn/api/yggdrasil");

        Assert.True(clearedCredentials);
        Assert.True(viewModel.IsThirdPartyCredentialsStep);
        Assert.True(viewModel.CanShowAddAccountBackButton);
        Assert.Equal("https://littleskin.cn/api/yggdrasil", viewModel.ThirdParty.AuthenticationServer);
        Assert.Equal(string.Empty, viewModel.ThirdParty.UsernameOrEmail);
        Assert.False(viewModel.ThirdParty.HasPassword);
        Assert.Equal(string.Empty, viewModel.ThirdParty.UsernameError);
        Assert.Equal(string.Empty, viewModel.ThirdParty.PasswordError);
        Assert.False(viewModel.CanConfirmAddAccountDialog);
    }

    // 重复拖入同一个服务器不该清掉用户刚填好的内容。
    [Fact]
    public async Task DroppingTheSameServerKeepsWhatTheUserAlreadyTyped()
    {
        var viewModel = CreateViewModel();
        viewModel.OpenThirdPartyAddAccountDialog("https://littleskin.cn/api/yggdrasil");
        viewModel.ThirdParty.UsernameOrEmail = "ExistingPlayer";
        viewModel.ThirdParty.UpdatePasswordState(hasPassword: true);

        var clearedCredentials = viewModel.ApplyThirdPartyAuthenticationServer(
            "https://littleskin.cn/api/yggdrasil");

        Assert.False(clearedCredentials);
        Assert.Equal("ExistingPlayer", viewModel.ThirdParty.UsernameOrEmail);
        Assert.True(viewModel.ThirdParty.HasPassword);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task DropApplicationDoesNotChangeOfflineOrMicrosoftSteps()
    {
        var offlineViewModel = CreateViewModel();
        offlineViewModel.OpenAddAccountDialog();
        offlineViewModel.SelectedAccountTypeOption = offlineViewModel.AccountTypeOptions.Single(
            option => option.Kind == AccountTypeKinds.Offline);
        await offlineViewModel.ConfirmAddAccountDialogAsync();
        offlineViewModel.ThirdParty.AuthenticationServer = "https://old.example/api";

        Assert.False(
            offlineViewModel.ApplyThirdPartyAuthenticationServer("https://littleskin.cn/api/yggdrasil"));

        Assert.True(offlineViewModel.IsOfflineNameStep);
        Assert.Equal("https://old.example/api", offlineViewModel.ThirdParty.AuthenticationServer);

        var microsoftViewModel = CreateViewModel();
        microsoftViewModel.OpenAddAccountDialog();
        microsoftViewModel.BeginMicrosoftAccountLogin();
        microsoftViewModel.ThirdParty.AuthenticationServer = "https://old.example/api";

        Assert.False(
            microsoftViewModel.ApplyThirdPartyAuthenticationServer("https://littleskin.cn/api/yggdrasil"));

        Assert.True(microsoftViewModel.IsMicrosoftLoginStep);
        Assert.Equal("https://old.example/api", microsoftViewModel.ThirdParty.AuthenticationServer);
    }

    private static AccountDialogViewModel CreateViewModel(
        IThirdPartyAccountService? thirdPartyAccountService = null) =>
        CreateViewModel(out _, thirdPartyAccountService);

    private static AccountDialogViewModel CreateViewModel(
        out AccountListViewModel accountList,
        IThirdPartyAccountService? thirdPartyAccountService = null)
    {
        accountList = new AccountListViewModel(
            new EmptyAccountStore());
        return new AccountDialogViewModel(
            accountList,
            Stub<IMicrosoftAccountService>(),
            thirdPartyAccountService ?? Stub<IThirdPartyAccountService>(),
            Stub<IOfflineAccountUuidService>(),
            Stub<IStatusService>());
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

    private sealed class EmptyAccountStore : IAccountStore
    {
        private static readonly AccountStoreSnapshot EmptySnapshot = new([], null);

        public Task<AccountStoreSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(EmptySnapshot);

        public Task<AccountStoreSnapshot> LoadCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(EmptySnapshot);

        public Task SaveOrderAsync(
            string? selectedAccountId,
            IEnumerable<LauncherAccount> accounts,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingThirdPartyAccountService : IThirdPartyAccountService
    {
        public int UsernameLoginCount { get; private set; }
        public string AuthenticationServer { get; private set; } = string.Empty;
        public string Username { get; private set; } = string.Empty;
        public string Password { get; private set; } = string.Empty;
        public List<string> ImportedProfileUuids { get; } = [];

        public Task<LauncherAccount> LoginWithUsernameAsync(
            string authenticationServer,
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            UsernameLoginCount++;
            AuthenticationServer = authenticationServer;
            Username = username;
            Password = password;
            return Task.FromResult(new LauncherAccount
            {
                Id = "third-party-account",
                DisplayName = username,
                Uuid = "00000000-0000-0000-0000-000000000001",
                Kind = LauncherAccountKind.ThirdParty,
                AuthenticationServerUrl = authenticationServer
            });
        }

        public Task<ThirdPartyEmailLoginSession> BeginEmailLoginAsync(
            string authenticationServer,
            string email,
            string password,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ThirdPartyEmailLoginSession(
                "attempt",
                [
                    new ThirdPartyProfileOption(
                        "00000000-0000-0000-0000-000000000001",
                        "Profile One",
                        string.Empty),
                    new ThirdPartyProfileOption(
                        "00000000-0000-0000-0000-000000000002",
                        "Profile Two",
                        string.Empty)
                ]));

        public Action? OnImportProfile { get; set; }

        public Task<LauncherAccount> ImportEmailProfileAsync(
            string attemptId,
            string profileUuid,
            string password,
            CancellationToken cancellationToken = default)
        {
            OnImportProfile?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            ImportedProfileUuids.Add(profileUuid);
            var isFirst = profileUuid.EndsWith('1');
            return Task.FromResult(new LauncherAccount
            {
                Id = isFirst ? "profile-one" : "profile-two",
                DisplayName = isFirst ? "Profile One" : "Profile Two",
                Uuid = profileUuid,
                Kind = LauncherAccountKind.ThirdParty,
                AuthenticationServerUrl = "https://littleskin.cn/api/yggdrasil"
            });
        }

        public Task CancelEmailLoginAsync(
            string attemptId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<LauncherAccount> RefreshAccountProfileAsync(
            LauncherAccount account,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LauncherAccount> ReauthenticateAsync(
            LauncherAccount account,
            string password,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteCredentialsAsync(
            string accountId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
