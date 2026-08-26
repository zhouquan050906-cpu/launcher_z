/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Resources;
using Launcher.App.ViewModels.Shell;
using Launcher.Application.Services;

namespace Launcher.Tests.ViewModels.Shell;

public sealed class MinecraftDirectoryStartupRecoveryDialogViewModelTests
{
    [Fact]
    public void NoRecoveryDoesNotOpenDialog()
    {
        var viewModel = new MinecraftDirectoryStartupRecoveryDialogViewModel();

        viewModel.Prime(null);
        viewModel.ShowPending();

        Assert.False(viewModel.IsOpen);
        Assert.Empty(viewModel.Message);
    }

    [Fact]
    public void SwitchedDirectoryRecoveryOpensOnceAndCanBeClosed()
    {
        const string invalidDirectory = @"C:\Missing\.minecraft";
        const string selectedDirectory = @"D:\Games\.minecraft";
        var recovery = new MinecraftDirectoryStartupRecoveryResult(
            invalidDirectory,
            selectedDirectory,
            UsedDefaultDirectory: false,
            CreatedDefaultDirectory: false);
        var viewModel = new MinecraftDirectoryStartupRecoveryDialogViewModel();

        viewModel.Prime(recovery);
        viewModel.ShowPending();

        Assert.True(viewModel.IsOpen);
        Assert.Equal(
            string.Format(
                Strings.Dialog_MinecraftDirectoryStartupSwitchedMessageFormat,
                invalidDirectory,
                selectedDirectory),
            viewModel.Message);

        viewModel.CloseCommand.Execute(null);
        viewModel.ShowPending();

        Assert.False(viewModel.IsOpen);
    }

    [Fact]
    public void DefaultDirectoryRecoveryUsesDefaultMessage()
    {
        const string invalidDirectory = @"C:\Missing\.minecraft";
        const string selectedDirectory = @"C:\Launcher\.minecraft";
        var recovery = new MinecraftDirectoryStartupRecoveryResult(
            invalidDirectory,
            selectedDirectory,
            UsedDefaultDirectory: true,
            CreatedDefaultDirectory: true);
        var viewModel = new MinecraftDirectoryStartupRecoveryDialogViewModel();

        viewModel.Prime(recovery);
        viewModel.ShowPending();

        Assert.True(viewModel.IsOpen);
        Assert.Equal(
            string.Format(
                Strings.Dialog_MinecraftDirectoryStartupDefaultMessageFormat,
                invalidDirectory,
                selectedDirectory),
            viewModel.Message);
    }
}
