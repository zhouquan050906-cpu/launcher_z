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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Application.Accounts;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Account;

public sealed partial class AccountDialogViewModel
{
    public void OpenThirdPartyAddAccountDialog(string authenticationServer)
    {
        ResetAddAccountDialogState(clearOfflineName: true);
        isDirectThirdPartyAddAccountEntry = true;
        navigateToAccountAfterThirdPartyAddition = true;
        ThirdParty.AuthenticationServer = authenticationServer;
        AddAccountDialogStep = AccountDialogSteps.AddAccountThirdPartyCredentials;
        IsAddAccountDialogOpen = true;
    }

    /// <summary>
    /// 把拖入的认证服务器地址应用到当前的添加账户对话框。
    /// 返回 true 表示已输入的凭据被清空，调用方需要同步清空视图上的密码框。
    /// </summary>
    public bool ApplyThirdPartyAuthenticationServer(string authenticationServer)
    {
        if (!IsAccountTypeStep && !IsThirdPartyCredentialsStep)
            return false;

        navigateToAccountAfterThirdPartyAddition = true;
        // 换了认证服务器，为上一个服务器输入的用户名和密码不能沿用，
        // 否则用户可能没注意到地址已变就直接提交，把凭据发给另一个服务器。
        var clearedCredentials = !string.Equals(
            ThirdParty.AuthenticationServer,
            authenticationServer,
            StringComparison.Ordinal);
        if (clearedCredentials)
            ThirdParty.ResetCredentials();

        ThirdParty.AuthenticationServer = authenticationServer;
        if (IsAccountTypeStep)
            AddAccountDialogStep = AccountDialogSteps.AddAccountThirdPartyCredentials;

        return clearedCredentials;
    }

    public void SelectAllThirdPartyProfiles() => ThirdParty.SelectAllProfiles();

    public Task RetryThirdPartyProfileImportAsync(string password) =>
        ImportThirdPartyProfilesAsync(thirdPartyFailedProfiles.ToArray(), password);

    private async Task ImportThirdPartyProfilesAsync(
        IReadOnlyList<ThirdPartyProfileOptionViewModel> profiles,
        string password)
    {
        if (profiles.Count == 0)
            return;
        thirdPartyFailedProfiles.Clear();
        ThirdPartyImportFailedCount = 0;
        ThirdPartyImportCompletedCount = 0;
        ThirdPartyImportTotalCount = profiles.Count;
        AddAccountDialogStep = AccountDialogSteps.AddAccountThirdPartyImportProgress;
        IsAddAccountDialogBusy = true;
        using var cancellation = new CancellationTokenSource();
        thirdPartyImportCancellationTokenSource = cancellation;
        try
        {
            foreach (var profile in profiles)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                ThirdPartyImportCurrentProfileName = profile.Name;
                var imported = await ThirdParty.ImportEmailProfileAsync(profile, password, cancellation.Token);
                if (imported is null)
                {
                    thirdPartyFailedProfiles.Add(profile);
                }
                else if (thirdPartySuccessfulAccounts.All(account => !string.Equals(account.Id, imported.Id, StringComparison.Ordinal)))
                {
                    thirdPartySuccessfulAccounts.Add(imported);
                }
                ThirdPartyImportCompletedCount++;
            }

            await SelectLastSuccessfulThirdPartyAccountAsync();
            ThirdPartyImportFailedCount = thirdPartyFailedProfiles.Count;
            if (thirdPartyFailedProfiles.Count == 0)
            {
                IsAddAccountDialogOpen = false;
                await ThirdParty.CancelEmailLoginAsync();
                CompleteDroppedThirdPartyAccountAddition();
            }
            else
            {
                AddAccountDialogStep = AccountDialogSteps.AddAccountThirdPartyImportResult;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // 中途取消同样可能已经导入成功若干角色，处理方式与结果页一致：选中最后一个并跳转。
            await SelectLastSuccessfulThirdPartyAccountAsync();
            IsAddAccountDialogOpen = false;
            await ThirdParty.CancelEmailLoginAsync();
            if (thirdPartySuccessfulAccounts.Count > 0)
                CompleteDroppedThirdPartyAccountAddition();
        }
        finally
        {
            thirdPartyImportCancellationTokenSource = null;
            IsAddAccountDialogBusy = false;
        }
    }

    // 此时对话框才刚被标记为关闭，收起动画尚未播放；立刻切换页面会让页面转场和弹窗淡出叠在一起。
    // 因此只登记意图，等 ResetAddAccountDialog（收起动画的完成回调）触发时再真正导航。
    private void CompleteDroppedThirdPartyAccountAddition()
    {
        if (!navigateToAccountAfterThirdPartyAddition)
            return;

        isDirectThirdPartyAddAccountEntry = false;
        navigateToAccountAfterThirdPartyAddition = false;
        hasPendingDroppedThirdPartyNavigation = true;
    }
}
