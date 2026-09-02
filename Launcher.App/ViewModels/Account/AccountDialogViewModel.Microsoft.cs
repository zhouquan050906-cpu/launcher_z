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
    public void OpenAddAccountDialog()
    {
        ResetAddAccountDialogState(clearOfflineName: true);
        IsAddAccountDialogOpen = true;
    }

    public void OpenThirdPartyReauthenticationDialog(LauncherAccount account)
    {
        ResetAddAccountDialogState(clearOfflineName: true);
        AccountPendingThirdPartyReauthentication = account;
        AddAccountDialogStep = AccountDialogSteps.AddAccountThirdPartyReauthentication;
        ThirdParty.PrepareReauthentication(account);
        IsAddAccountDialogOpen = true;
    }

    public void OpenMicrosoftReauthenticationDialog(LauncherAccount account)
    {
        ResetAddAccountDialogState(clearOfflineName: true);
        AccountPendingMicrosoftReauthentication = account;
        AddAccountDialogStep = AccountDialogSteps.AddAccountMicrosoftReauthenticationPrompt;
        IsAddAccountDialogBusy = false;
        ResetMicrosoftLoginResultState(string.Format(
            Strings.Dialog_MicrosoftAccountExpiredMessageFormat,
            account.DisplayName));
        IsAddAccountDialogOpen = true;
        ReportStatus(Strings.Status_MicrosoftReauthenticationRequired);
    }

    public void BeginMicrosoftAccountReauthentication()
    {
        if (AccountPendingMicrosoftReauthentication is null)
            return;

        AddAccountDialogStep = AccountDialogSteps.AddAccountMicrosoftReauthentication;
        IsAddAccountDialogBusy = true;
        BeginMicrosoftAuthenticationCancellation();
        ResetMicrosoftLoginResultState(MicrosoftLoginActiveMessage);
        ReportStatus(Strings.Status_OpeningMicrosoftLogin);
    }

    public void CancelAddAccountDialog()
    {
        if (IsThirdPartyImportProgressStep)
        {
            thirdPartyImportCancellationTokenSource?.Cancel();
            return;
        }
        if (CanShowMicrosoftAuthenticationCancelButton)
        {
            microsoftAuthenticationCancellationTokenSource?.Cancel();
            return;
        }
        if (IsAddAccountDialogBusy)
            return;

        IsAddAccountDialogOpen = false;
        _ = ThirdParty.CancelEmailLoginAsync();
    }

    // DialogHost.Hide 在收起动画播放完毕后回调这里，是拖入添加后切换页面的最早安全时机。
    public void ResetAddAccountDialog()
    {
        var shouldNavigateToAccountPage = hasPendingDroppedThirdPartyNavigation;
        ResetAddAccountDialogState(clearOfflineName: true);
        if (shouldNavigateToAccountPage)
            DroppedThirdPartyAccountAdditionCompleted?.Invoke();
    }

    public void BackToAddAccountTypeStep()
    {
        if (IsAddAccountDialogBusy)
            return;

        ResetAddAccountDialogState(clearOfflineName: false);
    }

    public void BeginMicrosoftAccountLogin()
    {
        // 先进入 Busy 状态再启动外部浏览器登录，保证按钮状态和状态文案在异步边界前完成切换。
        AddAccountDialogStep = AccountDialogSteps.AddAccountMicrosoftLogin;
        IsAddAccountDialogBusy = true;
        BeginMicrosoftAuthenticationCancellation();
        ResetMicrosoftLoginResultState(MicrosoftLoginActiveMessage);
        ReportStatus(Strings.Status_OpeningMicrosoftLogin);
    }

    public async Task CompleteMicrosoftAccountLoginAsync()
    {
        var authenticationCancellation =
            microsoftAuthenticationCancellationTokenSource
            ?? BeginMicrosoftAuthenticationCancellation();
        try
        {
            // 服务返回的资料仍需在 UI 边界校验；缺少名称或 UUID 的响应不能进入账户集合。
            var account = await microsoftAccountService.LoginInteractivelyAsync(
                authenticationCancellation.Token);
            if (string.IsNullOrWhiteSpace(account.DisplayName) || string.IsNullOrWhiteSpace(account.Uuid))
            {
                var message = Strings.Status_LoginMissingProfile;
                ReportStatus(message);
                ShowMicrosoftLoginResult(false, message);
                return;
            }

            // Microsoft UUID 才是稳定身份。重复登录时选中已有账户并刷新顺序，不能创建名称相同的副本。
            var existing = accountList.Accounts.FirstOrDefault(item =>
                item.Account.IsMicrosoft && string.Equals(item.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                accountList.SelectItem(existing, persistSelection: false);
                await accountList.PersistAccountOrderAsync();
                var message = string.Format(Strings.Status_LoginAccountAlreadyAddedFormat, existing.DisplayName);
                ReportStatus(message);
                ShowMicrosoftLoginResult(true, message, alreadyAdded: true);
                return;
            }

            await accountList.AddAndSelectAsync(account);
            var addedMessage = string.Format(Strings.Status_LoginAccountAddedFormat, account.DisplayName);
            ReportStatus(addedMessage);
            ShowMicrosoftLoginResult(true, addedMessage);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Microsoft account login canceled.");
            var message = Strings.Status_LoginCanceled;
            ReportStatus(message);
            ShowMicrosoftLoginResult(false, message);
        }
        catch (MicrosoftAccountLoginException exception)
        {
            logger.LogWarning(
                "Microsoft account login failed. Reason={Reason}",
                exception.Reason);
            var message = exception.Reason switch
            {
                MicrosoftAccountLoginFailureReason.NotConfigured
                    => Strings.Status_MicrosoftLoginNotConfigured,
                MicrosoftAccountLoginFailureReason.ApplicationNotAuthorized
                    => Strings.Status_MicrosoftApplicationNotAuthorized,
                MicrosoftAccountLoginFailureReason.TimedOut
                    => Strings.Status_MicrosoftAuthenticationTimedOut,
                MicrosoftAccountLoginFailureReason.GameOwnershipRequired
                    => Strings.Status_MinecraftJavaOwnershipRequired,
                MicrosoftAccountLoginFailureReason.AuthenticationServerUnavailable
                    => Strings.Status_MicrosoftAuthenticationServerUnavailable,
                MicrosoftAccountLoginFailureReason.CredentialStorageFailed
                    => Strings.Status_MicrosoftCredentialStorageFailed,
                _ => Strings.Status_LoginFailed
            };
            ReportStatus(message);
            ShowMicrosoftLoginResult(false, message);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Microsoft account login failed. ErrorType={ErrorType}",
                exception.GetType().FullName);
            var message = Strings.Status_LoginFailed;
            ReportStatus(message);
            ShowMicrosoftLoginResult(false, message);
        }
        finally
        {
            // 所有成功、取消和异常路径都必须解除 Busy，否则对话框会永久失去关闭能力。
            CompleteMicrosoftAuthenticationCancellation(authenticationCancellation);
            IsAddAccountDialogBusy = false;
        }
    }

    public void CloseAddAccountDialogAfterMicrosoftResult()
    {
        if (IsAddAccountDialogBusy)
            return;

        IsAddAccountDialogOpen = false;
    }
}
