# AI Teammate

## What this mod does

AI Teammate adds AI-controlled teammates to a local fake-multiplayer run in Slay the Spire 2.

Instead of playing solo, you can start a run with one or more AI companions and let them take their own turns, handle rewards, and participate in the run alongside you.

This is an experimental mod. The AI is functional and can complete a lot of normal gameplay flow, but it is not a high-level strategic bot. It can make inefficient, strange, or outright bad decisions.

## Main features

- Adds AI-controlled teammates to a local fake-multiplayer run
- Supports multiple teammate characters with different behavior profiles
- Includes per-character JSON config files for users who want to tune AI behavior
- Handles combat, rewards, potions, shops, and many events through a shared AI system
- Uses conservative fallback behavior in places where bespoke support is incomplete

## How to use it

1. Install the mod and its required dependencies.
2. Launch the game with mods enabled.
3. Open the AI Teammate setup flow from the mod's menu entry.
4. Choose the characters you want for the host slot and AI teammate slots.
5. Start the run.

During the run, the AI teammates will act on their own when it is their turn or when they need to make supported combat, reward, shop, potion, or event decisions.

## AI expectations

- The AI is meant to be a working experimental teammate, not an optimized bot.
- It can finish combats, claim rewards, use potions, shop, and make many event choices.
- Different characters can behave differently because the mod ships with per-character behavior configs.
- Some content is handled conservatively for stability rather than trying to be clever.

## Current limitations

- Decision quality is limited. The AI can still misplay turns, take awkward rewards, or make strange event and utility choices.
- Some events still rely on generic fallback behavior instead of event-specific intelligence.
- One risky event, `CrystalSphere`, is excluded from AI teammate event pools for release safety.
- Edge cases and bugs may still exist, especially in less common multiplayer-adjacent flows.

## Configuration

The shipped mod includes editable per-character config files under:

`mods/sts2AITeammate/config/ai-behavior/`

Most players do not need to touch these files, but tinkerers can use them to adjust how aggressive, defensive, selective, or cautious each AI teammate feels.

## Installation

See the full install guide here:

- [INSTALL.md](INSTALL.md)

## Known limitations

See the player-facing limitations page here:

- [LIMITATIONS.md](LIMITATIONS.md)

## Source / GitHub

- GitHub: `<repo link here>`

## Support and maintenance

This is a hobby / experimental project shared in a usable public state. Support may be limited, and long-term maintenance is not guaranteed.

The source code is available for people who want to inspect, tweak, or build on the mod.

## Credits

- Mod implementation and release assembly by the project author
- Built through iterative AI-assisted development and experimentation
