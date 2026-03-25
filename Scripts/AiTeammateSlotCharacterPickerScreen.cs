using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace AITeammate.Scripts;

public partial class AiTeammateSlotCharacterPickerScreen : NSubmenu
{
    private const int DuplicateNodeFlags = 14;
    private const string BackButtonNodeName = "BackButton";
    private const string ContentPanelNodeName = "AiTeammateCharacterPickerPanel";
    private static readonly Vector2 BackButtonPivotOffset = new(20f, 40f);
    private static readonly Color PageBackgroundColor = new(0.17f, 0.24f, 0.32f, 0.96f);
    private static readonly Color PageBorderColor = new(0.62f, 0.73f, 0.81f, 0.95f);
    private static readonly Color PortraitCardColor = new(0.10f, 0.16f, 0.24f, 1f);
    private static readonly Color PortraitCardHoverColor = new(0.15f, 0.23f, 0.33f, 1f);
    private static readonly Color PortraitCardSelectedColor = new(0.23f, 0.33f, 0.45f, 1f);
    private static readonly Color PortraitCardBorderColor = new(0.42f, 0.55f, 0.65f, 1f);
    private static readonly Color PortraitSelectionColor = new(0.94f, 0.71f, 0.16f, 0.92f);
    private static readonly Color ActionButtonLeftColor = new(0.26f, 0.33f, 0.41f, 1f);
    private static readonly Color ActionButtonClearColor = new(0.63f, 0.20f, 0.20f, 1f);
    private static readonly Color ActionButtonRightColor = new(0.22f, 0.44f, 0.36f, 1f);

    private readonly Dictionary<string, Button> _portraitButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextureRect> _portraitOutlines = new(StringComparer.Ordinal);
    private Button? _confirmButton;
    private Button? _clearButton;
    private Label? _subtitleLabel;
    private AiTeammateCharacterSetupScreen? _ownerScreen;
    private int _slotIndex = -1;
    private string? _selectedCharacterId;
    private string? _currentCharacterId;

    protected override Control? InitialFocusedControl => _confirmButton;

    public static AiTeammateSlotCharacterPickerScreen CreateFromTemplate(NSingleplayerSubmenu sourceSingleplayerSubmenu, string nodeName)
    {
        var screen = new AiTeammateSlotCharacterPickerScreen();
        ((Node)screen).Name = nodeName;
        screen.BuildLayout(sourceSingleplayerSubmenu);
        return screen;
    }

    public override void _Ready()
    {
        ConnectSignals();
    }

    public override void OnSubmenuOpened()
    {
        Log.Info("[AITeammate] AI slot picker page opened.");
        RefreshSelectionState();
    }

    protected override void OnSubmenuShown()
    {
        HideBackButtonImmediately();
    }

    public void BeginSelection(AiTeammateCharacterSetupScreen ownerScreen, int slotIndex, string? selectedCharacterId)
    {
        _ownerScreen = ownerScreen;
        _slotIndex = slotIndex;
        _currentCharacterId = selectedCharacterId;
        _selectedCharacterId = selectedCharacterId;
        if (_subtitleLabel != null)
        {
            _subtitleLabel.Text = slotIndex == 0
                ? "Choose a placeholder character for the Human Player slot."
                : $"Choose a placeholder character for AI Player {slotIndex}.";
        }

        RefreshSelectionState();
    }

    private void BuildLayout(NSingleplayerSubmenu sourceSingleplayerSubmenu)
    {
        CopyRootLayoutFrom(sourceSingleplayerSubmenu);
        DuplicateBackButtonFrom(sourceSingleplayerSubmenu);
        BuildContentPanel();
    }

