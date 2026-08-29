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

using Launcher.Application.Accounts;
namespace Launcher.Application.Accounts;

public interface IAccountStore
{
    Task<AccountStoreSnapshot> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 只读取本地账户记录用于首屏，不访问凭据库、不做迁移也不写回。
    /// </summary>
    Task<AccountStoreSnapshot> LoadCachedAsync(CancellationToken cancellationToken = default);

    Task SaveOrderAsync(
        string? selectedAccountId,
        IEnumerable<LauncherAccount> accounts,
        CancellationToken cancellationToken = default);
}
