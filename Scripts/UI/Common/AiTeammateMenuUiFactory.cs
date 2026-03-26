using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace AITeammate.Scripts;

internal static class AiTeammateMenuUiFactory
{
    internal const int DuplicateNodeFlags = 14;
    internal static readonly Vector2 StockBackButtonPivotOffset = new(20f, 40f);

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
        Node? sourceBackButton = sourceSubmenu.GetNodeOrNull<Node>("BackButton");
        if (sourceBackButton == null)
        {
            Log.Warn($"[AITeammate] Could not find BackButton on NSingleplayerSubmenu while {failureContext}.");
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
}
