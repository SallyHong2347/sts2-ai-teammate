using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace AITeammate.Scripts;

public partial class AiTeammateCharacterSetupScreen : NSubmenu
{
    private const int DuplicateNodeFlags = 14;
    private const string BackButtonNodeName = "BackButton";
    private static readonly Vector2 BackButtonPivotOffset = new(20f, 40f);

    protected override Control? InitialFocusedControl => null;

    public static AiTeammateCharacterSetupScreen CreateFromTemplate(NSingleplayerSubmenu sourceSingleplayerSubmenu, string nodeName)
    {
        var screen = new AiTeammateCharacterSetupScreen();
        ((Node)screen).Name = nodeName;
        screen.BuildFallbackLayout(sourceSingleplayerSubmenu);
        return screen;
    }

    public override void _Ready()
    {
        ConnectSignals();
    }

    public override void OnSubmenuOpened()
    {
        Log.Info("[AITeammate] AI teammate setup page opened.");
    }

    private void BuildFallbackLayout(NSingleplayerSubmenu sourceSingleplayerSubmenu)
    {
        LayoutMode = sourceSingleplayerSubmenu.LayoutMode;
        AnchorLeft = sourceSingleplayerSubmenu.AnchorLeft;
        AnchorTop = sourceSingleplayerSubmenu.AnchorTop;
        AnchorRight = sourceSingleplayerSubmenu.AnchorRight;
        AnchorBottom = sourceSingleplayerSubmenu.AnchorBottom;
        OffsetLeft = sourceSingleplayerSubmenu.OffsetLeft;
        OffsetTop = sourceSingleplayerSubmenu.OffsetTop;
        OffsetRight = sourceSingleplayerSubmenu.OffsetRight;
        OffsetBottom = sourceSingleplayerSubmenu.OffsetBottom;
        GrowHorizontal = sourceSingleplayerSubmenu.GrowHorizontal;
        GrowVertical = sourceSingleplayerSubmenu.GrowVertical;
        Scale = sourceSingleplayerSubmenu.Scale;
        Rotation = sourceSingleplayerSubmenu.Rotation;
        PivotOffset = sourceSingleplayerSubmenu.PivotOffset;
        LayoutDirection = sourceSingleplayerSubmenu.LayoutDirection;
        SizeFlagsHorizontal = sourceSingleplayerSubmenu.SizeFlagsHorizontal;
        SizeFlagsVertical = sourceSingleplayerSubmenu.SizeFlagsVertical;
        Theme = sourceSingleplayerSubmenu.Theme;
        ThemeTypeVariation = sourceSingleplayerSubmenu.ThemeTypeVariation;
        MouseFilter = MouseFilterEnum.Stop;

        var sourceBackButton = ((Node)sourceSingleplayerSubmenu).GetNodeOrNull<Node>(BackButtonNodeName);
        if (sourceBackButton == null)
        {
            Log.Warn("[AITeammate] Could not find BackButton on NSingleplayerSubmenu while creating the fallback AI teammate setup screen.");
            return;
        }

        var duplicate = sourceBackButton.Duplicate(DuplicateNodeFlags);
        if (duplicate is Control duplicateControl)
        {
            // Reset to the authored stock scene geometry instead of inheriting the hidden source button's runtime state.
            duplicateControl.AnchorLeft = 0f;
            duplicateControl.AnchorTop = 1f;
            duplicateControl.AnchorRight = 0f;
            duplicateControl.AnchorBottom = 1f;
            duplicateControl.OffsetLeft = -40f;
            duplicateControl.OffsetTop = -354f;
            duplicateControl.OffsetRight = 160f;
            duplicateControl.OffsetBottom = -244f;
            duplicateControl.GrowVertical = GrowDirection.Begin;
            duplicateControl.PivotOffset = BackButtonPivotOffset;
            duplicateControl.Scale = Vector2.One;
        }

        AddChild(duplicate);
        Log.Info("[AITeammate] AI teammate setup screen created from fallback layout with the stock back button.");
    }
}
