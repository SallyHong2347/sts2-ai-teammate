using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;

namespace AITeammate.Scripts;

public partial class AiTeammateCharacterSetupScreen : NSubmenu, IStartRunLobbyListener
{
    private const int DuplicateNodeFlags = 14;
    private const int MaxPlayerCount = 5;
    private const string BackButtonNodeName = "BackButton";
    private const string ContentPanelNodeName = "AiTeammateContentPanel";
    private const string PickerScreenNodeName = "AiTeammateSlotCharacterPickerScreen";
    private const string AscensionPanelNodeName = "AscensionPanel";
    private const string UniqueAscensionPanelNodePath = "%AscensionPanel";
    private const string RemotePlayerContainerNodeName = "RemotePlayerContainer";
    private const string SessionPanelNodeName = "AiTeammateSessionPanel";
    private const string SessionSummaryNodeName = "AiTeammateSessionSummary";
    private const string SessionHintNodeName = "AiTeammateSessionHint";
    private const string ProceedButtonNodeName = "AiTeammateProceedButton";
    private const float ContentPanelVerticalShift = 170f;
    private static readonly Vector2 BackButtonPivotOffset = new(20f, 40f);
    private static readonly Vector2 AscensionPanelPosition = new(-317f, -341f);
    private static readonly Vector2 AscensionPanelSize = new(634f, 117f);
    private static readonly Color PageBackgroundColor = new(0.21f, 0.31f, 0.39f, 0.92f);
    private static readonly Color SlotColor = new(0.12f, 0.20f, 0.29f, 1f);
    private static readonly Color SlotBorderColor = new(0.43f, 0.57f, 0.66f, 1f);
    private static readonly Color DividerColor = new(0.65f, 0.78f, 0.85f, 0.85f);
    private static readonly Color SlotHoverColor = new(0.16f, 0.26f, 0.37f, 1f);
    private static readonly Color SlotPressedColor = new(0.11f, 0.18f, 0.26f, 1f);
    private static readonly Color ContentPanelBorderColor = new(0.58f, 0.71f, 0.8f, 0.95f);
    private static readonly Color PortraitPanelColor = new(0.07f, 0.11f, 0.16f, 1f);
    private static readonly Color RemoveButtonColor = new(0.72f, 0.18f, 0.18f, 0.98f);
    private static readonly Color RemoveButtonHoverColor = new(0.82f, 0.22f, 0.22f, 1f);
    private static readonly Color SessionPanelColor = new(0.10f, 0.14f, 0.20f, 0.94f);
    private static readonly Color ProceedButtonColor = new(0.75f, 0.60f, 0.16f, 1f);
    private static readonly Color ProceedButtonHoverColor = new(0.85f, 0.69f, 0.20f, 1f);
    private static readonly Color ProceedButtonDisabledColor = new(0.38f, 0.38f, 0.38f, 1f);

    private readonly Dictionary<int, Button> _slotButtons = new();
    private readonly Dictionary<int, TextureRect> _slotPortraits = new();
    private readonly Dictionary<int, Label> _slotTitles = new();
    private readonly Dictionary<int, Label> _slotSubtitles = new();
    private readonly Dictionary<int, Button> _slotRemoveButtons = new();
    private readonly Dictionary<int, string?> _slotSelections = new();
    private readonly HashSet<int> _slotHoverStates = new();
    private readonly HashSet<int> _slotRemoveHoverStates = new();
    private NSingleplayerSubmenu? _sourceSingleplayerSubmenu;
    private NCharacterSelectScreen? _sourceCharacterSelectScreen;
    private AiTeammateSlotCharacterPickerScreen? _pickerScreen;
    private NRemoteLobbyPlayerContainer? _remoteLobbyPlayerContainer;
    private Label? _sessionSummaryLabel;
    private Label? _sessionHintLabel;
    private Button? _proceedButton;
    private AiTeammateSessionState? _sessionState;
    private AiTeammateLoopbackHostGameService? _loopbackService;
    private StartRunLobby? _lobby;
    private NAscensionPanel? _ascensionPanel;
    private int _selectedAscensionLevel;
    private bool _isSyncingAscensionPanel;
    private bool _isStartingRun;

    protected override Control? InitialFocusedControl => null;

    public static AiTeammateCharacterSetupScreen CreateFromTemplate(
        NSingleplayerSubmenu sourceSingleplayerSubmenu,
        NCharacterSelectScreen? sourceCharacterSelectScreen,
        string nodeName)
    {
        var screen = new AiTeammateCharacterSetupScreen();
        ((Node)screen).Name = nodeName;
        screen.BuildFallbackLayout(sourceSingleplayerSubmenu, sourceCharacterSelectScreen);
        return screen;
    }

