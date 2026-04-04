using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace AITeammate.Scripts;

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
public static class MainMenuAiTeammatePatch
{
    private const string ButtonName = "AiTeammateButton";
    private const string ButtonLabel = "Play with AI Teammate";
    private const string ScreenNodeName = "AiTeammateCharacterSetupScreen";
    private const string ContinueScreenNodeName = "AiTeammateContinueRunScreen";
    private static readonly System.Reflection.MethodInfo FocusedHandler =
        AccessTools.Method(typeof(NMainMenu), "MainMenuButtonFocused")!;

    private static readonly System.Reflection.MethodInfo UnfocusedHandler =
        AccessTools.Method(typeof(NMainMenu), "MainMenuButtonUnfocused")!;

    private static readonly System.Reflection.FieldInfo LocStringField =
        AccessTools.Field(typeof(NMainMenuTextButton), "_locString")!;

    private static readonly System.Reflection.FieldInfo LastHitButtonField =
        AccessTools.Field(typeof(NMainMenu), "_lastHitButton")!;

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

        var labelNode = ((Node)aiButton).GetChild(0, false);
        if (labelNode is not Label label)
        {
            Log.Warn("[AITeammate] Could not relabel the AI teammate menu button because its label node was not a Godot Label.");
            return;
        }

        label.Text = ButtonLabel;
        label.PivotOffset = label.Size * 0.5f;
    }

    private static void ConfigureFocus(
        NMainMenu mainMenu,
        NMainMenuTextButton multiplayerButton,
        NMainMenuTextButton aiButton,
        NMainMenuTextButton compendiumButton)
    {
        if (!((Node)multiplayerButton).IsInsideTree() ||
            !((Node)aiButton).IsInsideTree() ||
            !((Node)compendiumButton).IsInsideTree())
        {
            Log.Warn("[AITeammate] Skipped main menu focus wiring because one or more buttons were not yet in the scene tree.");
            return;
        }

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
            Callable.From<NButton>((Action<NButton>)(_ => OnAiTeammateButtonPressed(aiButton))),
            0u);
    }

    private static void OnAiTeammateButtonPressed(NMainMenuTextButton aiButton)
    {
        Log.Info("[AITeammate] Button clicked.");

        var buttonContainer = ((Node)aiButton).GetParent();
        var mainMenu = buttonContainer?.GetParent() as NMainMenu;
        if (mainMenu == null)
        {
            Log.Warn("[AITeammate] Could not open AI teammate setup page because the main menu was not found.");
            return;
        }

        try
        {
            if (AiTeammateSaveSupport.HasContinueableSavedRun())
            {
                Log.Info("[AITeammate] Saved AI teammate run detected. Opening continue screen.");
                OpenAiTeammateContinueSubmenu(mainMenu, aiButton);
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[AITeammate] Failed to route AI teammate menu button through saved-run flow: {ex}");
        }

        try
        {
            Log.Info("[AITeammate] Opening AI teammate setup screen.");
            OpenAiTeammateSetupSubmenu(mainMenu, aiButton);
        }
        catch (Exception ex)
        {
            Log.Error($"[AITeammate] Failed to open AI teammate setup screen: {ex}");
        }
    }

    internal static void OpenAiTeammateSetupSubmenu(NMainMenu mainMenu, Control? focusSource = null)
    {
        if (focusSource is NMainMenuTextButton mainMenuTextButton)
        {
            LastHitButtonField.SetValue(mainMenu, mainMenuTextButton);
        }

        var submenuStack = mainMenu.SubmenuStack;
        var screen = GetOrCreateSetupScreen(submenuStack);
        screen.ResetForFreshEntry();
        submenuStack.Push(screen);
    }

    internal static void OpenAiTeammateContinueSubmenu(NMainMenu mainMenu, Control? focusSource = null)
    {
        if (focusSource is NMainMenuTextButton mainMenuTextButton)
        {
            LastHitButtonField.SetValue(mainMenu, mainMenuTextButton);
        }

        var submenuStack = mainMenu.SubmenuStack;
        try
        {
            var screen = GetOrCreateContinueScreen(submenuStack);
            submenuStack.Push(screen);
        }
        catch (Exception ex)
        {
            Log.Error($"[AITeammate] Failed to open AI teammate continue screen, falling back to setup: {ex}");
            var screen = GetOrCreateSetupScreen(submenuStack);
            submenuStack.Push(screen);
        }
    }

    private static AiTeammateCharacterSetupScreen GetOrCreateSetupScreen(NSubmenuStack submenuStack)
    {
        var screen = ((Node)submenuStack).GetNodeOrNull<AiTeammateCharacterSetupScreen>(ScreenNodeName);
        if (screen != null)
        {
            return screen;
        }

        var sourceSingleplayerSubmenu = submenuStack.GetSubmenuType<NSingleplayerSubmenu>();
        if (sourceSingleplayerSubmenu == null)
        {
            throw new InvalidOperationException("The stock singleplayer submenu was unavailable while creating the AI teammate setup page.");
        }

        var sourceCharacterSelectScreen = submenuStack.GetSubmenuType<NCharacterSelectScreen>();
        screen = AiTeammateCharacterSetupScreen.CreateFromTemplate(sourceSingleplayerSubmenu, sourceCharacterSelectScreen, ScreenNodeName);
        ((CanvasItem)screen).Visible = false;
        ((Node)(object)submenuStack).AddChild(screen);
        return screen;
    }

    private static AiTeammateContinueRunScreen GetOrCreateContinueScreen(NSubmenuStack submenuStack)
    {
        var screen = ((Node)submenuStack).GetNodeOrNull<AiTeammateContinueRunScreen>(ContinueScreenNodeName);
        if (screen != null)
        {
            return screen;
        }

        var sourceSingleplayerSubmenu = submenuStack.GetSubmenuType<NSingleplayerSubmenu>();
        if (sourceSingleplayerSubmenu == null)
        {
            throw new InvalidOperationException("The stock singleplayer submenu was unavailable while creating the AI teammate continue page.");
        }

        screen = AiTeammateContinueRunScreen.CreateFromTemplate(sourceSingleplayerSubmenu, ContinueScreenNodeName);
        ((CanvasItem)screen).Visible = false;
        ((Node)(object)submenuStack).AddChild(screen);
        return screen;
    }
}
