# Install

## For players

1. Install the mod loader and any dependencies required for Slay the Spire 2 mods.
2. Extract the release archive.
3. Place the `sts2AITeammate` mod folder in your game's `mods/` directory.
4. Launch the game with mods enabled.

The mod ships with per-character behavior config files under:

`mods/sts2AITeammate/config/ai-behavior/`

You do not need to edit these files to use the mod. They are there for users who want to tune AI behavior by hand.

## How to verify it loaded

- Start the game with mods enabled.
- Look for the AI Teammate menu/setup entry added by the mod.
- Start an AI teammate run and confirm AI companions appear in the run setup and act on their own once the run begins.

## How to uninstall

- Remove the `sts2AITeammate` folder from your game's `mods/` directory.
- If you made manual edits to the shipped config files, those edits will be removed with the mod folder unless you backed them up elsewhere.

## For local builds

Before building, point the project at your Slay the Spire 2 install by setting either:

- the `STS2_DIR` environment variable
- or the `Sts2Dir` MSBuild property

Example PowerShell session:

```powershell
$env:STS2_DIR = "D:\Games\Slay the Spire 2 - ModDev"
dotnet build
```

The post-build step copies the mod output into `$(Sts2Dir)\mods\sts2AITeammate\`.
