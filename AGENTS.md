# AGENTS.md

This file gives repo-specific instructions to coding agents working on this project.

## Project context
This project is a Slay the Spire 2 mod prototype focused on building an AI teammate / AI-player feature.

The user is still learning:
- C#
- modding workflow
- game launch/debug workflow
- how to inspect decompiled game code

The goal is to maintain steady progress with small, verifiable steps.

---

## Main workflow rules
- Prefer small, incremental changes.
- Prefer changes that are easy to verify in-game.
- Each task should produce one visible or testable result.
- Do not perform large refactors unless clearly necessary.
- Preserve existing user code and comments unless clearly obsolete.

---

## Game folder usage
We use a separate ModDev copy of the game:

C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2 - ModDev

Rules:
- Treat this as the working game directory.
- Do not modify the original Steam installation.
- Minimize changes inside the ModDev folder.
- Prefer creating new helper/debug files instead of modifying original ones.
- Clearly name helper files (e.g., *_dev.bat, *_debug.log).

---

## Decompiled code usage (VERY IMPORTANT)
A full decompiled version of the game is available at:

C:\Users\hongs\Desktop\dev\sts2Code\sts2DecomposedCode

Rules:
- Always refer to this codebase when:
  - searching for classes
  - identifying UI/menu logic
  - finding patch points
  - understanding game structure
- Prefer using real class/method names from the decompiled code instead of guessing.
- Do not rely only on assumptions or generic patterns.
- When selecting a patch point:
  - identify the exact class and method from the decompiled code
  - briefly explain why it is a good target

---

## Documentation maintenance
Always ensure a `docs/` folder exists.

Maintain only these files:
- `docs/debugging-notes.md`
- `docs/modding-decisions.md`

### debugging-notes.md
Keep short, practical notes:
- launch commands
- flags
- log locations
- verification steps
- common issues

### modding-decisions.md
Record decisions and reasoning:
- chosen patch points
- UI approach
- workflow changes
- rejected approaches (if relevant)

---

## Code change guidance
- Favor minimal, targeted changes.
- Prefer patching/extending existing logic instead of rewriting systems.
- Add clear logging for new behavior:
  - mod loaded
  - menu injected
  - button clicked
- Use simple and readable code.

---

## Implementation approach
When implementing a feature:

1. Identify relevant classes/methods using the decompiled code.
2. Choose the smallest viable patch point.
3. Implement a minimal version first (placeholder behavior is fine).
4. Add logging for verification.
5. Ensure the result is visible/testable in-game.

Do not jump directly to full feature implementation.

---

## Communication guidance
When summarizing work:
- what was changed
- which class/method was used
- why that location was chosen
- how to test it
- expected result in-game
- what was added to docs

Separate facts from assumptions.

---

## Preferred mentality
The goal is not perfect understanding.

The goal is:
- keep moving
- use real game code as source of truth
- validate changes quickly in-game
- build confidence through small working steps