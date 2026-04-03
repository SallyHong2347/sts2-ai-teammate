# Modding Decisions

## Beta-402 Compatibility

- Date checked: 2026-04-03
- Decompiled source used: `C:\Users\hongs\Desktop\sts2PckBeta402`

Chosen patch point:
- `Scripts/Session/AiTeammateSaveSupport.cs`
- Method: `AiTeammateSaveSupport.AbandonSavedRun`

Why this location:
- `dotnet build` against the updated game DLLs failed on a single changed API call here.
- The new game version expects `ScoreUtility.CalculateScore(SerializableRun run, ulong playerId, bool won)`.
- This was the smallest viable compatibility fix and produced a clean build without broader code churn.

Implementation:
- Resolved the local player id with `PlatformUtil.GetLocalPlayerId(savedRun.SaveData.PlatformType)`.
- Passed that id into `ScoreUtility.CalculateScore`.

## Config Packaging Layout

Chosen patch point:
- `sts2AITeammate.csproj`
- Target: `Copy Mod`

Why this location:
- The existing build copy step flattened `config\ai-behavior\...` into `mods\sts2AITeammate\ai-behavior\...`.
- The intended install layout is `mods\sts2AITeammate\config\ai-behavior\...`.

Implementation:
- Switched the config copy step from `%(RecursiveDir)` to `%(RelativeDir)` so the top-level `config` folder is preserved during post-build copy.

Checked but not changed:
- Main menu reflection targets still exist in beta-402:
  - `NMainMenu.MainMenuButtonFocused`
  - `NMainMenu.MainMenuButtonUnfocused`
  - `NMainMenu._lastHitButton`
  - `NMainMenuTextButton._locString`
- String-based Harmony patch targets used by the mod were still present in the decompiled beta-402 source during this pass.

Assumption:
- The main remaining risk is runtime behavior rather than compile-time API mismatch, because in-game verification has not been run yet.
