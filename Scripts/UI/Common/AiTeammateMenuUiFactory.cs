using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace AITeammate.Scripts;

internal static class AiTeammateMenuUiFactory
{
    internal const int DuplicateNodeFlags = 14;
    internal static readonly Vector2 StockBackButtonPivotOffset = new(20f, 40f);
    internal static readonly Color CustomBackButtonColor = new(0.70f, 0.22f, 0.20f, 0.96f);
    internal static readonly Color CustomBackButtonHoverColor = new(0.81f, 0.29f, 0.25f, 0.98f);

    public static void CopySubmenuLayoutFrom(Control target, Control source)
    {
        target.LayoutMode = source.LayoutMode;
        target.AnchorLeft = source.AnchorLeft;
        target.AnchorTop = source.AnchorTop;
        target.AnchorRight = source.AnchorRight;
        target.AnchorBottom = source.AnchorBottom;
        target.OffsetLeft = source.OffsetLeft;
        target.OffsetTop = source.OffsetTop;
        target.OffsetRight = source.OffsetRight;
        target.OffsetBottom = source.OffsetBottom;
        target.GrowHorizontal = source.GrowHorizontal;
        target.GrowVertical = source.GrowVertical;
        target.Scale = source.Scale;
        target.Rotation = source.Rotation;
        target.PivotOffset = source.PivotOffset;
        target.LayoutDirection = source.LayoutDirection;
        target.SizeFlagsHorizontal = source.SizeFlagsHorizontal;
        target.SizeFlagsVertical = source.SizeFlagsVertical;
        target.Theme = source.Theme;
        target.ThemeTypeVariation = source.ThemeTypeVariation;
        target.MouseFilter = Control.MouseFilterEnum.Stop;
    }

    public static bool TryDuplicateStockBackButton(
        Node owner,
        NSingleplayerSubmenu sourceSubmenu,
        string failureContext)
    {
        return TryDuplicateBackButton(owner, sourceSubmenu, "BackButton", failureContext);
    }

    public static bool TryDuplicateBackButton(
        Node owner,
        Node sourceNode,
        string backButtonPath,
        string failureContext)
    {
        Node? sourceBackButton = sourceNode.GetNodeOrNull<Node>(backButtonPath);
        if (sourceBackButton == null)
        {
            Log.Warn($"[AITeammate] Could not find BackButton on {sourceNode.GetType().Name} while {failureContext}.");
            return false;
        }

        Node duplicate = sourceBackButton.Duplicate(DuplicateNodeFlags);
        if (duplicate is Control duplicateControl)
        {
            ResetStockBackButtonGeometry(duplicateControl);
        }

        owner.AddChild(duplicate);
        return true;
    }

    public static void ResetStockBackButtonGeometry(Control control)
    {
        // Reset to the authored stock scene geometry instead of inheriting hidden-source runtime state.
        control.AnchorLeft = 0f;
        control.AnchorTop = 1f;
        control.AnchorRight = 0f;
        control.AnchorBottom = 1f;
        control.OffsetLeft = -40f;
        control.OffsetTop = -354f;
        control.OffsetRight = 160f;
        control.OffsetBottom = -244f;
        control.GrowVertical = Control.GrowDirection.Begin;
        control.PivotOffset = StockBackButtonPivotOffset;
        control.Scale = Vector2.One;
    }

    public static StyleBoxFlat CreateRoundedPanelStyle(
        Color backgroundColor,
        Color borderColor,
        int borderWidth,
        int cornerRadius,
        float contentMargin = 20f)
    {
        StyleBoxFlat style = new()
        {
            BgColor = backgroundColor,
            BorderColor = borderColor,
            CornerRadiusTopLeft = cornerRadius,
            CornerRadiusTopRight = cornerRadius,
            CornerRadiusBottomRight = cornerRadius,
            CornerRadiusBottomLeft = cornerRadius
        };
        style.SetBorderWidthAll(borderWidth);
        style.ContentMarginLeft = contentMargin;
        style.ContentMarginTop = contentMargin;
        style.ContentMarginRight = contentMargin;
        style.ContentMarginBottom = contentMargin;
        return style;
    }

    public static Button CreateSimpleBackButton(string nodeName = "AiTeammateBackButton")
    {
        Button button = new()
        {
            Name = nodeName,
            Text = "Back",
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(150f, 52f)
        };
        button.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        button.OffsetLeft = 28f;
        button.OffsetTop = 28f;
        button.OffsetRight = 178f;
        button.OffsetBottom = 80f;
        button.AddThemeStyleboxOverride("normal", CreateRoundedPanelStyle(CustomBackButtonColor, Colors.White, 2, 14, 8f));
        button.AddThemeStyleboxOverride("hover", CreateRoundedPanelStyle(CustomBackButtonHoverColor, Colors.White, 2, 14, 8f));
        button.AddThemeStyleboxOverride("pressed", CreateRoundedPanelStyle(CustomBackButtonColor.Darkened(0.12f), Colors.White, 2, 14, 8f));
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeFontSizeOverride("font_size", 20);
        return button;
    }
}
