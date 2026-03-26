using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace AITeammate.Scripts;

internal sealed partial class AiTeammateDummyController
{
    private static readonly TimeSpan IdleTickInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ActionTickInterval = TimeSpan.FromMilliseconds(400);

    private DateTime _nextDecisionAtUtc = DateTime.MinValue;
    private bool _isExecutingAction;
    private string? _lastDeduplicationKey;

    public AiTeammateDummyController(int slotIndex, ulong playerId, CharacterModel character)
    {
        SlotIndex = slotIndex;
        PlayerId = playerId;
        Character = character;
    }

    public int SlotIndex { get; }

    public ulong PlayerId { get; }

    public CharacterModel Character { get; }

    public void Tick()
    {
        if (_isExecutingAction || DateTime.UtcNow < _nextDecisionAtUtc)
        {
            return;
        }

        IReadOnlyList<AiTeammateAvailableAction> actions = DiscoverAvailableActions();
        AiTeammateAvailableAction? action = ChooseDefaultAction(actions);
        if (action == null)
        {
            _nextDecisionAtUtc = DateTime.UtcNow + IdleTickInterval;
            return;
        }

        _isExecutingAction = true;
        _nextDecisionAtUtc = DateTime.UtcNow + ActionTickInterval;
        TaskHelper.RunSafely(ExecuteChosenActionAsync(action));
    }

    public IReadOnlyList<AiTeammateAvailableAction> DiscoverAvailableActions()
    {
        if (!TryGetControlledPlayer(out Player player, out RunState runState))
        {
            return Array.Empty<AiTeammateAvailableAction>();
        }

        if (!player.Creature.IsAlive)
        {
            return Array.Empty<AiTeammateAvailableAction>();
        }

        if (IsCombatDecisionWindow(player))
        {
            return DiscoverCombatActions(player);
        }

        if (runState.CurrentRoom is MegaCrit.Sts2.Core.Rooms.EventRoom)
        {
            return DiscoverEventActions(player);
        }

        if (runState.CurrentRoom is MegaCrit.Sts2.Core.Rooms.RestSiteRoom)
        {
            return DiscoverRestSiteActions(player);
        }

        return Array.Empty<AiTeammateAvailableAction>();
    }

    public static bool IsAiPlayer(Player? player)
    {
        return player != null &&
               AiTeammateSessionRegistry.Current?.AiControllers.ContainsKey(player.NetId) == true;
    }

    public static bool TryGetControllerFor(ulong playerId, out AiTeammateDummyController controller)
    {
        if (AiTeammateSessionRegistry.Current is { } session &&
            session.AiControllers.TryGetValue(playerId, out AiTeammateDummyController? foundController))
        {
            controller = foundController;
            return true;
        }

        controller = null!;
        return false;
    }

    private static bool IsCombatDecisionWindow(Player player)
    {
        return MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsInProgress &&
               MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsPlayPhase &&
               !MegaCrit.Sts2.Core.Combat.CombatManager.Instance.PlayerActionsDisabled &&
               player.Creature.CombatState?.CurrentSide == player.Creature.Side;
    }

    private AiTeammateAvailableAction? ChooseDefaultAction(IReadOnlyList<AiTeammateAvailableAction> actions)
    {
        return actions.FirstOrDefault(action => action.DeduplicationKey == null || action.DeduplicationKey != _lastDeduplicationKey);
    }

    private async Task ExecuteChosenActionAsync(AiTeammateAvailableAction action)
    {
        try
        {
            await action.ExecuteAsync();
            if (!string.IsNullOrEmpty(action.DeduplicationKey))
            {
                _lastDeduplicationKey = action.DeduplicationKey;
            }
        }
        catch (Exception exception)
        {
            Log.Warn($"[AITeammate] Dummy controller {PlayerId} failed to execute {action.Kind}: {exception}");
        }
        finally
        {
            _isExecutingAction = false;
        }
    }

    private bool TryGetControlledPlayer(out Player player, out RunState runState)
    {
        runState = RunManager.Instance.DebugOnlyGetState()!;
        player = runState?.GetPlayer(PlayerId)!;
        return runState != null && player != null;
    }
}
