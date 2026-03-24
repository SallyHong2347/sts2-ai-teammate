using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace AITeammate.Scripts;

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
public static class MainMenuAiTeammatePatch
{
    private const string ButtonName = "AiTeammateButton";
    private const string ButtonLabel = "Play with AI Teammate";

    private static readonly System.Reflection.MethodInfo FocusedHandler =
        AccessTools.Method(typeof(NMainMenu), "MainMenuButtonFocused")!;

    private static readonly System.Reflection.MethodInfo UnfocusedHandler =
        AccessTools.Method(typeof(NMainMenu), "MainMenuButtonUnfocused")!;

    private static readonly System.Reflection.FieldInfo LocStringField =
        AccessTools.Field(typeof(NMainMenuTextButton), "_locString")!;

    public static void Postfix(NMainMenu __instance)
    {
        try
        {
            AddAiTeammateButton(__instance);
        }
        catch (Exception ex)
        {
            Log.Error($"[AITeammate] Failed to add main menu button: {ex}");
        }
    }

    private static void AddAiTeammateButton(NMainMenu mainMenu)
    {
        var buttonContainer = ((Node)mainMenu).GetNode<Control>("%MainMenuTextButtons");
        if (((Node)buttonContainer).GetNodeOrNull<NMainMenuTextButton>(ButtonName) != null)
        {
            return;
        }

        var multiplayerButton = ((Node)mainMenu).GetNode<NMainMenuTextButton>("MainMenuTextButtons/MultiplayerButton");
        var compendiumButton = ((Node)mainMenu).GetNode<NMainMenuTextButton>("MainMenuTextButtons/CompendiumButton");

        var aiButton = (NMainMenuTextButton)((Node)multiplayerButton).Duplicate(14);
        ((Node)aiButton).Name = ButtonName;
        ((Node)(object)buttonContainer).AddChildSafely((Node?)(object)aiButton);
        ((Node)buttonContainer).MoveChild((Node)(object)aiButton, ((Node)multiplayerButton).GetIndex(false) + 1);

        ConfigureLabel(aiButton);
        ConfigureFocus(mainMenu, multiplayerButton, aiButton, compendiumButton);
        ConnectSignals(aiButton);

        Log.Info("[AITeammate] Main menu button created.");
    }

    private static void ConfigureLabel(NMainMenuTextButton aiButton)
    {
        LocStringField.SetValue(aiButton, null);
        var label = ((Node)aiButton).GetChild<MegaLabel>(0, false);
        label.SetTextAutoSize(ButtonLabel);
        ((Control)label).PivotOffset = ((Control)label).Size * 0.5f;
    }

    private static void ConfigureFocus(
        NMainMenu mainMenu,
        NMainMenuTextButton multiplayerButton,
        NMainMenuTextButton aiButton,
        NMainMenuTextButton compendiumButton)
    {
        ((Control)aiButton).FocusNeighborTop = ((Node)multiplayerButton).GetPath();
        ((Control)aiButton).FocusNeighborBottom = ((Node)compendiumButton).GetPath();

        ((Control)multiplayerButton).FocusNeighborBottom = ((Node)aiButton).GetPath();
        ((Control)compendiumButton).FocusNeighborTop = ((Node)aiButton).GetPath();

        // Match the stock menu's reticle behavior for focused/unfocused buttons.
        ((GodotObject)aiButton).Connect(
            NClickableControl.SignalName.Unfocused,
            Callable.From<NMainMenuTextButton>((Action<NMainMenuTextButton>)(button =>
            {
                UnfocusedHandler.Invoke(mainMenu, new object[] { button });
            })),
            0u);

        ((GodotObject)aiButton).Connect(
            NClickableControl.SignalName.Focused,
            Callable.From<NMainMenuTextButton>((Action<NMainMenuTextButton>)(button =>
            {
                var callable = Callable.From(() =>
                {
                    FocusedHandler.Invoke(mainMenu, new object[] { button });
                });
                callable.CallDeferred(Array.Empty<Variant>());
            })),
            0u);
    }

    private static void ConnectSignals(NMainMenuTextButton aiButton)
    {
        ((GodotObject)aiButton).Connect(
            NClickableControl.SignalName.Released,
            Callable.From<NButton>((Action<NButton>)(_ => OnAiTeammateButtonPressed())),
            0u);
    }

    private static void OnAiTeammateButtonPressed()
    {
        Log.Info("[AITeammate] Button clicked.");

        var game = NGame.Instance;
        if (game == null)
        {
            Log.Warn("[AITeammate] Could not show placeholder message because NGame.Instance was null.");
            return;
        }

        var placeholderVfx = NFullscreenTextVfx.Create("AI Teammate placeholder");
        ((Node)game).AddChildSafely(placeholderVfx);
    }
}
