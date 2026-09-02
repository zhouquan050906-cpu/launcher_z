/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Accounts;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Account;

public sealed partial class AccountDialogViewModel
{
    private async Task<bool> TryHandleAddAccountResultStepAsync(string thirdPartyPassword)
    {
        if (IsMicrosoftLoginResultStep)
        {
            CloseAddAccountDialogAfterMicrosoftResult();
            return true;
        }

        if (IsThirdPartyImportResultStep)
        {
            IsAddAccountDialogOpen = false;
            await ThirdParty.CancelEmailLoginAsync();
            if (thirdPartySuccessfulAccounts.Count > 0)
                CompleteDroppedThirdPartyAccountAddition();
            return true;
        }

        if (!IsThirdPartyProfileSelectionStep)
            return false;

        await ImportThirdPartyProfilesAsync(
            ThirdParty.Profiles.Where(profile => profile.IsSelected).ToArray(),
            thirdPartyPassword);
        return true;
    }

    private async Task<bool> TryHandleThirdPartyReauthenticationAsync(string thirdPartyPassword)
    {
        if (!IsThirdPartyReauthenticationStep
            || AccountPendingThirdPartyReauthentication is not { } pendingAccount)
        {
            return false;
        }

        IsAddAccountDialogBusy = true;
        try
        {
            var authenticated = await ThirdParty.ReauthenticateAsync(pendingAccount, thirdPartyPassword);
            if (authenticated is not null)
            {
                await accountList.ReplaceSelectedAccountAndPersistAsync(pendingAccount, authenticated);
                IsAddAccountDialogOpen = false;
            }
        }
        finally
        {
            IsAddAccountDialogBusy = false;
        }

        return true;
    }

    private bool TryAdvanceAccountTypeStep()
    {
        if (!IsAccountTypeStep)
            return false;

        if (SelectedAccountTypeOption!.Kind is AccountTypeKinds.Offline)
            AddAccountDialogStep = AccountDialogSteps.AddAccountOfflineName;
        else if (SelectedAccountTypeOption.Kind is AccountTypeKinds.ThirdParty)
            AddAccountDialogStep = AccountDialogSteps.AddAccountThirdPartyCredentials;

        return true;
    }

    private async Task<bool> TryHandleThirdPartyCredentialsAsync(string thirdPartyPassword)
    {
        if (!IsThirdPartyCredentialsStep)
            return false;

        IsAddAccountDialogBusy = true;
        try
        {
            if (ThirdParty.IsEmailIdentifier)
            {
                if (await ThirdParty.BeginEmailLoginAsync(thirdPartyPassword))
                    AddAccountDialogStep = AccountDialogSteps.AddAccountThirdPartyProfileSelection;
            }
            else if (await ThirdParty.LoginAsync(thirdPartyPassword))
            {
                IsAddAccountDialogOpen = false;
                CompleteDroppedThirdPartyAccountAddition();
            }
        }
        finally
        {
            IsAddAccountDialogBusy = false;
        }

        return true;
    }

    private async Task AddOfflineAccountAsync()
    {
        var accountName = NewOfflineAccountName.Trim();
        if (!AccountNameValidator.IsValid(accountName))
        {
            IsNewOfflineAccountNameInvalid = true;
            ReportStatus(AccountNameValidator.ValidationMessage);
            return;
        }

        var account = new LauncherAccount
        {
            Id = $"offline-{Guid.NewGuid():N}",
            DisplayName = accountName,
            Uuid = offlineUuidService.CreateUuid(accountName, OfflineUuidGenerationMode.Standard),
            OfflineUuidGenerationMode = OfflineUuidGenerationMode.Standard,
            Kind = LauncherAccountKind.Offline
        };

        await accountList.AddAndSelectAsync(account);
        IsAddAccountDialogOpen = false;
        ReportStatus(string.Format(Strings.Status_OfflineAccountAddedFormat, accountName));
    }
}
