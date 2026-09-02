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
    private void NotifyAddAccountDialogStepPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsAccountTypeStep));
        OnPropertyChanged(nameof(IsOfflineNameStep));
        OnPropertyChanged(nameof(IsThirdPartyCredentialsStep));
        OnPropertyChanged(nameof(IsThirdPartyReauthenticationStep));
        OnPropertyChanged(nameof(IsThirdPartyFormStep));
        OnPropertyChanged(nameof(IsThirdPartyProfileSelectionStep));
        OnPropertyChanged(nameof(IsThirdPartyImportProgressStep));
        OnPropertyChanged(nameof(IsThirdPartyImportResultStep));
        OnPropertyChanged(nameof(CanSelectAllThirdPartyProfiles));
        OnPropertyChanged(nameof(CanShowStandardAddAccountFooter));
        OnPropertyChanged(nameof(IsThirdPartyIdentityReadOnly));
        OnPropertyChanged(nameof(IsMicrosoftLoginStep));
        OnPropertyChanged(nameof(IsMicrosoftLoginResultStep));
        OnPropertyChanged(nameof(IsMicrosoftReauthenticationPromptStep));
        OnPropertyChanged(nameof(IsMicrosoftReauthenticationStep));
        OnPropertyChanged(nameof(IsMicrosoftReauthenticationResultStep));
        OnPropertyChanged(nameof(IsMicrosoftStatusStep));
        OnPropertyChanged(nameof(MicrosoftLoginIconKey));
        OnPropertyChanged(nameof(AddAccountConfirmButtonText));
        NotifyAddAccountDialogActionPropertiesChanged();
        OnPropertyChanged(nameof(IsMicrosoftAccountTypeSelected));
        OnPropertyChanged(nameof(AddAccountDialogTitle));
        OnPropertyChanged(nameof(AddAccountDialogSubtitle));
    }

    private void NotifyAddAccountDialogActionPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanShowAddAccountBackButton));
        OnPropertyChanged(nameof(CanShowAddAccountCancelButton));
        OnPropertyChanged(nameof(CanShowMicrosoftAuthenticationCancelButton));
        OnPropertyChanged(nameof(CanShowStandardAddAccountFooter));
        OnPropertyChanged(nameof(IsAddAccountFooterEnabled));
        OnPropertyChanged(nameof(CanConfirmAddAccountDialog));
    }

    private void NotifyRenameAccountDialogStepPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsRenameAccountInputStep));
        OnPropertyChanged(nameof(IsRenameAccountStatusStep));
        OnPropertyChanged(nameof(IsRenameAccountResultStep));
        OnPropertyChanged(nameof(IsRenameAccountMessageStep));
        OnPropertyChanged(nameof(RenameAccountIconKey));
        NotifyRenameAccountDialogActionPropertiesChanged();
        OnPropertyChanged(nameof(RenameAccountDialogTitle));
        OnPropertyChanged(nameof(RenameAccountDialogSubtitle));
    }

    private void NotifyRenameAccountDialogActionPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanShowRenameAccountCancelButton));
        OnPropertyChanged(nameof(CanConfirmRenameAccountDialog));
    }

    private void ResetAddAccountDialogState(bool clearOfflineName)
    {
        CancelAndDisposeMicrosoftAuthentication();
        thirdPartyImportCancellationTokenSource?.Cancel();
        thirdPartyImportCancellationTokenSource = null;
        thirdPartyFailedProfiles.Clear();
        thirdPartySuccessfulAccounts.Clear();
        ThirdPartyImportCurrentProfileName = string.Empty;
        ThirdPartyImportCompletedCount = 0;
        ThirdPartyImportTotalCount = 0;
        ThirdPartyImportFailedCount = 0;
        AccountPendingThirdPartyReauthentication = null;
        AccountPendingMicrosoftReauthentication = null;
        isDirectThirdPartyAddAccountEntry = false;
        navigateToAccountAfterThirdPartyAddition = false;
        hasPendingDroppedThirdPartyNavigation = false;
        AddAccountDialogStep = AccountDialogSteps.AddAccountType;
        if (clearOfflineName)
        {
            NewOfflineAccountName = string.Empty;
            ThirdParty.Reset();
        }

        IsNewOfflineAccountNameInvalid = false;
        IsAddAccountDialogBusy = false;
        ResetMicrosoftLoginResultState();
        SelectedAccountTypeOption = null;
    }

    private CancellationTokenSource BeginMicrosoftAuthenticationCancellation()
    {
        CancelAndDisposeMicrosoftAuthentication();
        microsoftAuthenticationCancellationTokenSource = new CancellationTokenSource();
        return microsoftAuthenticationCancellationTokenSource;
    }

    private void CompleteMicrosoftAuthenticationCancellation(
        CancellationTokenSource cancellationTokenSource)
    {
        if (!ReferenceEquals(
                microsoftAuthenticationCancellationTokenSource,
                cancellationTokenSource))
        {
            return;
        }

        microsoftAuthenticationCancellationTokenSource.Dispose();
        microsoftAuthenticationCancellationTokenSource = null;
    }

    private void CancelAndDisposeMicrosoftAuthentication()
    {
        microsoftAuthenticationCancellationTokenSource?.Cancel();
        microsoftAuthenticationCancellationTokenSource?.Dispose();
        microsoftAuthenticationCancellationTokenSource = null;
    }

    private void ResetMicrosoftLoginResultState(string? message = null)
    {
        IsMicrosoftLoginSuccessful = false;
        IsMicrosoftAccountAlreadyAdded = false;
        MicrosoftLoginIcon = DialogBusyIcon;
        MicrosoftLoginMessage = message ?? MicrosoftLoginInitialMessage;
    }

    private void ResetRenameAccountDialogState(string accountName)
    {
        IsRenameAccountDialogBusy = false;
        RenameAccountDialogStep = AccountDialogSteps.RenameInput;
        RenameAccountName = accountName;
        IsRenameAccountNameInvalid = false;
        IsRenameAccountSuccessful = false;
        RenameAccountIcon = RenameInputIcon;
        RenameAccountMessage = string.Empty;
        RenameAccountErrorCodeMessage = string.Empty;
    }

    private void ShowMicrosoftLoginResult(bool isSuccess, string message, bool alreadyAdded = false)
    {
        IsMicrosoftLoginSuccessful = isSuccess;
        IsMicrosoftAccountAlreadyAdded = alreadyAdded;
        MicrosoftLoginIcon = isSuccess ? DialogSuccessIcon : DialogFailureIcon;
        MicrosoftLoginMessage = message;
        AddAccountDialogStep = AccountDialogSteps.AddAccountMicrosoftResult;
    }

    private void ShowMicrosoftReauthenticationFailure(string message)
    {
        IsMicrosoftLoginSuccessful = false;
        IsMicrosoftAccountAlreadyAdded = false;
        MicrosoftLoginIcon = DialogFailureIcon;
        MicrosoftLoginMessage = message;
        AddAccountDialogStep = AccountDialogSteps.AddAccountMicrosoftReauthenticationResult;
        ReportStatus(message);
    }

    private void ShowRenameAccountResult(bool isSuccess, string message, string errorCodeMessage = "")
    {
        IsRenameAccountSuccessful = isSuccess;
        RenameAccountIcon = isSuccess ? DialogSuccessIcon : DialogFailureIcon;
        RenameAccountMessage = message;
        RenameAccountErrorCodeMessage = isSuccess ? string.Empty : errorCodeMessage;
        RenameAccountDialogStep = AccountDialogSteps.RenameResult;
    }

    private static string GetRenameFailureMessage(Exception exception)
    {
        return exception is MicrosoftAccountNameChangeException nameChangeException
            ? nameChangeException.Reason switch
            {
                MicrosoftAccountNameChangeFailureReason.DuplicateName => Strings.Status_AccountRenameFailedDuplicateName,
                MicrosoftAccountNameChangeFailureReason.NotAllowed => Strings.Status_AccountRenameFailedNotAllowed,
                MicrosoftAccountNameChangeFailureReason.InvalidName => Strings.Status_AccountRenameFailedInvalidName,
                _ => Strings.Status_AccountRenameFailed
            }
            : Strings.Status_AccountRenameFailed;
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }
}
