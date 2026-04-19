# Version Branches

This repository maintains separate branches for different Slay the Spire 2 public builds.

## Target branches

- `beta-326`
  Targets the public beta build updated on March 26.
- `default-314`
  Currently tracks the public default build updated on April 16, 2026. (The branch name is historical — it was first cut against the March 14 build and has since been rolled forward to later default releases.)

## Why the split exists

The March 14 and March 26 game builds differ in several multiplayer-facing APIs used by this mod.

The main breakpoints are:

- `IStartRunLobbyListener.PlayerChanged`
- `StartRunLobby` begin-run method names and internal field names
- `ActModel.GetRandomList` parameters
- map vote source types
- treasure relic vote return types

Keeping separate branches is simpler and safer than trying to support both builds in one runtime code path.

## Build guidance

- On `default-314`, build against the March 14 public default game files.
- On `beta-326`, build against the March 26 public beta game files.

If your local game path differs, set `Sts2Dir` in `sts2AITeammate.csproj`, set `STS2_DIR`, or pass `-p:Sts2Dir=...` to `dotnet build`.
