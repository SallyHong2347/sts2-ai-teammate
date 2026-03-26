using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace AITeammate.Scripts;

public partial class AiTeammateContinueRunScreen : NSubmenu
{
    private const string DefaultDescription = "Continue your saved AI teammate run, or abandon it and return to setup.";
    private const string MissingSaveDescription = "No valid AI teammate save was found. Press Back to return, or reopen the mode to start a new setup flow.";
    private static readonly Color PageBackgroundColor = new(0.16f, 0.24f, 0.31f, 0.95f);
    private static readonly Color BorderColor = new(0.58f, 0.71f, 0.8f, 0.95f);
    private static readonly Color ContinueColor = new(0.18f, 0.52f, 0.34f, 1f);
    private static readonly Color ContinueHoverColor = new(0.24f, 0.62f, 0.41f, 1f);
    private static readonly Color AbandonColor = new(0.70f, 0.18f, 0.18f, 1f);
    private static readonly Color AbandonHoverColor = new(0.80f, 0.24f, 0.24f, 1f);

    private Button? _continueButton;
    private Button? _abandonButton;
    private Label? _descriptionLabel;

    protected override Control? InitialFocusedControl => _continueButton;

    public static AiTeammateContinueRunScreen CreateFromTemplate(NSingleplayerSubmenu sourceSingleplayerSubmenu, string nodeName)
    {
        AiTeammateContinueRunScreen screen = new();
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
        base.OnSubmenuOpened();
        RefreshState();
    }

    private void BuildLayout(NSingleplayerSubmenu sourceSingleplayerSubmenu)
    {
        AiTeammateMenuUiFactory.CopySubmenuLayoutFrom(this, sourceSingleplayerSubmenu);
        AiTeammateMenuUiFactory.TryDuplicateStockBackButton(this, sourceSingleplayerSubmenu, "creating the AI teammate continue page");

        Panel panel = new()
        {
            Name = "AiTeammateContinuePanel",
            MouseFilter = MouseFilterEnum.Stop
        };
        panel.SetAnchorsPreset(LayoutPreset.Center);
        panel.OffsetLeft = -360f;
        panel.OffsetTop = -210f;
        panel.OffsetRight = 360f;
        panel.OffsetBottom = 210f;
        panel.AddThemeStyleboxOverride("panel", CreatePanelStyle(PageBackgroundColor, BorderColor));
        AddChild(panel);

        Label title = new()
        {
            Text = "AI Teammate Run Found",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.SetAnchorsPreset(LayoutPreset.TopWide);
        title.OffsetLeft = 24f;
        title.OffsetTop = 32f;
        title.OffsetRight = -24f;
        title.OffsetBottom = 78f;
        title.AddThemeFontSizeOverride("font_size", 30);
        panel.AddChild(title);

        _descriptionLabel = new Label
        {
            Text = DefaultDescription,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _descriptionLabel.SetAnchorsPreset(LayoutPreset.TopWide);
        _descriptionLabel.OffsetLeft = 36f;
        _descriptionLabel.OffsetTop = 92f;
        _descriptionLabel.OffsetRight = -36f;
        _descriptionLabel.OffsetBottom = 142f;
        _descriptionLabel.Modulate = new Color(0.88f, 0.93f, 0.97f, 0.88f);
        panel.AddChild(_descriptionLabel);

        _continueButton = CreateActionButton("Continue Run", ContinueColor, ContinueHoverColor);
        _continueButton.SetAnchorsPreset(LayoutPreset.Center);
        _continueButton.OffsetLeft = -220f;
        _continueButton.OffsetTop = 10f;
        _continueButton.OffsetRight = 220f;
        _continueButton.OffsetBottom = 72f;
        _continueButton.Pressed += OnContinuePressed;
        panel.AddChild(_continueButton);

        _abandonButton = CreateActionButton("Abandon Run", AbandonColor, AbandonHoverColor);
        _abandonButton.SetAnchorsPreset(LayoutPreset.Center);
        _abandonButton.OffsetLeft = -220f;
        _abandonButton.OffsetTop = 92f;
        _abandonButton.OffsetRight = 220f;
        _abandonButton.OffsetBottom = 154f;
        _abandonButton.Pressed += OnAbandonPressed;
        panel.AddChild(_abandonButton);
    }

    private void RefreshState()
    {
        bool hasSavedRun = AiTeammateSaveSupport.HasContinueableSavedRun();
        if (_continueButton != null)
        {
            _continueButton.Disabled = !hasSavedRun;
        }

        if (_abandonButton != null)
        {
            _abandonButton.Disabled = false;
        }

        if (_descriptionLabel != null)
        {
            _descriptionLabel.Text = hasSavedRun ? DefaultDescription : MissingSaveDescription;
        }
    }

    private void OnContinuePressed()
    {
        if (_continueButton == null || _abandonButton == null)
        {
            return;
        }

        _continueButton.Disabled = true;
        _abandonButton.Disabled = true;
        TaskHelper.RunSafely(ContinueAsync());
    }

    private async Task ContinueAsync()
    {
        bool loaded = await AiTeammateSaveSupport.ContinueSavedRunAsync();
        if (!loaded)
        {
            RefreshState();
        }
    }

    private void OnAbandonPressed()
    {
        AiTeammateSaveSupport.AbandonSavedRun();
        _stack.Pop();
        Log.Info("[AITeammate] Abandoned saved AI teammate run and returned to the main menu.");
    }

    private static Button CreateActionButton(string text, Color normalColor, Color hoverColor)
    {
        Button button = new()
        {
            Text = text,
            FocusMode = FocusModeEnum.All,
            MouseFilter = MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(440f, 62f)
        };
        button.AddThemeStyleboxOverride("normal", CreatePanelStyle(normalColor, Colors.White));
        button.AddThemeStyleboxOverride("hover", CreatePanelStyle(hoverColor, Colors.White));
        button.AddThemeStyleboxOverride("pressed", CreatePanelStyle(normalColor.Darkened(0.14f), Colors.White));
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeFontSizeOverride("font_size", 22);
        return button;
    }

    private static StyleBoxFlat CreatePanelStyle(Color background, Color border)
    {
        return AiTeammateMenuUiFactory.CreateRoundedPanelStyle(background, border, 2, 18, contentMargin: 0f);
    }
}
