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
    public async Task<bool> CompleteMicrosoftAccountReauthenticationAsync()
    {
        var account = AccountPendingMicrosoftReauthentication;
        if (account is null)
            return false;

        BeginMicrosoftAccountReauthentication();
        var authenticationCancellation =
            microsoftAuthenticationCancellationTokenSource
            ?? BeginMicrosoftAuthenticationCancellation();
        try
        {
            var refreshed = await microsoftAccountService.ReauthenticateInteractivelyAsync(
                account,
                authenticationCancellation.Token);
            await accountList.ReplaceSelectedAccountAndPersistAsync(account, refreshed);
            AccountPendingMicrosoftReauthentication = refreshed;
            ReportStatus(Strings.Status_MicrosoftReauthenticationSuccessful);
            IsAddAccountDialogOpen = false;
            return true;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Microsoft account reauthentication canceled. AccountId={AccountId}", account.Id);
            ShowMicrosoftReauthenticationFailure(Strings.Status_LoginCanceled);
            return false;
        }
        catch (MicrosoftAccountReauthenticationException exception)
        {
            logger.LogWarning(
                "Microsoft account reauthentication failed. AccountId={AccountId} Reason={Reason}",
                account.Id,
                exception.Reason);
            var message = exception.Reason switch
            {
                MicrosoftAccountReauthenticationFailureReason.NotConfigured => Strings.Status_MicrosoftLoginNotConfigured,
                MicrosoftAccountReauthenticationFailureReason.ApplicationNotAuthorized => Strings.Status_MicrosoftApplicationNotAuthorized,
                MicrosoftAccountReauthenticationFailureReason.TimedOut => Strings.Status_MicrosoftAuthenticationTimedOut,
                MicrosoftAccountReauthenticationFailureReason.GameOwnershipRequired => Strings.Status_MinecraftJavaOwnershipRequired,
                MicrosoftAccountReauthenticationFailureReason.AuthenticationServerUnavailable => Strings.Status_MicrosoftAuthenticationServerUnavailable,
                MicrosoftAccountReauthenticationFailureReason.AccountMismatch => Strings.Status_MicrosoftReauthenticationAccountMismatch,
                MicrosoftAccountReauthenticationFailureReason.CredentialStorageFailed => Strings.Status_MicrosoftCredentialStorageFailed,
                _ => Strings.Status_LoginFailed
            };
            ShowMicrosoftReauthenticationFailure(message);
            return false;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Microsoft account reauthentication failed. AccountId={AccountId} ErrorType={ErrorType}",
                account.Id,
                exception.GetType().FullName);
            ShowMicrosoftReauthenticationFailure(Strings.Status_LoginFailed);
            return false;
        }
        finally
        {
            CompleteMicrosoftAuthenticationCancellation(authenticationCancellation);
            IsAddAccountDialogBusy = false;
        }
    }

    private async Task SelectLastSuccessfulThirdPartyAccountAsync()
    {
        if (thirdPartySuccessfulAccounts.Count == 0)
            return;
        var last = accountList.FindAccount(thirdPartySuccessfulAccounts[^1].Id);
        if (last is null)
            return;
        accountList.SelectAccount(last, persistSelection: false);
        await accountList.PersistAccountOrderAsync();
    }
}
