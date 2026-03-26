using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace AITeammate.Scripts;

public partial class AiTeammateCharacterSetupScreen
{
    private void BuildSessionUi(Control contentPanel, NCharacterSelectScreen? sourceCharacterSelectScreen)
    {
        if (contentPanel.GetNodeOrNull<Control>(SessionPanelNodeName) != null)
        {
            return;
        }

        Panel sessionPanel = new()
        {
            Name = SessionPanelNodeName,
            MouseFilter = MouseFilterEnum.Stop
        };
        sessionPanel.SetAnchorsPreset(LayoutPreset.BottomWide);
        sessionPanel.OffsetLeft = 42f;
        sessionPanel.OffsetTop = -238f;
        sessionPanel.OffsetRight = -42f;
        sessionPanel.OffsetBottom = -28f;
        sessionPanel.AddThemeStyleboxOverride("panel", CreatePanelStyle(SessionPanelColor, ContentPanelBorderColor, 3, 18));
        contentPanel.AddChild(sessionPanel);

        Label summaryLabel = new()
        {
            Name = SessionSummaryNodeName,
            Text = "Session panel",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        summaryLabel.SetAnchorsPreset(LayoutPreset.TopWide);
        summaryLabel.OffsetLeft = 22f;
        summaryLabel.OffsetTop = 18f;
        summaryLabel.OffsetRight = -250f;
        summaryLabel.OffsetBottom = 48f;
        summaryLabel.AddThemeFontSizeOverride("font_size", 21);
        sessionPanel.AddChild(summaryLabel);
        _sessionSummaryLabel = summaryLabel;

        Label hintLabel = new()
        {
            Name = SessionHintNodeName,
            Text = "Select the host character first to build a local teammate session.",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        hintLabel.SetAnchorsPreset(LayoutPreset.TopWide);
        hintLabel.OffsetLeft = 22f;
        hintLabel.OffsetTop = 50f;
        hintLabel.OffsetRight = -250f;
        hintLabel.OffsetBottom = 86f;
        hintLabel.Modulate = new Color(0.88f, 0.93f, 0.97f, 0.82f);
        sessionPanel.AddChild(hintLabel);
        _sessionHintLabel = hintLabel;

        CheckBox useTestMapToggle = new()
        {
            Name = TestMapToggleNodeName,
            Text = "Use Test Map",
            ButtonPressed = _useTestMap,
            FocusMode = FocusModeEnum.All,
            MouseFilter = MouseFilterEnum.Stop
        };
        useTestMapToggle.SetAnchorsPreset(LayoutPreset.TopLeft);
        useTestMapToggle.OffsetLeft = 22f;
        useTestMapToggle.OffsetTop = 94f;
        useTestMapToggle.OffsetRight = 220f;
        useTestMapToggle.OffsetBottom = 124f;
        useTestMapToggle.AddThemeFontSizeOverride("font_size", 18);
        useTestMapToggle.Toggled += OnUseTestMapToggled;
        sessionPanel.AddChild(useTestMapToggle);
        _useTestMapToggle = useTestMapToggle;

        if (sourceCharacterSelectScreen != null)
        {
            Node? sourceRemoteContainer = ((Node)sourceCharacterSelectScreen).GetNodeOrNull<Node>(RemotePlayerContainerNodeName);
            if (sourceRemoteContainer != null)
            {
                Node duplicate = sourceRemoteContainer.Duplicate(AiTeammateMenuUiFactory.DuplicateNodeFlags);
                if (duplicate is NRemoteLobbyPlayerContainer remoteLobbyPlayerContainer)
                {
                    ((Node)remoteLobbyPlayerContainer).Name = "AiTeammateRemotePlayerContainer";
                    remoteLobbyPlayerContainer.SetAnchorsPreset(LayoutPreset.FullRect);
                    remoteLobbyPlayerContainer.OffsetLeft = 18f;
                    remoteLobbyPlayerContainer.OffsetTop = 124f;
                    remoteLobbyPlayerContainer.OffsetRight = -18f;
                    remoteLobbyPlayerContainer.OffsetBottom = -18f;
                    sessionPanel.AddChild(remoteLobbyPlayerContainer);
                    _remoteLobbyPlayerContainer = remoteLobbyPlayerContainer;
                    HideInviteControls(remoteLobbyPlayerContainer);
                }
            }
        }

        Button proceedButton = new()
        {
            Name = ProceedButtonNodeName,
            Text = "Proceed",
            Disabled = true,
            FocusMode = FocusModeEnum.All,
            MouseFilter = MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(180f, 52f)
        };
        proceedButton.SetAnchorsPreset(LayoutPreset.BottomRight);
        proceedButton.OffsetLeft = -202f;
        proceedButton.OffsetTop = -78f;
        proceedButton.OffsetRight = -22f;
        proceedButton.OffsetBottom = -22f;
        proceedButton.AddThemeStyleboxOverride("normal", CreatePanelStyle(ProceedButtonColor, Colors.White, 2, 16));
        proceedButton.AddThemeStyleboxOverride("hover", CreatePanelStyle(ProceedButtonHoverColor, Colors.White, 2, 16));
        proceedButton.AddThemeStyleboxOverride("pressed", CreatePanelStyle(ProceedButtonColor.Darkened(0.15f), Colors.White, 2, 16));
        proceedButton.AddThemeStyleboxOverride("disabled", CreatePanelStyle(ProceedButtonDisabledColor, new Color(1f, 1f, 1f, 0.6f), 2, 16));
        proceedButton.AddThemeColorOverride("font_color", Colors.White);
        proceedButton.AddThemeColorOverride("font_hover_color", Colors.White);
        proceedButton.AddThemeColorOverride("font_pressed_color", Colors.White);
        proceedButton.AddThemeColorOverride("font_disabled_color", new Color(1f, 1f, 1f, 0.75f));
        proceedButton.AddThemeFontSizeOverride("font_size", 20);
        proceedButton.Pressed += OnProceedPressed;
        sessionPanel.AddChild(proceedButton);
        _proceedButton = proceedButton;

        RefreshSessionStatus();
        RefreshProceedButtonState();
    }

    private void RefreshSessionFromSelections()
    {
        AiTeammateSessionState? sessionState = AiTeammateSessionState.CreateFromSelections(_slotSelections, _useTestMap);
        _sessionState = sessionState;
        AiTeammateSessionRegistry.SetCurrent(sessionState);

        if (sessionState == null)
        {
            CleanupActiveLobby(disconnectSession: true, clearSessionRegistry: false);
            RefreshAscensionPanelState();
            RefreshSessionStatus();
            RefreshProceedButtonState();
            return;
        }

        EnsureLobbyCreated(sessionState);
        SyncLobbyToSession(sessionState);
        RefreshAscensionPanelState();
        RefreshSessionStatus();
        RefreshProceedButtonState();
    }

    private void EnsureLobbyCreated(AiTeammateSessionState sessionState)
    {
        if (_lobby != null && _loopbackService != null)
        {
            return;
        }

        CleanupActiveLobby(disconnectSession: true, clearSessionRegistry: false);

        _loopbackService = new AiTeammateLoopbackHostGameService(sessionState.HostPlayerId);
        _lobby = new StartRunLobby(GameMode.Standard, _loopbackService, this, MaxPlayerCount);
        _lobby.AddLocalHostPlayer(SaveManager.Instance.GenerateUnlockStateFromProgress(), SaveManager.Instance.Progress.MaxMultiplayerAscension);
        if (_selectedAscensionLevel > 0)
        {
            _lobby.SyncAscensionChange(Math.Clamp(_selectedAscensionLevel, 0, _lobby.MaxAscension));
        }

        if (_remoteLobbyPlayerContainer != null)
        {
            _remoteLobbyPlayerContainer.Initialize(_lobby, displayLocalPlayer: true);
            HideInviteControls(_remoteLobbyPlayerContainer);
        }
    }

    private void SyncLobbyToSession(AiTeammateSessionState sessionState)
    {
        if (_lobby == null || _loopbackService == null)
        {
            return;
        }

        HashSet<ulong> targetPlayerIds = sessionState.Participants.Select((participant) => participant.PlayerId).ToHashSet();

        for (int index = _lobby.Players.Count - 1; index >= 0; index--)
        {
            LobbyPlayer existingPlayer = _lobby.Players[index];
            if (targetPlayerIds.Contains(existingPlayer.id))
            {
                continue;
            }

            _lobby.Players.RemoveAt(index);
            _lobby.InputSynchronizer.OnPlayerDisconnected(existingPlayer.id);
            RemotePlayerDisconnected(existingPlayer);
        }

        foreach (AiTeammateSessionParticipant participant in sessionState.Participants)
        {
            if (_lobby.Players.All((player) => player.id != participant.PlayerId))
            {
                AddParticipantToLobby(participant);
            }

            UpdateLobbyPlayer(participant.PlayerId, participant.Character, isReady: false);
        }

        InvokeLobbyUpdateMaxAscension();
        int desiredAscension = Math.Clamp(_selectedAscensionLevel, 0, _lobby.MaxAscension);
        if (_lobby.Ascension != desiredAscension)
        {
            _lobby.SyncAscensionChange(desiredAscension);
        }

        if (_remoteLobbyPlayerContainer != null)
        {
            _remoteLobbyPlayerContainer.Initialize(_lobby, displayLocalPlayer: true);
            HideInviteControls(_remoteLobbyPlayerContainer);
        }

        _loopbackService.SetCurrentSenderId(sessionState.HostPlayerId);
    }

    private void AddParticipantToLobby(AiTeammateSessionParticipant participant)
    {
        if (_lobby == null || _loopbackService == null)
        {
            return;
        }

        _loopbackService.SetCurrentSenderId(participant.PlayerId);
        _lobby.AddLocalHostPlayerInternal(
            SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable(),
            SaveManager.Instance.Progress.MaxMultiplayerAscension);
        _loopbackService.SetCurrentSenderId(_sessionState?.HostPlayerId ?? participant.PlayerId);
    }

    private void UpdateLobbyPlayer(ulong playerId, CharacterModel character, bool isReady)
    {
        if (_lobby == null)
        {
            return;
        }

        int playerIndex = _lobby.Players.FindIndex((player) => player.id == playerId);
        if (playerIndex < 0)
        {
            return;
        }

        LobbyPlayer lobbyPlayer = _lobby.Players[playerIndex];
        lobbyPlayer.character = character;
        lobbyPlayer.isReady = isReady;
        lobbyPlayer.unlockState = SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable();
        lobbyPlayer.maxMultiplayerAscensionUnlocked = SaveManager.Instance.Progress.MaxMultiplayerAscension;
        _lobby.Players[playerIndex] = lobbyPlayer;
        PlayerChanged(lobbyPlayer);
    }

    private void InvokeLobbyUpdateMaxAscension()
    {
        if (_lobby == null)
        {
            return;
        }

        AccessTools.Method(typeof(StartRunLobby), "UpdateMaxMultiplayerAscension")?.Invoke(_lobby, Array.Empty<object>());
    }

    private void RefreshSessionStatus()
    {
        if (_sessionSummaryLabel == null || _sessionHintLabel == null)
        {
            return;
        }

        if (_sessionState == null)
        {
            _sessionSummaryLabel.Text = "Session panel";
            _sessionHintLabel.Text = "Select the host character first to build a local teammate session.";
            _remoteLobbyPlayerContainer?.Cleanup();
            return;
        }

        _sessionSummaryLabel.Text = $"Session participants: {_sessionState.Participants.Count}";
        _sessionHintLabel.Text = _sessionState.AiCount > 0
            ? $"Host plus {_sessionState.AiCount} local fake remote teammate(s) now share a real StartRunLobby model.{(_sessionState.UseTestMap ? " Test map enabled." : string.Empty)}"
            : "Host is ready in the session model. Add at least one AI teammate to enable Proceed.";
    }

    private void RefreshProceedButtonState()
    {
        if (_proceedButton == null)
        {
            return;
        }

        bool canProceed = !_isStartingRun &&
                          _sessionState != null &&
                          _sessionState.HasHost &&
                          _sessionState.AiCount > 0 &&
                          _lobby != null;
        _proceedButton.Disabled = !canProceed;
    }

    private void OnProceedPressed()
    {
        if (_sessionState == null || _lobby == null || _loopbackService == null || _isStartingRun)
        {
            return;
        }

        Log.Info($"[AITeammate] Proceed pressed. players={_sessionState.Participants.Count}, ai={_sessionState.AiCount}");

        foreach (AiTeammateSessionParticipant participant in _sessionState.Participants.Where((candidate) => !candidate.IsHost))
        {
            UpdateLobbyPlayer(participant.PlayerId, participant.Character, isReady: true);
        }

        _loopbackService.SetCurrentSenderId(_sessionState.HostPlayerId);
        _lobby.SetReady(ready: true);
        RefreshProceedButtonState();
    }

    private void OnUseTestMapToggled(bool enabled)
    {
        _useTestMap = enabled;
        RefreshSessionFromSelections();
    }

    private async System.Threading.Tasks.Task StartLobbyRunAsync(string seed, List<ActModel> acts, IReadOnlyList<ModifierModel> modifiers)
    {
        if (_lobby == null)
        {
            return;
        }

        _isStartingRun = true;
        RefreshProceedButtonState();

        try
        {
            CharacterModel hostCharacter = _lobby.LocalPlayer.character;
            if (NGame.Instance == null)
            {
                throw new InvalidOperationException("NGame.Instance was null while trying to start the AI teammate run.");
            }

            if (_sessionState != null)
            {
                AiTeammateSaveSupport.MarkCurrentProfile(_sessionState);
            }

            Log.Info($"[AITeammate] Starting local AI teammate run. host={hostCharacter.Id.Entry}, players={_lobby.Players.Count}, seed={seed}");
            NAudioManager.Instance?.StopMusic();
            SfxCmd.Play(hostCharacter.CharacterTransitionSfx);
            await NGame.Instance.Transition.FadeOut(0.8f, hostCharacter.CharacterSelectTransitionPath);
            await NGame.Instance.StartNewMultiplayerRun(_lobby, shouldSave: true, acts, modifiers, seed, _lobby.Ascension);
            CleanupActiveLobby(disconnectSession: false, clearSessionRegistry: false);
        }
        catch (Exception ex)
        {
            _isStartingRun = false;
            RefreshProceedButtonState();
            Log.Error($"[AITeammate] Failed to start AI teammate run: {ex}");
        }
    }

    private void CleanupActiveLobby(bool disconnectSession, bool clearSessionRegistry)
    {
        _remoteLobbyPlayerContainer?.Cleanup();
        HideInviteControls(_remoteLobbyPlayerContainer);

        if (_lobby != null)
        {
            _lobby.CleanUp(disconnectSession);
            _lobby = null;
        }

        _loopbackService = null;

        if (clearSessionRegistry)
        {
            AiTeammateSessionRegistry.SetCurrent(null);
        }
    }

    public void PlayerConnected(LobbyPlayer player)
    {
        _remoteLobbyPlayerContainer?.OnPlayerConnected(player);
        HideInviteControls(_remoteLobbyPlayerContainer);
    }

    public void PlayerChanged(LobbyPlayer player)
    {
        _remoteLobbyPlayerContainer?.OnPlayerChanged(player);
        HideInviteControls(_remoteLobbyPlayerContainer);
    }

    public void AscensionChanged()
    {
        if (_lobby != null)
        {
            _selectedAscensionLevel = _lobby.Ascension;
        }

        RefreshAscensionPanelState();
    }

    public void SeedChanged()
    {
    }

    public void ModifiersChanged()
    {
    }

    public void MaxAscensionChanged()
    {
        if (_lobby != null)
        {
            _selectedAscensionLevel = Math.Clamp(_selectedAscensionLevel, 0, _lobby.MaxAscension);
        }

        RefreshAscensionPanelState();
    }

    public void RemotePlayerDisconnected(LobbyPlayer player)
    {
        _remoteLobbyPlayerContainer?.OnPlayerDisconnected(player);
        HideInviteControls(_remoteLobbyPlayerContainer);
    }

    public void BeginRun(string seed, List<ActModel> acts, IReadOnlyList<ModifierModel> modifiers)
    {
        TaskHelper.RunSafely(StartLobbyRunAsync(seed, acts, modifiers));
    }

    public void LocalPlayerDisconnected(NetErrorInfo info)
    {
        if (info.SelfInitiated && info.GetReason() == NetError.Quit)
        {
            return;
        }

        Log.Warn($"[AITeammate] Local player disconnected before run start: {info.GetReason()}");
    }

    private static void HideInviteControls(Node? root)
    {
        if (root == null)
        {
            return;
        }

        foreach (Node child in EnumerateDescendants(root))
        {
            if (child is not CanvasItem canvasItem)
            {
                continue;
            }

            string name = child.Name.ToString();
            if (name.Contains("Invite", StringComparison.OrdinalIgnoreCase))
            {
                canvasItem.Visible = false;
            }
        }
    }

    private static IEnumerable<Node> EnumerateDescendants(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            yield return child;
            foreach (Node nestedChild in EnumerateDescendants(child))
            {
                yield return nestedChild;
            }
        }
    }
}
