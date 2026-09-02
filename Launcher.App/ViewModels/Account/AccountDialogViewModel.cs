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

/// <summary>
/// 协调账户新增、删除和重命名对话框的多步骤 UI 状态，并把实际账户操作委托给账户服务与列表模型。
/// </summary>
public sealed partial class AccountDialogViewModel : ObservableObject
{
    // 图标与步骤字符串共同构成对话框状态机；集中定义可避免各分支用不同值表达同一状态。
    private const string DialogBusyIcon = "\uE895";
    private const string DialogSuccessIcon = "\uE73E";
    private const string DialogFailureIcon = "\uE783";
    private const string RenameInputIcon = "\uE70F";

    private static string MicrosoftLoginInitialMessage => Strings.Status_OpeningMicrosoftLogin;
    private static string MicrosoftLoginActiveMessage => Strings.Status_LoginMicrosoftActive;

    // AccountListViewModel 是当前账户集合和持久化顺序的唯一 UI 所有者，本类只编排对话框流程。
    private readonly AccountListViewModel accountList;
    private readonly IMicrosoftAccountService microsoftAccountService;
    private readonly IThirdPartyAccountService thirdPartyAccountService;
    private readonly IOfflineAccountUuidService offlineUuidService;
    private readonly IStatusService statusService;
    private readonly ILogger<AccountDialogViewModel> logger;

    // 新增账户流程：类型选择 -> 离线名称或 Microsoft 登录 -> 结果。Busy 时禁止关闭和回退，
    // 防止交互登录尚未返回时 UI 被重置，随后又把过期结果写回新一轮对话框。
    [ObservableProperty]
    private bool isAddAccountDialogOpen;

    [ObservableProperty]
    private bool isAddAccountDialogBusy;

    [ObservableProperty]
    private AccountTypeOption? selectedAccountTypeOption;

    [ObservableProperty]
    private string addAccountDialogStep = AccountDialogSteps.AddAccountType;

    [ObservableProperty]
    private string newOfflineAccountName = string.Empty;

    [ObservableProperty]
    private bool isNewOfflineAccountNameInvalid;

    [ObservableProperty]
    private string microsoftLoginMessage = MicrosoftLoginInitialMessage;

    [ObservableProperty]
    private string microsoftLoginIcon = DialogBusyIcon;

    [ObservableProperty]
    private bool isMicrosoftLoginSuccessful;

    [ObservableProperty]
    private bool isMicrosoftAccountAlreadyAdded;

    [ObservableProperty]
    private LauncherAccount? accountPendingMicrosoftReauthentication;

    [ObservableProperty]
    private LauncherAccount? accountPendingThirdPartyReauthentication;

    [ObservableProperty] private string thirdPartyImportCurrentProfileName = string.Empty;
    [ObservableProperty] private int thirdPartyImportCompletedCount;
    [ObservableProperty] private int thirdPartyImportTotalCount;
    [ObservableProperty] private int thirdPartyImportFailedCount;
    private readonly List<ThirdPartyProfileOptionViewModel> thirdPartyFailedProfiles = [];
    private readonly List<LauncherAccount> thirdPartySuccessfulAccounts = [];
    private CancellationTokenSource? thirdPartyImportCancellationTokenSource;
    private CancellationTokenSource? microsoftAuthenticationCancellationTokenSource;
    private bool isDirectThirdPartyAddAccountEntry;
    private bool navigateToAccountAfterThirdPartyAddition;
    private bool hasPendingDroppedThirdPartyNavigation;

    public event Action? DroppedThirdPartyAccountAdditionCompleted;

    // 删除对话框只保留待删除对象；确认后会立即清空引用，避免重复确认触发两次删除。
    [ObservableProperty]
    private bool isDeleteAccountDialogOpen;

    [ObservableProperty]
    private LauncherAccount? accountPendingDelete;

