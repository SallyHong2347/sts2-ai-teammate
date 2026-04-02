using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;

namespace AITeammate.Scripts;

internal sealed class DeterministicCombatContextBuilder
{
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
        int incomingDamage = 0;
        foreach (Creature enemy in player.Creature.CombatState.HittableEnemies)
        {
            int enemyDamage = EstimateIncomingDamage(enemy, player.Creature);
            string enemyId = GetTargetId(enemy);
            enemiesById[enemyId] = new DeterministicEnemyState
            {
                Id = enemyId,
                Creature = enemy,
                IncomingDamage = enemyDamage
            };
            incomingDamage += enemyDamage;
        }

        Dictionary<string, int> actorPowerAmounts = player.Creature.Powers
            .Where(static power => power.IsVisible)
            .GroupBy(power => power.Id.Entry, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(power => power.DisplayAmount), StringComparer.Ordinal);

        HashSet<string> actorRelicIds = player.Relics
            .Select(static relic => relic.Id.Entry.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        return new DeterministicCombatContext
        {
            Actor = player,
            LegalActions = legalActions,
            HandCardsByInstanceId = handCardsByInstanceId,
            EnemiesById = enemiesById,
            ActorPowerAmounts = actorPowerAmounts,
            ActorRelicIds = actorRelicIds,
            CombatConfig = AiCharacterCombatConfigLoader.LoadForPlayer(player),
            RoomTypeName = roomTypeName,
            IsEliteCombat = roomTypeName.Contains("Elite", StringComparison.OrdinalIgnoreCase),
            IsBossCombat = roomTypeName.Contains("Boss", StringComparison.OrdinalIgnoreCase),
            IncomingDamage = incomingDamage
        };
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
}
