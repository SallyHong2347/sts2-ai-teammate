using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace AITeammate.Scripts;

internal sealed partial class AiTeammateDummyController
{
    private static readonly MethodInfo? EventChooseOptionForEventMethod =
        AccessTools.Method(typeof(EventSynchronizer), "ChooseOptionForEvent");
    private static readonly MethodInfo? EventVoteForSharedOptionMethod =
        AccessTools.Method(typeof(EventSynchronizer), "PlayerVotedForSharedOptionIndex");
    private static readonly MethodInfo? RestSiteChooseOptionMethod =
        AccessTools.Method(typeof(RestSiteSynchronizer), "ChooseOption");
    private static readonly FieldInfo? EventPageIndexField =
        AccessTools.Field(typeof(EventSynchronizer), "_pageIndex");

    private IReadOnlyList<AiTeammateAvailableAction> DiscoverEventActions(Player player)
    {
        EventSynchronizer synchronizer = RunManager.Instance.EventSynchronizer;
        if (synchronizer.IsShared && synchronizer.GetPlayerVote(player).HasValue)
        {
            return [];
        }

        EventModel eventForPlayer = synchronizer.GetEventForPlayer(player);
        IReadOnlyList<EventOption> options = eventForPlayer.CurrentOptions;
        string eventFingerprint = BuildEventActionFingerprint(synchronizer, eventForPlayer);
        EventPlanningInspection inspection = InspectCurrentEventPlan(player, synchronizer, eventForPlayer, eventFingerprint);
        EventExecutionSelection selection = ResolveEventExecutionSelection(
            player,
            synchronizer,
            eventForPlayer,
            inspection,
            eventFingerprint,
            phase: "discover");

        if (selection.OptionIndex < 0 || selection.SelectedOption == null)
        {
            return [];
        }

        return
        [
            new AiTeammateAvailableAction(
                new AiLegalActionOption
                {
                    ActionId = BuildEventOptionActionId(eventFingerprint, selection.OptionIndex),
                    ActionType = AiTeammateActionKind.ChooseEventOption.ToString(),
                    Description = $"Choose event option {selection.SelectedOption.TextKey}",
                    Label = $"Event option {selection.SelectedOption.TextKey}",
                    Summary = $"Choose event option {selection.SelectedOption.TextKey}."
                },
                async () =>
                {
                    EventModel liveEvent = synchronizer.GetEventForPlayer(player);
                    EventExecutionSelection liveSelection = ResolveEventExecutionSelection(
                        player,
                        synchronizer,
                        liveEvent,
                        inspection,
                        eventFingerprint,
                        phase: "execute");
                    if (liveSelection.OptionIndex < 0 || liveSelection.SelectedOption == null)
                    {
                        return AiActionExecutionResult.Completed;
                    }

                    if (string.Equals(liveSelection.SelectionMode, "planner", System.StringComparison.Ordinal))
                    {
                        Log.Info($"[AITeammate][Event] Executing planner-selected event option player={PlayerId} optionIndex={liveSelection.OptionIndex} textKey={liveSelection.SelectedOption.TextKey} title=\"{DescribeOptionTitle(liveSelection.SelectedOption)}\"");
                    }
                    else
                    {
                        Log.Info($"[AITeammate][Event] Executing fallback event option player={PlayerId} optionIndex={liveSelection.OptionIndex} textKey={liveSelection.SelectedOption.TextKey} title=\"{DescribeOptionTitle(liveSelection.SelectedOption)}\" reason={liveSelection.Reason}");
                    }

                    await ChooseEventOptionAsync(synchronizer, player, liveSelection.OptionIndex);
                    return AiActionExecutionResult.Completed;
                },
                $"{PlayerId}:event:{eventFingerprint}:{selection.OptionIndex}")
        ];
    }

    private IReadOnlyList<AiTeammateAvailableAction> DiscoverRestSiteActions(Player player)
    {
        RestSiteSynchronizer synchronizer = RunManager.Instance.RestSiteSynchronizer;
        IReadOnlyList<RestSiteOption> options = synchronizer.GetOptionsForPlayer(player);
        RestSiteOption? preferredOption = options.FirstOrDefault(static option => option.OptionId == "HEAL") ?? options.FirstOrDefault();
        if (preferredOption == null)
        {
            return [];
        }

        int optionIndex = options.ToList().IndexOf(preferredOption);
        return
        [
            new AiTeammateAvailableAction(
                new AiLegalActionOption
                {
                    ActionId = BuildRestSiteOptionActionId(preferredOption.OptionId, optionIndex),
                    ActionType = AiTeammateActionKind.ChooseRestSiteOption.ToString(),
                    Description = $"Choose rest site option {preferredOption.OptionId}",
                    Label = $"Rest site option {preferredOption.OptionId}",
                    Summary = $"Choose rest site option {preferredOption.OptionId}."
                },
                async () =>
                {
                    await ChooseRestSiteOptionAsync(synchronizer, player, optionIndex);
                    return AiActionExecutionResult.Completed;
                })
        ];
    }

    private static string BuildEventActionFingerprint(EventSynchronizer synchronizer, EventModel eventForPlayer)
    {
        uint pageIndex = EventPageIndexField?.GetValue(synchronizer) is uint currentPageIndex
            ? currentPageIndex
            : 0u;
        string optionFingerprint = string.Join(
            ",",
            eventForPlayer.CurrentOptions.Select(static option => $"{option.TextKey}:{option.IsLocked}:{option.IsProceed}"));
        return $"{eventForPlayer.Id}|finished={eventForPlayer.IsFinished}|page={pageIndex}|options={optionFingerprint}";
    }

    private static async Task ChooseEventOptionAsync(EventSynchronizer synchronizer, Player player, int optionIndex)
    {
        using IDisposable selectorScope = PushDeterministicCardSelector();

        if (synchronizer.IsShared)
        {
            uint pageIndex = EventPageIndexField?.GetValue(synchronizer) is uint currentPageIndex
                ? currentPageIndex
                : 0u;
            EventVoteForSharedOptionMethod?.Invoke(synchronizer, new object[] { player, (uint)optionIndex, pageIndex });
            await Task.CompletedTask;
            return;
        }

        EventChooseOptionForEventMethod?.Invoke(synchronizer, new object[] { player, optionIndex });
        await Task.CompletedTask;
    }

    private static async Task ChooseRestSiteOptionAsync(RestSiteSynchronizer synchronizer, Player player, int optionIndex)
    {
        if (RestSiteChooseOptionMethod?.Invoke(synchronizer, new object[] { player, optionIndex }) is Task<bool> task)
        {
            await task;
        }
    }

    private static string BuildEventOptionActionId(string eventFingerprint, int optionIndex)
    {
        return $"event_option_{optionIndex}_{SanitizeActionToken(eventFingerprint)}";
    }

    private static string BuildRestSiteOptionActionId(string optionId, int optionIndex)
    {
        return $"rest_site_option_{optionIndex}_{SanitizeActionToken(optionId)}";
    }
}