    // 重命名流程同时服务离线与 Microsoft 账户。两者共享校验和结果页，但底层身份更新规则不同。
    [ObservableProperty]
    private bool isRenameAccountDialogOpen;

    [ObservableProperty]
    private bool isRenameAccountDialogBusy;

    [ObservableProperty]
    private LauncherAccount? accountPendingRename;

    [ObservableProperty]
    private string renameAccountDialogStep = AccountDialogSteps.RenameInput;

    [ObservableProperty]
    private string renameAccountName = string.Empty;

    [ObservableProperty]
    private bool isRenameAccountNameInvalid;

    [ObservableProperty]
    private bool isRenameAccountSuccessful;

    [ObservableProperty]
    private string renameAccountMessage = string.Empty;

    [ObservableProperty]
    private string renameAccountErrorCodeMessage = string.Empty;

    [ObservableProperty]
    private string renameAccountIcon = RenameInputIcon;

    public AccountDialogViewModel(
        AccountListViewModel accountList,
        IMicrosoftAccountService microsoftAccountService,
        IThirdPartyAccountService thirdPartyAccountService,
        IOfflineAccountUuidService offlineUuidService,
        IStatusService statusService,
        ILogger<AccountDialogViewModel>? logger = null)
    {
        this.accountList = accountList;
        this.microsoftAccountService = microsoftAccountService;
        this.thirdPartyAccountService = thirdPartyAccountService;
        this.offlineUuidService = offlineUuidService;
        this.statusService = statusService;
        this.logger = logger ?? NullLogger<AccountDialogViewModel>.Instance;
        ThirdParty = new ThirdPartyAccountDialogViewModel(accountList, thirdPartyAccountService, this.logger);
        ThirdParty.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ThirdPartyAccountDialogViewModel.CanConfirm)
                or nameof(ThirdPartyAccountDialogViewModel.HasSelectedProfiles))
                OnPropertyChanged(nameof(CanConfirmAddAccountDialog));
            if (e.PropertyName == nameof(ThirdPartyAccountDialogViewModel.CanSelectAllProfiles))
                OnPropertyChanged(nameof(CanSelectAllThirdPartyProfiles));
        };
    }

    public ObservableCollection<AccountTypeOption> AccountTypeOptions { get; } = new(AccountTypeOptionFactory.Create());
    public ThirdPartyAccountDialogViewModel ThirdParty { get; }

    public bool IsAccountTypeStep => AddAccountDialogStep == AccountDialogSteps.AddAccountType;
    public bool IsOfflineNameStep => AddAccountDialogStep == AccountDialogSteps.AddAccountOfflineName;
    public bool IsThirdPartyCredentialsStep => AddAccountDialogStep == AccountDialogSteps.AddAccountThirdPartyCredentials;
    public bool IsThirdPartyReauthenticationStep => AddAccountDialogStep == AccountDialogSteps.AddAccountThirdPartyReauthentication;
    public bool IsThirdPartyFormStep => IsThirdPartyCredentialsStep || IsThirdPartyReauthenticationStep;
    public bool IsThirdPartyProfileSelectionStep => AddAccountDialogStep == AccountDialogSteps.AddAccountThirdPartyProfileSelection;
    public bool IsThirdPartyImportProgressStep => AddAccountDialogStep == AccountDialogSteps.AddAccountThirdPartyImportProgress;
    public bool IsThirdPartyImportResultStep => AddAccountDialogStep == AccountDialogSteps.AddAccountThirdPartyImportResult;
    public bool IsMicrosoftLoginStep => AddAccountDialogStep == AccountDialogSteps.AddAccountMicrosoftLogin;
    public bool IsMicrosoftLoginResultStep => AddAccountDialogStep == AccountDialogSteps.AddAccountMicrosoftResult;
    public bool IsMicrosoftReauthenticationPromptStep =>
        AddAccountDialogStep == AccountDialogSteps.AddAccountMicrosoftReauthenticationPrompt;
    public bool IsMicrosoftReauthenticationStep => AddAccountDialogStep == AccountDialogSteps.AddAccountMicrosoftReauthentication;
    public bool IsMicrosoftReauthenticationResultStep => AddAccountDialogStep == AccountDialogSteps.AddAccountMicrosoftReauthenticationResult;
    public bool IsMicrosoftReauthenticationMode => AccountPendingMicrosoftReauthentication is not null;
    public bool IsMicrosoftStatusStep => IsMicrosoftLoginStep || IsMicrosoftLoginResultStep
        || IsMicrosoftReauthenticationPromptStep
        || IsMicrosoftReauthenticationStep || IsMicrosoftReauthenticationResultStep;
    public bool CanShowAddAccountBackButton => !IsAddAccountDialogBusy
        && (IsOfflineNameStep
            || (IsThirdPartyCredentialsStep && !isDirectThirdPartyAddAccountEntry)
            || IsMicrosoftLoginStep);
    public bool CanShowAddAccountCancelButton => !IsAddAccountDialogBusy
        && (!IsMicrosoftLoginResultStep || IsMicrosoftReauthenticationMode);
    public bool CanShowMicrosoftAuthenticationCancelButton =>
        IsAddAccountDialogBusy
        && (IsMicrosoftLoginStep || IsMicrosoftReauthenticationStep);
    public bool IsAddAccountFooterEnabled => !IsAddAccountDialogBusy;
    public bool CanConfirmAddAccountDialog => !IsAddAccountDialogBusy
        && (IsMicrosoftLoginResultStep
            || IsMicrosoftReauthenticationPromptStep
            || IsMicrosoftReauthenticationResultStep
            || IsOfflineNameStep
            || (IsThirdPartyFormStep && ThirdParty.CanConfirm)
            || (IsThirdPartyProfileSelectionStep && ThirdParty.HasSelectedProfiles)
            || IsThirdPartyImportResultStep
            || (IsAccountTypeStep && SelectedAccountTypeOption is not null));
    public bool IsMicrosoftAccountTypeSelected => IsAccountTypeStep && SelectedAccountTypeOption?.Kind is AccountTypeKinds.Microsoft;

    public bool IsRenameAccountInputStep => RenameAccountDialogStep == AccountDialogSteps.RenameInput;
    public bool IsRenameAccountStatusStep => RenameAccountDialogStep == AccountDialogSteps.RenameStatus;
    public bool IsRenameAccountResultStep => RenameAccountDialogStep == AccountDialogSteps.RenameResult;
    public bool IsRenameAccountMessageStep => IsRenameAccountStatusStep || IsRenameAccountResultStep;
    public bool IsRenameMicrosoftAccount => AccountPendingRename?.IsMicrosoft == true;
    public bool CanShowRenameAccountCancelButton => !IsRenameAccountDialogBusy && IsRenameAccountInputStep;
    public bool CanConfirmRenameAccountDialog => !IsRenameAccountDialogBusy
        && (IsRenameAccountResultStep || (IsRenameAccountInputStep && !string.IsNullOrWhiteSpace(RenameAccountName)));
    public bool HasRenameAccountErrorCode => !string.IsNullOrWhiteSpace(RenameAccountErrorCodeMessage);

    public string? MicrosoftLoginIconKey =>
        IsMicrosoftLoginStep || IsMicrosoftReauthenticationStep
            ? "general/general_external-web"
            : IsMicrosoftReauthenticationPromptStep
                ? "general/general_attention"
                : IsMicrosoftLoginResultStep || IsMicrosoftReauthenticationResultStep
                    ? IsMicrosoftLoginSuccessful ? "general/general_passed" : "general/general_attention"
                    : null;

    public string? RenameAccountIconKey => IsRenameAccountResultStep
        ? IsRenameAccountSuccessful ? "general/general_passed" : "general/general_attention"
        : null;

    public string RenameAccountDialogTitle =>
        AccountDialogText.GetRenameTitle(RenameAccountDialogStep, IsRenameAccountSuccessful);

    public string RenameAccountDialogSubtitle =>
        AccountDialogText.GetRenameSubtitle(RenameAccountDialogStep, IsRenameMicrosoftAccount);

    public string AddAccountDialogTitle => AddAccountDialogStep switch
    {
        AccountDialogSteps.AddAccountMicrosoftReauthenticationPrompt => Strings.Dialog_MicrosoftAccountExpiredTitle,
        AccountDialogSteps.AddAccountMicrosoftReauthentication => Strings.Dialog_ReauthenticateMicrosoftAccountTitle,
        AccountDialogSteps.AddAccountMicrosoftReauthenticationResult => Strings.Dialog_ReauthenticateMicrosoftAccountTitle,
        AccountDialogSteps.AddAccountThirdPartyReauthentication => Strings.Dialog_ReauthenticateThirdPartyAccountTitle,
        AccountDialogSteps.AddAccountThirdPartyProfileSelection => Strings.Dialog_ThirdPartyProfileSelectionTitle,
        AccountDialogSteps.AddAccountThirdPartyImportProgress => Strings.Dialog_ThirdPartyImportProgressTitle,
        AccountDialogSteps.AddAccountThirdPartyImportResult => Strings.Dialog_ThirdPartyImportResultTitle,
        _ => AccountDialogText.GetAddTitle(
            AddAccountDialogStep,
            IsMicrosoftAccountAlreadyAdded,
            IsMicrosoftLoginSuccessful)
    };

    public string AddAccountDialogSubtitle => AddAccountDialogStep switch
    {
        AccountDialogSteps.AddAccountMicrosoftReauthenticationPrompt => Strings.Dialog_MicrosoftAccountExpiredSubtitle,
        AccountDialogSteps.AddAccountMicrosoftReauthentication => Strings.Dialog_ReauthenticateMicrosoftAccountSubtitle,
        AccountDialogSteps.AddAccountMicrosoftReauthenticationResult => Strings.Dialog_ReauthenticateMicrosoftAccountSubtitle,
        AccountDialogSteps.AddAccountThirdPartyReauthentication => Strings.Dialog_ReauthenticateThirdPartyAccountSubtitle,
        AccountDialogSteps.AddAccountThirdPartyProfileSelection => Strings.Dialog_ThirdPartyProfileSelectionSubtitle,
        AccountDialogSteps.AddAccountThirdPartyImportProgress => Strings.Dialog_ThirdPartyImportProgressSubtitle,
        AccountDialogSteps.AddAccountThirdPartyImportResult => Strings.Dialog_ThirdPartyImportResultSubtitle,
        _ => AccountDialogText.GetAddSubtitle(AddAccountDialogStep)
    };

    public bool IsThirdPartyIdentityReadOnly => IsThirdPartyReauthenticationStep;
    public bool CanSelectAllThirdPartyProfiles => IsThirdPartyProfileSelectionStep && ThirdParty.CanSelectAllProfiles;
    public bool CanShowStandardAddAccountFooter =>
        !IsThirdPartyImportProgressStep
        && !IsThirdPartyImportResultStep
        && !CanShowMicrosoftAuthenticationCancelButton;
    public string AddAccountConfirmButtonText => IsMicrosoftReauthenticationResultStep
        ? Strings.Retry_Button
        : Strings.Confirm_Button;
    public string ThirdPartyImportProgressText => string.Format(
        Strings.Dialog_ThirdPartyImportProgressFormat,
        ThirdPartyImportCompletedCount,
        ThirdPartyImportTotalCount);
    public string ThirdPartyImportFailureText => string.Format(
        Strings.Dialog_ThirdPartyImportFailureFormat,
        ThirdPartyImportFailedCount);

}
