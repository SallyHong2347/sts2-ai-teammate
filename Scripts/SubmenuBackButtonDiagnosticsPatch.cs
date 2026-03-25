using System;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace AITeammate.Scripts;

[HarmonyPatch(typeof(NSubmenuStack), nameof(NSubmenuStack.Push))]
public static class SubmenuBackButtonDiagnosticsPatch
{
    public static void Postfix(NSubmenu screen)
    {
        if (!ShouldLog(screen))
        {
            return;
        }

        LogScreenState(screen, "push-postfix");
        Callable.From(() => LogScreenState(screen, "push-deferred")).CallDeferred(Array.Empty<Variant>());
        _ = LogScreenStateAfterDelay(screen, "push-post-animation", 500);
    }

    private static bool ShouldLog(NSubmenu screen)
    {
        var typeName = screen.GetType().Name;
        return typeName is
            "AiTeammateCharacterSetupScreen" or
            "NSingleplayerSubmenu" or
            "NMultiplayerSubmenu" or
            "NCharacterSelectScreen" or
            "NTimelineScreen" or
            "NDailyRunScreen" or
            "NCustomRunScreen";
    }

    private static void LogScreenState(NSubmenu screen, string stage)
    {
        if (!GodotObject.IsInstanceValid(screen))
        {
            Log.Warn($"[AITeammate] Stock submenu diagnostics at {stage}: screen instance is no longer valid.");
            return;
        }

        var backButton = ((Node)screen).GetNodeOrNull<NBackButton>("BackButton");
        var backButtonParent = backButton != null ? ((Node)backButton).GetParent() as Control : null;
        var ascensionPanel = ((Node)screen).GetNodeOrNull<Control>("AscensionPanel") ?? ((Node)screen).GetNodeOrNull<Control>("%AscensionPanel");
        var ascensionPanelParent = ascensionPanel != null ? ((Node)ascensionPanel).GetParent() as Control : null;
        var window = ((Node)screen).GetWindow();
        var windowContentSize = window?.ContentScaleSize ?? Vector2I.Zero;
        Log.Info(
            $"[AITeammate] Stock submenu diagnostics at {stage}: " +
            $"type={screen.GetType().Name} " +
            $"screen={DescribeControl(screen)} " +
            $"backButton={(backButton == null ? "missing" : DescribeControl(backButton))} " +
            $"backButtonParent={(backButtonParent == null ? "missing" : DescribeControl(backButtonParent))} " +
            $"ascensionPanel={(ascensionPanel == null ? "missing" : DescribeControl(ascensionPanel))} " +
            $"ascensionPanelParent={(ascensionPanelParent == null ? "missing" : DescribeControl(ascensionPanelParent))} " +
            $"windowContentSize={windowContentSize}");
    }

    private static async Task LogScreenStateAfterDelay(NSubmenu screen, string stage, int delayMs)
    {
        if (!GodotObject.IsInstanceValid(screen))
        {
            return;
        }

        var tree = ((Node)screen).GetTree();
        if (tree == null)
        {
            return;
        }

        await ((GodotObject)screen).ToSignal(tree.CreateTimer(delayMs / 1000.0), SceneTreeTimer.SignalName.Timeout);
        LogScreenState(screen, stage);
    }

    private static string DescribeControl(Control control)
    {
        return
            $"name={((Node)control).Name}, " +
            $"parent={((Node)control).GetParent()?.Name}, " +
            $"visible={((CanvasItem)control).Visible}, " +
            $"anchors=({control.AnchorLeft}, {control.AnchorTop}, {control.AnchorRight}, {control.AnchorBottom}), " +
            $"offsets=({control.OffsetLeft}, {control.OffsetTop}, {control.OffsetRight}, {control.OffsetBottom}), " +
            $"position={control.Position}, " +
            $"global={control.GlobalPosition}, " +
            $"size={control.Size}, " +
            $"scale={control.Scale}, " +
            $"modulate={control.Modulate}";
    }
}
