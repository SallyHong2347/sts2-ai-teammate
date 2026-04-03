# Debugging Notes

## Build

- Default game path comes from `sts2AITeammate.csproj` via `Sts2Dir`.
- Override with `STS2_DIR` if needed.
- Build command:

```powershell
dotnet build
```

## Logs

- Recommended launch helper from [`README.md`](/c:/Users/hongs/Desktop/sts2AITeammateProject/sts2-ai-teammate/README.md): `launch_opengl_moddev_debug.bat`
- Captured mod log example:

```powershell
./launch_opengl_moddev_debug.bat > ./mods/sts2AITeammate/aiteammate_launch_debug.log 2>&1
```

## Beta-402 Check

- April 2 beta changed `ScoreUtility.CalculateScore` to require `playerId`.
- Updated mod call site in `AiTeammateSaveSupport.AbandonSavedRun`.
- Quick verification:
  - build the mod
  - launch the game
  - open the AI teammate menu from the main menu
  - if you have a saved AI teammate run, test the abandon flow
  - confirm there is no crash when abandoning a daily run save
  - confirm shipped config files install under `mods\sts2AITeammate\config\ai-behavior\`
