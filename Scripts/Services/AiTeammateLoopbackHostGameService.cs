using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Quality;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace AITeammate.Scripts;

internal sealed class AiTeammateLoopbackHostGameService : INetHostGameService
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    private readonly List<NetClientData> _connectedPeers = new();
    private ulong _currentSenderId;

    public AiTeammateLoopbackHostGameService(ulong hostPlayerId)
    {
        _currentSenderId = hostPlayerId;
        IsConnected = true;
        Log.Info($"[AITeammate] Created local loopback host service. sender={_currentSenderId}");
    }

    public ulong NetId => _currentSenderId;

    public bool IsConnected { get; private set; }

    public bool IsGameLoading { get; private set; }

    public NetGameType Type => NetGameType.Host;

    public PlatformType Platform => PlatformType.None;

    public IReadOnlyList<NetClientData> ConnectedPeers => _connectedPeers;

    public NetHost? NetHost => null;

    public event Action<NetErrorInfo>? Disconnected;

    public event Action<ulong>? ClientConnected;

    public event Action<ulong, NetErrorInfo>? ClientDisconnected;

    public void SetCurrentSenderId(ulong playerId)
    {
        if (_currentSenderId == playerId)
        {
            return;
        }

        Log.Info($"[AITeammate] Loopback sender changed: {_currentSenderId} -> {playerId}");
        _currentSenderId = playerId;
    }

    public void SendMessage<T>(T message, ulong playerId) where T : INetMessage
    {
        AlignSenderWithLocalContext();
        Log.Info($"[AITeammate] Loopback directed message: {typeof(T).Name}, sender={_currentSenderId}, target={playerId}");
    }

    public void SendMessage<T>(T message) where T : INetMessage
    {
        AlignSenderWithLocalContext();
        Log.Info($"[AITeammate] Loopback broadcast message: {typeof(T).Name}, sender={_currentSenderId}");
        TryDispatchSyntheticPlayerSync(message);
    }

    public void RegisterMessageHandler<T>(MessageHandlerDelegate<T> messageHandlerDelegate) where T : INetMessage
    {
        Type messageType = typeof(T);
        if (!_handlers.TryGetValue(messageType, out List<Delegate>? handlers))
        {
            handlers = new List<Delegate>();
            _handlers[messageType] = handlers;
        }

        handlers.Add(messageHandlerDelegate);
    }

    public void UnregisterMessageHandler<T>(MessageHandlerDelegate<T> messageHandlerDelegate) where T : INetMessage
    {
        Type messageType = typeof(T);
        if (_handlers.TryGetValue(messageType, out List<Delegate>? handlers))
        {
            handlers.Remove(messageHandlerDelegate);
        }
    }

    public void DispatchLoopback<T>(T message, ulong senderId) where T : INetMessage
    {
        Type messageType = typeof(T);
        if (!_handlers.TryGetValue(messageType, out List<Delegate>? handlers) || handlers.Count == 0)
        {
            return;
        }

        foreach (Delegate handler in handlers)
        {
            if (handler is MessageHandlerDelegate<T> typedHandler)
            {
                typedHandler(message, senderId);
            }
        }
    }

    public void Update()
    {
    }

    public void Disconnect(NetError reason, bool now = false)
    {
        if (!IsConnected)
        {
            return;
        }

        IsConnected = false;
        Log.Info($"[AITeammate] Loopback disconnected. reason={reason}, now={now}");
        Disconnected?.Invoke(new NetErrorInfo(reason, selfInitiated: true));
    }

    public ConnectionStats? GetStatsForPeer(ulong peerId)
    {
        return null;
    }

    public void SetGameLoading(bool isLoading)
    {
        IsGameLoading = isLoading;
    }

    public string? GetRawLobbyIdentifier()
    {
        return "ai-teammate-local-lobby";
    }

    public void DisconnectClient(ulong peerId, NetError reason, bool now = false)
    {
        ClientDisconnected?.Invoke(peerId, new NetErrorInfo(reason, selfInitiated: true));
    }

    public void SetPeerReadyForBroadcasting(ulong peerId)
    {
        ClientConnected?.Invoke(peerId);
    }

    private void TryDispatchSyntheticPlayerSync<T>(T message) where T : INetMessage
    {
        AiTeammateSessionState? session = AiTeammateSessionRegistry.Current;
        if (session == null || _currentSenderId != session.HostPlayerId || message is not SyncPlayerDataMessage)
        {
            return;
        }

        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
        {
            return;
        }

        foreach (var player in runState.Players.Where((candidate) => candidate.NetId != session.HostPlayerId))
        {
            SyncPlayerDataMessage syntheticMessage = new()
            {
                player = player.ToSerializable()
            };

            DispatchLoopback(syntheticMessage, player.NetId);
        }
    }

    private void AlignSenderWithLocalContext()
    {
        if (LocalContext.NetId.HasValue && LocalContext.NetId.Value != _currentSenderId)
        {
            SetCurrentSenderId(LocalContext.NetId.Value);
        }
    }
}
