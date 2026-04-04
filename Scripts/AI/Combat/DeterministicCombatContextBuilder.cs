using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;

namespace AITeammate.Scripts;

internal sealed class DeterministicCombatContextBuilder
{
    private readonly EnemyReactiveMetadataRepository _enemyReactiveMetadataRepository = EnemyReactiveMetadataRepository.Shared;

    private readonly ICardResolver _cardResolver = new CardResolver(
        CardCatalogRepository.Shared,
        new CardDefinitionRepository(),
        new RunCardStateStore(),
        new CombatCardStateStore());

    public DeterministicCombatContext? Build(string actorId, IReadOnlyList<AiLegalActionOption> legalActions)
    {
        if (!ulong.TryParse(actorId, out ulong parsedActorId))
        {
            return null;
        }

        Player? player = RunManager.Instance.DebugOnlyGetState()?.GetPlayer(parsedActorId);
        if (player?.Creature?.CombatState == null || player.PlayerCombatState == null)
        {
            return null;
        }

        AbstractRoom? currentRoom = RunManager.Instance.DebugOnlyGetState()?.CurrentRoom;
        string roomTypeName = currentRoom?.GetType().Name ?? "UnknownRoom";

        Dictionary<string, ResolvedCardView> handCardsByInstanceId = PileType.Hand.GetPile(player).Cards
            .GroupBy(GetCardInstanceId)
            .ToDictionary(
                group => group.Key,
                group => _cardResolver.Resolve(group.First(), group.Key),
                StringComparer.Ordinal);

        Dictionary<string, DeterministicEnemyState> enemiesById = new(StringComparer.Ordinal);
        Dictionary<string, DeterministicAllyState> alliesById = new(StringComparer.Ordinal);
        int incomingDamage = 0;
        foreach (Creature enemy in player.Creature.CombatState.HittableEnemies)
        {
            int enemyDamage = EstimateIncomingDamage(enemy, player.Creature);
            string enemyId = GetTargetId(enemy);
            IReadOnlyList<DeterministicEnemyReactiveState> reactiveStates = BuildReactiveStates(enemyId, enemy);
            enemiesById[enemyId] = new DeterministicEnemyState
            {
                Id = enemyId,
                Creature = enemy,
                ReactiveStates = reactiveStates,
                IncomingDamage = enemyDamage
            };
            incomingDamage += enemyDamage;
        }

        foreach (Creature ally in player.Creature.CombatState.PlayerCreatures.Where(static creature => creature.IsAlive && creature.Player != null))
        {
            int allyIncomingDamage = 0;
            foreach (Creature enemy in player.Creature.CombatState.HittableEnemies)
            {
                allyIncomingDamage += EstimateIncomingDamage(enemy, ally);
            }

            string allyId = GetTargetId(ally);
            alliesById[allyId] = new DeterministicAllyState
            {
                Id = allyId,
                Player = ally.Player!,
                Creature = ally,
                IsActor = ally.Player!.NetId == player.NetId,
                IncomingDamage = Math.Max(0, allyIncomingDamage)
            };
        }

        Dictionary<string, int> actorPowerAmounts = player.Creature.Powers
            .Where(static power => power.IsVisible)
            .GroupBy(power => power.Id.Entry, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(power => power.DisplayAmount), StringComparer.Ordinal);

        HashSet<string> actorRelicIds = player.Relics
            .Select(static relic => relic.Id.Entry.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        DeterministicCombatContext context = new()
        {
            Actor = player,
            LegalActions = legalActions,
            HandCardsByInstanceId = handCardsByInstanceId,
            EnemiesById = enemiesById,
            AlliesById = alliesById,
            ActorPowerAmounts = actorPowerAmounts,
            ActorRelicIds = actorRelicIds,
            CombatConfig = AiCharacterCombatConfigLoader.LoadForPlayer(player),
            RoomTypeName = roomTypeName,
            IsEliteCombat = roomTypeName.Contains("Elite", StringComparison.OrdinalIgnoreCase),
            IsBossCombat = roomTypeName.Contains("Boss", StringComparison.OrdinalIgnoreCase),
            IncomingDamage = incomingDamage
        };

        LogReactiveContext(context);
        return context;
    }

    private static int EstimateIncomingDamage(Creature enemy, Creature target)
    {
        if (enemy.Monster?.NextMove?.Intents == null)
        {
            return 0;
        }

        int total = 0;
        foreach (AttackIntent intent in enemy.Monster.NextMove.Intents.OfType<AttackIntent>())
        {
            total += intent.GetTotalDamage([target], enemy);
        }

        return Math.Max(total, 0);
    }

    private static string GetCardInstanceId(CardModel card)
    {
        return NetCombatCardDb.Instance.TryGetCardId(card, out uint cardId)
            ? $"combat_{cardId}"
            : card.Id.Entry.Replace(':', '_').Replace('/', '_').Replace(' ', '_');
    }

    private static string GetTargetId(Creature target)
    {
        if (target.Player != null)
        {
            return $"player_{target.Player.NetId}";
        }

        return $"creature_{target.CombatId?.ToString() ?? target.Name.Replace(' ', '_')}";
    }

    private IReadOnlyList<DeterministicEnemyReactiveState> BuildReactiveStates(string enemyId, Creature enemy)
    {
        List<DeterministicEnemyReactiveState> reactiveStates = [];
        List<string> filteredVisiblePowerIds = [];
        foreach (PowerModel power in enemy.Powers)
        {
            if (!_enemyReactiveMetadataRepository.TryGet(power, out EnemyReactiveMetadata? metadata) || metadata == null)
            {
                if (GetSafePowerVisibility(power))
                {
                    filteredVisiblePowerIds.Add(power.Id.Entry);
                }

                continue;
            }

            int currentAmount = GetSafePowerAmount(power);
            int currentDisplayAmount = GetSafePowerDisplayAmount(power, currentAmount);
            List<DeterministicEnemyReactiveEffectEntry> effects = metadata.Effects
                .Select(descriptor => new DeterministicEnemyReactiveEffectEntry
                {
                    EnemyId = enemyId,
                    PowerId = metadata.PowerId,
                    Descriptor = descriptor,
                    CurrentPowerAmount = currentAmount,
                    CurrentDisplayAmount = currentDisplayAmount
                })
                .ToList();

            reactiveStates.Add(new DeterministicEnemyReactiveState
            {
                EnemyId = enemyId,
                Power = power,
                Metadata = metadata,
                Effects = effects,
                PowerId = metadata.PowerId,
                PowerTypeName = power.GetType().FullName ?? power.GetType().Name,
                CurrentAmount = currentAmount,
                CurrentDisplayAmount = currentDisplayAmount,
                IsVisible = GetSafePowerVisibility(power)
            });
        }

        if (filteredVisiblePowerIds.Count > 0)
        {
            Log.Debug($"[AITeammate][ReactiveContext] enemy={enemyId} filteredVisiblePowers=[{string.Join(", ", filteredVisiblePowerIds.OrderBy(static id => id, StringComparer.Ordinal))}] retainedReactivePowers={reactiveStates.Count}");
        }

        return reactiveStates;
    }

    private static int GetSafePowerAmount(PowerModel power)
    {
        try
        {
            return power.Amount;
        }
        catch (Exception ex)
        {
            Log.Warn($"[AITeammate][ReactiveContext] Failed to read Amount for power={power.Id.Entry}: {ex.Message}");
            return 0;
        }
    }

    private static int GetSafePowerDisplayAmount(PowerModel power, int fallbackAmount)
    {
        try
        {
            return power.DisplayAmount;
        }
        catch (Exception ex)
        {
            Log.Warn($"[AITeammate][ReactiveContext] Failed to read DisplayAmount for power={power.Id.Entry}: {ex.Message}");
            return fallbackAmount;
        }
    }

    private static bool GetSafePowerVisibility(PowerModel power)
    {
        try
        {
            return power.IsVisible;
        }
        catch (Exception ex)
        {
            Log.Warn($"[AITeammate][ReactiveContext] Failed to read IsVisible for power={power.Id.Entry}: {ex.Message}");
            return true;
        }
    }

    private static void LogReactiveContext(DeterministicCombatContext context)
    {
        int enemyCount = context.EnemiesById.Count;
        int enemyWithReactiveCount = context.EnemiesById.Values.Count(static enemy => enemy.HasReactiveEffects);
        int reactiveStateCount = context.EnemiesById.Values.Sum(static enemy => enemy.ReactiveStates.Count);
        int reactiveEffectCount = context.EnemiesById.Values.Sum(enemy => enemy.ReactiveStates.Sum(static state => state.Effects.Count));

        Log.Info($"[AITeammate][ReactiveContext] actor={context.Actor.NetId} enemies={enemyCount} enemiesWithReactive={enemyWithReactiveCount} reactiveStates={reactiveStateCount} reactiveEffects={reactiveEffectCount}");

        foreach (DeterministicEnemyState enemy in context.EnemiesById.Values.OrderBy(static enemy => enemy.Id, StringComparer.Ordinal))
        {
            if (!enemy.HasReactiveEffects)
            {
                continue;
            }

            Log.Info($"[AITeammate][ReactiveContext] enemy={enemy.Id} name={enemy.Creature.LogName} powers={enemy.ReactiveStates.Count} hp={enemy.CurrentHp} block={enemy.Block} incoming={enemy.IncomingDamage}");
            foreach (DeterministicEnemyReactiveState reactiveState in enemy.ReactiveStates.OrderBy(static state => state.PowerId, StringComparer.Ordinal))
            {
                Log.Info($"[AITeammate][ReactiveContext] enemy={enemy.Id} power={reactiveState.PowerId} amount={reactiveState.CurrentAmount} displayAmount={reactiveState.CurrentDisplayAmount} visible={reactiveState.IsVisible} metadataRuntime={reactiveState.Metadata.RequiresRuntimeResolution} metadataPartial={reactiveState.Metadata.HasPartialUnknowns} effects={reactiveState.Effects.Count}");
                foreach (DeterministicEnemyReactiveEffectEntry effect in reactiveState.Effects)
                {
                    string payload = effect.AppliedPowerId ?? effect.AfflictionId ?? effect.AddedCardId ?? effect.AppliedKeyword ?? string.Empty;
                    Log.Info($"[AITeammate][ReactiveContext] enemy={enemy.Id} power={reactiveState.PowerId} trigger={effect.TriggerKind} outcome={effect.OutcomeKind} scope={effect.TargetScope} blockability={effect.Blockability} staticMagnitude={effect.StaticMagnitude?.ToString() ?? "?"} staticKind={effect.StaticMagnitudeKind} currentAmount={effect.CurrentPowerAmount} currentDisplayAmount={effect.CurrentDisplayAmount} runtime={effect.RequiresRuntimeResolution} payload={payload}");
                }
            }
        }
    }
}
