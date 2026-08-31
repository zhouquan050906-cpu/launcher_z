/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Multiplayer;
using Launcher.Application.Services;

namespace Launcher.Tests.ViewModels.Multiplayer;

public sealed class MultiplayerPageViewModelTests
{
    [Fact]
    public async Task CreateLobbyStartsDetectionWithoutDiscoveringWorldFirst()
    {
        var completion = new TaskCompletionSource<MultiplayerLobbySnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var context = Create((_, _) => completion.Task);

        var operation = context.ViewModel.CreateLobbyCommand.ExecuteAsync(null);

        Assert.True(context.ViewModel.IsCreatingLobby);
        Assert.True(context.ViewModel.IsLanWorldDetectionDialogOpen);
        Assert.False(context.ViewModel.CreateLobbyCommand.CanExecute(null));
        Assert.Equal(1, context.LobbyService.CreateHostCount);

        completion.SetResult(CreateSnapshot());
        await operation;
    }

    [Fact]
    public async Task CancelDetectionCancelsCreationAndStaysOnSetupSilently()
    {
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var context = Create(async (_, cancellationToken) =>
        {
            using var registration = cancellationToken.Register(
                () => cancellationObserved.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation delay unexpectedly completed.");
        });

        var operation = context.ViewModel.CreateLobbyCommand.ExecuteAsync(null);
        context.ViewModel.CancelLobbyDetectionCommand.Execute(null);

        Assert.False(context.ViewModel.IsLanWorldDetectionDialogOpen);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await operation;

        Assert.True(context.LobbyService.LastCreateCancellationToken.IsCancellationRequested);
        Assert.False(context.ViewModel.IsCreatingLobby);
        Assert.Equal(MultiplayerCreateLobbyStep.Setup, context.ViewModel.CreateLobbyStep);
        Assert.False(context.ViewModel.IsLobbyStep);
        Assert.Null(context.Messages.StatusMessage);
        Assert.Null(context.Messages.FloatingMessage);
    }

    [Fact]
    public async Task HostOkClosesDetectionAndShowsRoomSnapshot()
    {
        var snapshot = CreateSnapshot();
        var context = Create((_, _) => Task.FromResult(snapshot));

        await context.ViewModel.CreateLobbyCommand.ExecuteAsync(null);

        Assert.False(context.ViewModel.IsLanWorldDetectionDialogOpen);
        Assert.False(context.ViewModel.IsCreatingLobby);
        Assert.True(context.ViewModel.IsLobbyStep);
        Assert.True(context.ViewModel.IsLobbyHost);
        Assert.Equal(snapshot.RoomCode, context.ViewModel.RoomCode);
        Assert.Equal("Host player", context.ViewModel.LobbyOwnerName);
        Assert.Collection(
            context.ViewModel.LobbyPlayers,
            player =>
            {
                Assert.Equal("Host player", player.DisplayName);
                Assert.True(player.IsHost);
            },
            player =>
            {
                Assert.Equal("Guest player", player.DisplayName);
                Assert.False(player.IsHost);
            });
    }

    [Theory]
    [InlineData(
        MultiplayerLobbyCreationFailure.MinecraftWorldUnavailable,
        nameof(Strings.Multiplayer_Create_WorldUnavailable))]
    [InlineData(
        MultiplayerLobbyCreationFailure.TerracottaBusy,
        nameof(Strings.Multiplayer_Create_TerracottaBusy))]
    public async Task KnownCreationFailureClosesDetectionAndAllowsRetry(
        MultiplayerLobbyCreationFailure failure,
        string expectedResourceName)
    {
        var context = Create((_, _) => Task.FromException<MultiplayerLobbySnapshot>(
            new MultiplayerLobbyCreationException(failure, "Test failure.")));

        await context.ViewModel.CreateLobbyCommand.ExecuteAsync(null);

        var expectedMessage = expectedResourceName switch
        {
            nameof(Strings.Multiplayer_Create_WorldUnavailable) =>
                Strings.Multiplayer_Create_WorldUnavailable,
            nameof(Strings.Multiplayer_Create_TerracottaBusy) =>
                Strings.Multiplayer_Create_TerracottaBusy,
            _ => throw new ArgumentOutOfRangeException(nameof(expectedResourceName))
        };
        AssertCreationFailure(context, expectedMessage);
    }

    [Fact]
    public async Task UnexpectedCreationFailureClosesDetectionAndAllowsRetry()
    {
        var context = Create((_, _) => Task.FromException<MultiplayerLobbySnapshot>(
            new IOException("Test failure.")));

        await context.ViewModel.CreateLobbyCommand.ExecuteAsync(null);

        AssertCreationFailure(context, Strings.Multiplayer_Create_LobbyFailed);
    }

    private static void AssertCreationFailure(TestContext context, string expectedMessage)
    {
        Assert.False(context.ViewModel.IsLanWorldDetectionDialogOpen);
        Assert.False(context.ViewModel.IsCreatingLobby);
        Assert.True(context.ViewModel.CreateLobbyCommand.CanExecute(null));
        Assert.Equal(MultiplayerCreateLobbyStep.Setup, context.ViewModel.CreateLobbyStep);
        Assert.Equal(expectedMessage, context.Messages.StatusMessage);
        Assert.Equal(expectedMessage, context.Messages.FloatingMessage);
    }

    private static TestContext Create(
        Func<string, CancellationToken, Task<MultiplayerLobbySnapshot>> createHost)
    {
        var lobbyService = new RecordingLobbyService(createHost);
        var messages = new RecordingMessageService();
        var viewModel = new MultiplayerPageViewModel(
            lobbyService,
            new EmptyClipboardService(),
            new ImmediateDispatcher(),
            messages,
            messages);
        return new TestContext(viewModel, lobbyService, messages);
    }

    private static MultiplayerLobbySnapshot CreateSnapshot() => new(
        "U/1234",
        MultiplayerLobbyState.Active,
        [
            new MultiplayerLobbyPlayer(
                "Host player",
                "host-machine",
                "Windows",
                MultiplayerLobbyPlayerKind.Host,
                12,
                true),
            new MultiplayerLobbyPlayer(
                "Guest player",
                "guest-machine",
                "Linux",
                MultiplayerLobbyPlayerKind.Guest,
                34)
        ]);

    private sealed record TestContext(
        MultiplayerPageViewModel ViewModel,
        RecordingLobbyService LobbyService,
        RecordingMessageService Messages);

    private sealed class RecordingLobbyService(
        Func<string, CancellationToken, Task<MultiplayerLobbySnapshot>> createHost)
        : IMultiplayerLobbyService
    {
        public MultiplayerLobbySnapshot? Current { get; private set; }

        public int CreateHostCount { get; private set; }

        public CancellationToken LastCreateCancellationToken { get; private set; }

#pragma warning disable CS0067
        public event Action<MultiplayerLobbySnapshot>? SnapshotChanged;
        public event Action<MultiplayerLobbyStopped>? Stopped;
#pragma warning restore CS0067

        public async Task<MultiplayerLobbySnapshot> CreateHostAsync(
            string hostName,
            CancellationToken cancellationToken = default)
        {
            CreateHostCount++;
            LastCreateCancellationToken = cancellationToken;
            Current = await createHost(hostName, cancellationToken);
            return Current;
        }

        public Task<MultiplayerLobbySnapshot> JoinAsync(
            string roomCode,
            string playerName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StopAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class EmptyClipboardService : IClipboardService
    {
        public Task<bool> CopyTextAsync(
            string text,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public bool HasAccess => true;

        public void Post(Action action) => action();

        public void PostAfterTransition(Action action) => action();

        public Task PostAfterTransitionAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public void Invoke(Action action) => action();

        public Task InvokeAsync(Func<Task> action) => action();
    }

    private sealed class RecordingMessageService : IStatusService, IFloatingMessageService
    {
        public event Action<string>? MessageReported;
        public event Action<string>? MessageRequested;

        public string? StatusMessage { get; private set; }

        public string? FloatingMessage { get; private set; }

        public void Report(string message)
        {
            StatusMessage = message;
            MessageReported?.Invoke(message);
        }

        public void Show(string message)
        {
            FloatingMessage = message;
            MessageRequested?.Invoke(message);
        }
    }
}