    public override void _Ready()
    {
        ConnectSignals();
    }

    public override void OnSubmenuOpened()
    {
        Log.Info("[AITeammate] AI teammate setup page opened.");
        RefreshSessionFromSelections();
    }

    public override void OnSubmenuClosed()
    {
        base.OnSubmenuClosed();
        if (!_isStartingRun)
        {
            CleanupActiveLobby(disconnectSession: true, clearSessionRegistry: true);
        }
    }

    private void BuildFallbackLayout(NSingleplayerSubmenu sourceSingleplayerSubmenu, NCharacterSelectScreen? sourceCharacterSelectScreen)
    {
        _sourceSingleplayerSubmenu = sourceSingleplayerSubmenu;
        _sourceCharacterSelectScreen = sourceCharacterSelectScreen;
        _selectedAscensionLevel = SaveManager.Instance.Progress.PreferredMultiplayerAscension;

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
            ResetBackButtonGeometry(duplicateControl);
        }

        AddChild(duplicate);
        Control contentPanel = BuildPlaceholderSlotsUi();
        BuildSessionUi(contentPanel, sourceCharacterSelectScreen);
        BuildPlaceholderAscensionPanel(sourceCharacterSelectScreen);
        Log.Info("[AITeammate] AI teammate setup screen created from fallback layout with the stock back button.");
    }

    private Control BuildPlaceholderSlotsUi()
    {
        Control? existing = GetNodeOrNull<Control>(ContentPanelNodeName);
        if (existing != null)
        {
            return existing;
        }

        var contentPanel = new Panel
        {
            Name = ContentPanelNodeName,
            CustomMinimumSize = new Vector2(1240f, 640f),
            MouseFilter = MouseFilterEnum.Stop
        };
        contentPanel.SetAnchorsPreset(LayoutPreset.Center);
        contentPanel.OffsetLeft = -620f;
        contentPanel.OffsetTop = -320f - ContentPanelVerticalShift;
        contentPanel.OffsetRight = 620f;
        contentPanel.OffsetBottom = 320f - ContentPanelVerticalShift;
        contentPanel.AddThemeStyleboxOverride("panel", CreatePanelStyle(PageBackgroundColor, ContentPanelBorderColor, 6, 22));
        AddChild(contentPanel);

        var title = new Label
        {
            Text = "AI Teammate Setup",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.SetAnchorsPreset(LayoutPreset.TopWide);
        title.OffsetLeft = 30f;
        title.OffsetTop = 22f;
        title.OffsetRight = -30f;
        title.OffsetBottom = 72f;
        title.AddThemeFontSizeOverride("font_size", 30);
        contentPanel.AddChild(title);

        var subtitle = new Label
        {
            Text = "Choose a real host character and any AI teammates, then reuse the multiplayer lobby start flow.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        subtitle.SetAnchorsPreset(LayoutPreset.TopWide);
        subtitle.OffsetLeft = 60f;
        subtitle.OffsetTop = 74f;
        subtitle.OffsetRight = -60f;
        subtitle.OffsetBottom = 120f;
        subtitle.Modulate = new Color(0.88f, 0.93f, 0.97f, 0.85f);
        contentPanel.AddChild(subtitle);

        var slotsRow = new HBoxContainer();
        slotsRow.AddThemeConstantOverride("separation", 18);
        slotsRow.SetAnchorsPreset(LayoutPreset.TopWide);
        slotsRow.OffsetLeft = 42f;
        slotsRow.OffsetTop = 136f;
        slotsRow.OffsetRight = -42f;
        slotsRow.OffsetBottom = 360f;
        contentPanel.AddChild(slotsRow);

        slotsRow.AddChild(CreateSlotButton(0, "Human Player", "Required", allowPicker: true));
        slotsRow.AddChild(CreateDivider());

        for (var slotIndex = 1; slotIndex < 5; slotIndex++)
        {
            slotsRow.AddChild(CreateSlotButton(slotIndex, $"AI Player {slotIndex}", "Optional", allowPicker: true));
        }

        return contentPanel;
    }

    private Button CreateSlotButton(int slotIndex, string title, string subtitle, bool allowPicker)
    {
        var slotButton = new Button
        {
            Name = $"PlayerSlot{slotIndex}",
            CustomMinimumSize = new Vector2(210f, 220f),
            FocusMode = FocusModeEnum.All
        };
        slotButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        slotButton.SizeFlagsVertical = SizeFlags.ExpandFill;
        slotButton.AddThemeStyleboxOverride("normal", CreatePanelStyle(SlotColor, SlotBorderColor, 3, 18));
        slotButton.AddThemeStyleboxOverride("hover", CreatePanelStyle(SlotHoverColor, DividerColor, 3, 18));
        slotButton.AddThemeStyleboxOverride("pressed", CreatePanelStyle(SlotPressedColor, DividerColor, 3, 18));
        slotButton.AddThemeStyleboxOverride("focus", CreatePanelStyle(SlotHoverColor, DividerColor, 4, 18));
        slotButton.AddThemeColorOverride("font_color", Colors.White);
        slotButton.AddThemeColorOverride("font_hover_color", Colors.White);
        slotButton.AddThemeColorOverride("font_pressed_color", Colors.White);
        slotButton.AddThemeColorOverride("font_focus_color", Colors.White);
        slotButton.AddThemeFontSizeOverride("font_size", 21);
        _slotButtons[slotIndex] = slotButton;

        if (allowPicker)
        {
            slotButton.MouseEntered += () =>
            {
                _slotHoverStates.Add(slotIndex);
                RefreshSlotRemoveButton(slotIndex);
            };
            slotButton.MouseExited += () =>
            {
                _slotHoverStates.Remove(slotIndex);
                RefreshSlotRemoveButton(slotIndex);
            };
        }

        var content = new MarginContainer
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        content.SetAnchorsPreset(LayoutPreset.FullRect);
        content.AddThemeConstantOverride("margin_left", 14);
        content.AddThemeConstantOverride("margin_top", 14);
        content.AddThemeConstantOverride("margin_right", 14);
        content.AddThemeConstantOverride("margin_bottom", 14);
        slotButton.AddChild(content);

        var stack = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        stack.SetAnchorsPreset(LayoutPreset.FullRect);
        stack.AddThemeConstantOverride("separation", 12);
        content.AddChild(stack);

        var portraitPanel = new Panel
        {
            CustomMinimumSize = new Vector2(0f, 138f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        portraitPanel.AddThemeStyleboxOverride("panel", CreatePanelStyle(PortraitPanelColor, SlotBorderColor, 2, 14));
        stack.AddChild(portraitPanel);

        var portrait = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };
        portrait.SetAnchorsPreset(LayoutPreset.FullRect);
        portrait.OffsetLeft = 6f;
        portrait.OffsetTop = 6f;
        portrait.OffsetRight = -6f;
        portrait.OffsetBottom = -6f;
        portraitPanel.AddChild(portrait);
        _slotPortraits[slotIndex] = portrait;

        if (allowPicker)
        {
            var removeButton = CreateRemoveButton(slotIndex);
            portraitPanel.AddChild(removeButton);
            _slotRemoveButtons[slotIndex] = removeButton;
        }

        var titleLabel = new Label
        {
            Text = title,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        titleLabel.CustomMinimumSize = new Vector2(0f, 34f);
        titleLabel.AddThemeFontSizeOverride("font_size", 21);
        stack.AddChild(titleLabel);
        _slotTitles[slotIndex] = titleLabel;

        var subtitleLabel = new Label
        {
            Text = subtitle,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore
        };
        subtitleLabel.CustomMinimumSize = new Vector2(0f, 54f);
        subtitleLabel.Modulate = new Color(0.88f, 0.93f, 0.97f, 0.85f);
        stack.AddChild(subtitleLabel);
        _slotSubtitles[slotIndex] = subtitleLabel;

        if (allowPicker)
        {
            slotButton.Pressed += () => OpenCharacterPicker(slotIndex);
        }
        return slotButton;
    }

    private static Control CreateDivider()
    {
        var dividerContainer = new CenterContainer
        {
            CustomMinimumSize = new Vector2(18f, 220f)
        };

        var divider = new ColorRect
        {
            Color = DividerColor,
            CustomMinimumSize = new Vector2(4f, 188f)
        };
        dividerContainer.AddChild(divider);
        return dividerContainer;
    }

    private void BuildPlaceholderAscensionPanel(NCharacterSelectScreen? sourceCharacterSelectScreen)
    {
        if (sourceCharacterSelectScreen == null || GetNodeOrNull<Control>(AscensionPanelNodeName) != null)
        {
            return;
        }

        var sourceAscensionPanel =
            ((Node)sourceCharacterSelectScreen).GetNodeOrNull<Node>(AscensionPanelNodeName) ??
            ((Node)sourceCharacterSelectScreen).GetNodeOrNull<Node>(UniqueAscensionPanelNodePath);

        if (sourceAscensionPanel == null)
        {
            Log.Warn("[AITeammate] Could not find AscensionPanel on NCharacterSelectScreen while creating the AI teammate setup screen.");
            return;
        }

        var duplicate = sourceAscensionPanel.Duplicate(DuplicateNodeFlags);
        if (duplicate is Control duplicateControl)
        {
            ResetAscensionPanelGeometry(duplicateControl);
        }

        AddChild(duplicate);
        if (duplicate is NAscensionPanel ascensionPanel)
        {
            _ascensionPanel = ascensionPanel;
            ((GodotObject)ascensionPanel).Connect(
                NAscensionPanel.SignalName.AscensionLevelChanged,
                Callable.From((Action)OnAscensionPanelLevelChanged),
                0u);
        }
    }

    public void ApplyAiSlotSelection(int slotIndex, string characterId)
    {
        if (slotIndex < 0 || !AiTeammatePlaceholderCharacters.TryGetById(characterId, out var character))
        {
            return;
        }

        _slotSelections[slotIndex] = characterId;
        UpdateAiSlotVisual(slotIndex, character);
        RefreshSessionFromSelections();
    }

    public void ClearAiSlotSelection(int slotIndex)
    {
        if (slotIndex < 0)
        {
            return;
        }

        _slotSelections.Remove(slotIndex);
        ResetAiSlotVisual(slotIndex);
        RefreshSessionFromSelections();
        Log.Info($"[AITeammate] Cleared character selection from slot {slotIndex}.");
    }

    private void OpenCharacterPicker(int slotIndex)
    {
        Log.Info($"[AITeammate] Placeholder slot clicked: {slotIndex}.");

        if (_sourceSingleplayerSubmenu == null || _stack == null)
        {
            Log.Warn("[AITeammate] Could not open the AI slot picker because the stock submenu template or submenu stack was unavailable.");
            return;
        }

        var pickerScreen = _pickerScreen;
        if (pickerScreen == null || !GodotObject.IsInstanceValid(pickerScreen))
        {
            pickerScreen = AiTeammateSlotCharacterPickerScreen.CreateFromTemplate(_sourceSingleplayerSubmenu, PickerScreenNodeName);
            _pickerScreen = pickerScreen;
            ((CanvasItem)pickerScreen).Visible = false;
            ((Node)(object)_stack).AddChild(pickerScreen);
        }

        _slotSelections.TryGetValue(slotIndex, out var selectedCharacterId);
        pickerScreen.BeginSelection(this, slotIndex, selectedCharacterId);
        _stack.Push(pickerScreen);
    }

    private void UpdateAiSlotVisual(int slotIndex, AiTeammatePlaceholderCharacter character)
    {
        if (_slotPortraits.TryGetValue(slotIndex, out var portrait))
        {
            portrait.Texture = AiTeammatePlaceholderCharacters.LoadTexture(character.TexturePath);
            portrait.Visible = portrait.Texture != null;
        }

        if (_slotTitles.TryGetValue(slotIndex, out var titleLabel))
        {
            titleLabel.Text = character.DisplayName;
        }

        if (_slotSubtitles.TryGetValue(slotIndex, out var subtitleLabel))
        {
            subtitleLabel.Text = slotIndex == 0 ? "Host" : "AI teammate";
        }

        RefreshSlotRemoveButton(slotIndex);
    }

    private void ResetAiSlotVisual(int slotIndex)
    {
        if (_slotPortraits.TryGetValue(slotIndex, out var portrait))
        {
            portrait.Texture = null;
            portrait.Visible = false;
        }

        if (_slotTitles.TryGetValue(slotIndex, out var titleLabel))
        {
            titleLabel.Text = slotIndex == 0 ? "Human Player" : $"AI Player {slotIndex}";
        }

        if (_slotSubtitles.TryGetValue(slotIndex, out var subtitleLabel))
        {
            subtitleLabel.Text = slotIndex == 0 ? "Required" : "Optional";
        }

        RefreshSlotRemoveButton(slotIndex);
    }

    private Button CreateRemoveButton(int slotIndex)
    {
        var removeButton = new Button
        {
            Name = $"RemoveSlot{slotIndex}Button",
            Text = "X",
            Visible = false,
            FocusMode = FocusModeEnum.None,
            MouseFilter = MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(30f, 30f)
        };
        removeButton.SetAnchorsPreset(LayoutPreset.TopRight);
        removeButton.OffsetLeft = -34f;
        removeButton.OffsetTop = 4f;
        removeButton.OffsetRight = -4f;
        removeButton.OffsetBottom = 34f;
        removeButton.AddThemeStyleboxOverride("normal", CreatePanelStyle(RemoveButtonColor, Colors.White, 2, 12));
        removeButton.AddThemeStyleboxOverride("hover", CreatePanelStyle(RemoveButtonHoverColor, Colors.White, 2, 12));
        removeButton.AddThemeStyleboxOverride("pressed", CreatePanelStyle(RemoveButtonColor.Darkened(0.12f), Colors.White, 3, 12));
        removeButton.AddThemeColorOverride("font_color", Colors.White);
        removeButton.AddThemeFontSizeOverride("font_size", 16);
        removeButton.MouseEntered += () =>
        {
            _slotRemoveHoverStates.Add(slotIndex);
            RefreshSlotRemoveButton(slotIndex);
        };
        removeButton.MouseExited += () =>
        {
            _slotRemoveHoverStates.Remove(slotIndex);
            RefreshSlotRemoveButton(slotIndex);
        };
        removeButton.Pressed += () => ClearAiSlotSelection(slotIndex);
        return removeButton;
    }

    private void RefreshSlotRemoveButton(int slotIndex)
    {
        if (!_slotRemoveButtons.TryGetValue(slotIndex, out var removeButton))
        {
            return;
        }

        var hasSelection = _slotSelections.ContainsKey(slotIndex);
        var hovered = _slotHoverStates.Contains(slotIndex) || _slotRemoveHoverStates.Contains(slotIndex);
        removeButton.Visible = hasSelection && hovered;
    }

    private static void ResetBackButtonGeometry(Control control)
    {
        // Reset to the authored stock scene geometry instead of inheriting the hidden source button's runtime state.
        control.AnchorLeft = 0f;
        control.AnchorTop = 1f;
        control.AnchorRight = 0f;
        control.AnchorBottom = 1f;
        control.OffsetLeft = -40f;
        control.OffsetTop = -354f;
        control.OffsetRight = 160f;
        control.OffsetBottom = -244f;
        control.GrowVertical = GrowDirection.Begin;
        control.PivotOffset = BackButtonPivotOffset;
        control.Scale = Vector2.One;
    }

    private static void ResetAscensionPanelGeometry(Control control)
    {
        // Reset to the authored character-select scene geometry instead of inheriting live runtime layout state.
        // The character-select screen overrides the standalone ascension panel scene to sit 120 px further left.
        control.AnchorLeft = 0.5f;
        control.AnchorTop = 1f;
        control.AnchorRight = 0.5f;
        control.AnchorBottom = 1f;
        control.OffsetLeft = AscensionPanelPosition.X;
        control.OffsetTop = AscensionPanelPosition.Y;
        control.OffsetRight = AscensionPanelPosition.X + AscensionPanelSize.X;
        control.OffsetBottom = AscensionPanelPosition.Y + AscensionPanelSize.Y;
        control.GrowHorizontal = GrowDirection.Both;
        control.GrowVertical = GrowDirection.Begin;
        control.Position = AscensionPanelPosition;
        control.Modulate = Colors.White;
        control.Rotation = 0f;
        control.PivotOffset = Vector2.Zero;
        control.Scale = Vector2.One;
        control.Visible = true;
    }

    private void OnAscensionPanelLevelChanged()
    {
        if (_isSyncingAscensionPanel || _ascensionPanel == null)
        {
            return;
        }

        int targetAscension = _ascensionPanel.Ascension;
        _selectedAscensionLevel = targetAscension;

        if (_lobby != null && _lobby.NetService.Type != NetGameType.Client && _lobby.Ascension != targetAscension)
        {
            _lobby.SyncAscensionChange(targetAscension);
            _selectedAscensionLevel = _lobby.Ascension;
        }
    }

    private void RefreshAscensionPanelState()
    {
        if (_ascensionPanel == null || !GodotObject.IsInstanceValid(_ascensionPanel) || !_ascensionPanel.IsNodeReady())
        {
            return;
        }

        int maxAscension = _lobby?.MaxAscension ?? 0;
        int ascension = Math.Clamp(_lobby?.Ascension ?? _selectedAscensionLevel, 0, maxAscension);

        _isSyncingAscensionPanel = true;
        try
        {
            _ascensionPanel.SetMaxAscension(maxAscension);
            _ascensionPanel.SetAscensionLevel(ascension);
        }
        finally
        {
            _isSyncingAscensionPanel = false;
        }

        _selectedAscensionLevel = ascension;
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
        style.ContentMarginLeft = 20f;
        style.ContentMarginTop = 20f;
        style.ContentMarginRight = 20f;
        style.ContentMarginBottom = 20f;
        return style;
    }
}
