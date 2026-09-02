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

namespace Launcher.App.ViewModels.Account;

public sealed partial class AccountDialogViewModel
{
    public async Task ConfirmAddAccountDialogAsync(string? thirdPartyPassword = null)
    {
        if (IsAddAccountDialogBusy)
            return;

        var password = thirdPartyPassword ?? string.Empty;
        if (await TryHandleAddAccountResultStepAsync(password))
            return;

        if (await TryHandleThirdPartyReauthenticationAsync(password))
            return;

        if (await TryHandleThirdPartyCredentialsAsync(password))
            return;

        if (SelectedAccountTypeOption is null)
            return;

        if (TryAdvanceAccountTypeStep())
            return;

        await AddOfflineAccountAsync();
    }
}