    private void CopyRootLayoutFrom(NSingleplayerSubmenu sourceSingleplayerSubmenu)
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
    }

    private void DuplicateBackButtonFrom(NSingleplayerSubmenu sourceSingleplayerSubmenu)
    {
        var sourceBackButton = ((Node)sourceSingleplayerSubmenu).GetNodeOrNull<Node>(BackButtonNodeName);
        if (sourceBackButton == null)
        {
            Log.Warn("[AITeammate] Could not find BackButton on NSingleplayerSubmenu while creating the AI slot picker screen.");
            return;
        }

        var duplicate = sourceBackButton.Duplicate(DuplicateNodeFlags);
        if (duplicate is Control duplicateControl)
        {
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
    }

    private void BuildContentPanel()
    {
        if (GetNodeOrNull<Control>(ContentPanelNodeName) != null)
        {
            return;
        }

        var contentPanel = new Panel
        {
            Name = ContentPanelNodeName,
            CustomMinimumSize = new Vector2(1340f, 620f),
            MouseFilter = MouseFilterEnum.Stop
        };
        contentPanel.SetAnchorsPreset(LayoutPreset.Center);
        contentPanel.OffsetLeft = -670f;
        contentPanel.OffsetTop = -420f;
        contentPanel.OffsetRight = 670f;
        contentPanel.OffsetBottom = 200f;
        contentPanel.AddThemeStyleboxOverride("panel", CreatePanelStyle(PageBackgroundColor, PageBorderColor, 6, 22));
        AddChild(contentPanel);

        var title = new Label
        {
            Text = "Choose AI Character",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.SetAnchorsPreset(LayoutPreset.TopWide);
        title.OffsetLeft = 30f;
        title.OffsetTop = 22f;
        title.OffsetRight = -30f;
        title.OffsetBottom = 74f;
        title.AddThemeFontSizeOverride("font_size", 30);
        contentPanel.AddChild(title);

        _subtitleLabel = new Label
        {
            Text = "Choose a placeholder character for this AI slot.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _subtitleLabel.SetAnchorsPreset(LayoutPreset.TopWide);
        _subtitleLabel.OffsetLeft = 50f;
        _subtitleLabel.OffsetTop = 78f;
        _subtitleLabel.OffsetRight = -50f;
        _subtitleLabel.OffsetBottom = 110f;
        _subtitleLabel.Modulate = new Color(0.88f, 0.93f, 0.97f, 0.86f);
        contentPanel.AddChild(_subtitleLabel);

        var portraitsRow = new HBoxContainer();
        portraitsRow.SetAnchorsPreset(LayoutPreset.TopWide);
        portraitsRow.OffsetLeft = 60f;
        portraitsRow.OffsetTop = 150f;
        portraitsRow.OffsetRight = -60f;
        portraitsRow.OffsetBottom = 420f;
        portraitsRow.AddThemeConstantOverride("separation", 18);
        contentPanel.AddChild(portraitsRow);

        foreach (var character in AiTeammatePlaceholderCharacters.All)
        {
            portraitsRow.AddChild(CreatePortraitButton(character));
        }

        var actionRow = new HBoxContainer();
        actionRow.SetAnchorsPreset(LayoutPreset.BottomWide);
        actionRow.OffsetLeft = 120f;
        actionRow.OffsetTop = -110f;
        actionRow.OffsetRight = -120f;
        actionRow.OffsetBottom = -32f;
        actionRow.AddThemeConstantOverride("separation", 22);
        contentPanel.AddChild(actionRow);

        var cancelButton = CreateActionButton("CancelButton", "Cancel", ActionButtonLeftColor);
        cancelButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        cancelButton.Pressed += OnCancelPressed;
        actionRow.AddChild(cancelButton);

        _clearButton = CreateActionButton("ClearSelectionButton", "Clear Selection", ActionButtonClearColor);
        _clearButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _clearButton.Visible = false;
        _clearButton.Pressed += OnClearPressed;
        actionRow.AddChild(_clearButton);

        _confirmButton = CreateActionButton("ConfirmButton", "Select", ActionButtonRightColor);
        _confirmButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _confirmButton.Disabled = true;
        _confirmButton.Pressed += OnConfirmPressed;
        actionRow.AddChild(_confirmButton);
    }

    private Control CreatePortraitButton(AiTeammatePlaceholderCharacter character)
    {
        var button = new Button
        {
            Name = $"{character.DisplayName}PortraitButton",
            CustomMinimumSize = new Vector2(230f, 250f),
            FocusMode = FocusModeEnum.All
        };
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        button.SizeFlagsVertical = SizeFlags.ExpandFill;
        button.AddThemeStyleboxOverride("normal", CreatePanelStyle(PortraitCardColor, PortraitCardBorderColor, 3, 18));
        button.AddThemeStyleboxOverride("hover", CreatePanelStyle(PortraitCardHoverColor, PortraitSelectionColor, 3, 18));
        button.AddThemeStyleboxOverride("pressed", CreatePanelStyle(PortraitCardSelectedColor, PortraitSelectionColor, 4, 18));
        button.AddThemeStyleboxOverride("focus", CreatePanelStyle(PortraitCardHoverColor, PortraitSelectionColor, 4, 18));
        button.Pressed += () => SelectCharacter(character.Id);

        var content = new MarginContainer
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        content.SetAnchorsPreset(LayoutPreset.FullRect);
        content.AddThemeConstantOverride("margin_left", 14);
        content.AddThemeConstantOverride("margin_top", 14);
        content.AddThemeConstantOverride("margin_right", 14);
        content.AddThemeConstantOverride("margin_bottom", 14);
        button.AddChild(content);

        var stack = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        stack.SetAnchorsPreset(LayoutPreset.FullRect);
        stack.AddThemeConstantOverride("separation", 12);
        content.AddChild(stack);

        var portraitHolder = new Panel
        {
            CustomMinimumSize = new Vector2(0f, 170f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        portraitHolder.AddThemeStyleboxOverride("panel", CreatePanelStyle(new Color(0.07f, 0.11f, 0.16f, 1f), PortraitCardBorderColor, 2, 16));
        stack.AddChild(portraitHolder);

        var outline = new TextureRect
        {
            Texture = AiTeammatePlaceholderCharacters.LoadTexture("res://images/packed/character_select/char_select_outline.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
            Modulate = PortraitSelectionColor
        };
        outline.SetAnchorsPreset(LayoutPreset.FullRect);
        outline.OffsetLeft = -8f;
        outline.OffsetTop = -8f;
        outline.OffsetRight = 8f;
        outline.OffsetBottom = 8f;
        portraitHolder.AddChild(outline);
        _portraitOutlines[character.Id] = outline;

        var portrait = new TextureRect
        {
            Texture = AiTeammatePlaceholderCharacters.LoadTexture(character.TexturePath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        portrait.SetAnchorsPreset(LayoutPreset.FullRect);
        portrait.OffsetLeft = 6f;
        portrait.OffsetTop = 6f;
        portrait.OffsetRight = -6f;
        portrait.OffsetBottom = -6f;
        portraitHolder.AddChild(portrait);

        var nameLabel = new Label
        {
            Text = character.DisplayName,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        nameLabel.CustomMinimumSize = new Vector2(0f, 42f);
        nameLabel.AddThemeFontSizeOverride("font_size", 22);
        stack.AddChild(nameLabel);

        _portraitButtons[character.Id] = button;
        return button;
    }

    private void SelectCharacter(string characterId)
    {
        _selectedCharacterId = characterId;
        RefreshSelectionState();
        Log.Info($"[AITeammate] Picker selected placeholder character: {characterId} for slot {_slotIndex}.");
    }

    private void RefreshSelectionState()
    {
        foreach (var option in AiTeammatePlaceholderCharacters.All)
        {
            var isSelected = string.Equals(option.Id, _selectedCharacterId, StringComparison.Ordinal);
            if (_portraitOutlines.TryGetValue(option.Id, out var outline))
            {
                outline.Visible = isSelected;
            }

            if (_portraitButtons.TryGetValue(option.Id, out var button))
            {
                button.AddThemeStyleboxOverride(
                    "normal",
                    CreatePanelStyle(
                        isSelected ? PortraitCardSelectedColor : PortraitCardColor,
                        isSelected ? PortraitSelectionColor : PortraitCardBorderColor,
                        isSelected ? 4 : 3,
                        18));
            }
        }

        if (_confirmButton != null)
        {
            _confirmButton.Disabled = string.IsNullOrEmpty(_selectedCharacterId);
        }

        if (_clearButton != null)
        {
            _clearButton.Visible = !string.IsNullOrEmpty(_currentCharacterId);
        }
    }

    private void OnCancelPressed()
    {
        Log.Info($"[AITeammate] Picker cancelled for slot {_slotIndex}.");
        _stack.Pop();
    }

    private void OnConfirmPressed()
    {
        if (_ownerScreen == null || string.IsNullOrEmpty(_selectedCharacterId))
        {
            return;
        }

        _ownerScreen.ApplyAiSlotSelection(_slotIndex, _selectedCharacterId);
        Log.Info($"[AITeammate] Picker confirmed placeholder character {_selectedCharacterId} for slot {_slotIndex}.");
        _stack.Pop();
    }

    private void OnClearPressed()
    {
        if (_ownerScreen == null || string.IsNullOrEmpty(_currentCharacterId))
        {
            return;
        }

        _ownerScreen.ClearAiSlotSelection(_slotIndex);
        Log.Info($"[AITeammate] Picker cleared placeholder character from slot {_slotIndex}.");
        _stack.Pop();
    }

    private static Button CreateActionButton(string nodeName, string text, Color backgroundColor)
    {
        var button = new Button
        {
            Name = nodeName,
            Text = text,
            CustomMinimumSize = new Vector2(0f, 58f),
            FocusMode = FocusModeEnum.All
        };
        button.AddThemeStyleboxOverride("normal", CreatePanelStyle(backgroundColor, PageBorderColor, 3, 16));
        button.AddThemeStyleboxOverride("hover", CreatePanelStyle(backgroundColor.Lightened(0.08f), PortraitSelectionColor, 3, 16));
        button.AddThemeStyleboxOverride("pressed", CreatePanelStyle(backgroundColor.Darkened(0.08f), PortraitSelectionColor, 4, 16));
        button.AddThemeStyleboxOverride("focus", CreatePanelStyle(backgroundColor.Lightened(0.08f), PortraitSelectionColor, 4, 16));
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeFontSizeOverride("font_size", 22);
        return button;
    }

    private static StyleBoxFlat CreatePanelStyle(Color backgroundColor, Color borderColor, int borderWidth, int cornerRadius)
    {
        var style = new StyleBoxFlat
        {
            BgColor = backgroundColor,
            BorderColor = borderColor,
            CornerRadiusTopLeft = cornerRadius,
            CornerRadiusTopRight = cornerRadius,
            CornerRadiusBottomRight = cornerRadius,
            CornerRadiusBottomLeft = cornerRadius
        };
        style.SetBorderWidthAll(borderWidth);
        style.ContentMarginLeft = 12f;
        style.ContentMarginTop = 12f;
        style.ContentMarginRight = 12f;
        style.ContentMarginBottom = 12f;
        return style;
    }
}
