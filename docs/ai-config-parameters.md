# AI Config Parameters Guide

This guide explains what each setting in the `.aiconfig` files does, from a player's perspective. You don't need to set every field - any field you leave out will inherit from `default.aiconfig`, and anything still missing falls back to built-in defaults.

Files live in `config/ai-behavior/` (or `mods/sts2AITeammate/config/ai-behavior/` when installed).

---

## Timing

Controls how fast the AI plays.

| Field | Default | Range | What it does |
|---|---|---|---|
| `actionIntervalMs` | 0 | 0 - 2000 | Extra delay (milliseconds) the AI waits between playing cards or using potions. At 0 the AI plays at natural speed. Try 300-600 if the AI feels too fast. |

```json
"timing": {
  "actionIntervalMs": 400
}
```

---

## Combat

Controls how the AI fights during combat encounters.

### `combat.coreWeights` - Basic value assignments

| Field | Default | What it does |
|---|---|---|
| `directDamageValuePerPoint` | 5 | How much the AI values each point of damage dealt |
| `attackWhileDefenseNeededPenalty` | 18 | How much the AI avoids attacking when it has unblocked incoming damage |
| `targetLowHealthBiasThreshold` | 24 | Enemy HP below which the AI gets a bonus for finishing them off |
| `targetLowHealthBiasValuePerPoint` | 1 | Extra value per HP below that threshold |
| `attackingTargetBonus` | 8 | Bonus for attacking enemies that are attacking you |
| `utilityValueWhenThreatened` | 10 | Value of utility cards (draw, buff) when under threat |
| `utilityValueWhenSafe` | 18 | Value of utility cards when the AI is safe |
| `lineDamageValuePerPoint` | 3 | How much multi-turn damage plans are valued per point |
| `leftoverBlockValuePerPoint` | 4 | How much the AI values block that remains after damage |

### `combat.statusWeights` - Buffs and debuffs

These control how the AI values applying or receiving status effects. Higher values make the AI prioritize that effect more.

**Strength (damage buff)**

| Field | Default | What it does |
|---|---|---|
| `strengthPerHitValue` | 1 | Value per point of strength per hit on an attack |
| `selfTemporaryStrengthValue` | 8 | Value of gaining temporary strength this turn |
| `temporaryStrengthPerAffordableAttackValue` | 8 | Extra value per attack card the AI can still play this turn |
| `temporaryStrengthMinimumValue` | 6 | Minimum value even without follow-up attacks |
| `persistentStrengthPerAffordableAttackValue` | 5 | Same as above but for permanent strength |
| `persistentStrengthMinimumValue` | 4 | Minimum value for permanent strength |

**Dexterity (block buff)**

| Field | Default | What it does |
|---|---|---|
| `temporaryDexterityPerAffordableBlockValue` | 10 | Extra value per block card the AI can play this turn |
| `temporaryDexterityMinimumValue` | 8 | Minimum value even without follow-up blocks |
| `persistentDexterityPerAffordableBlockValue` | 6 | Same for permanent dexterity |
| `persistentDexterityMinimumValue` | 4 | Minimum value for permanent dexterity |
| `temporaryDexterityWithFollowUpBlockValue` | 18 | Value when the AI has block cards to play after |
| `temporaryDexterityThreatenedBlockValue` | 12 | Value when threatened but no block follow-up |
| `temporaryDexteritySafeBlockValue` | 6 | Value when safe |
| `persistentDexterityWithBlockValue` | 10 | Permanent dexterity when block cards exist in deck |
| `persistentDexterityWithoutBlockValue` | 4 | Permanent dexterity without block sources |

**Enemy debuffs**

| Field | Default | What it does |
|---|---|---|
| `vulnerableWithFollowUpValue` | 16 | Value of making an enemy vulnerable when the AI has attacks to follow up |
| `vulnerableWithoutFollowUpValue` | 6 | Value of vulnerable without follow-up attacks |
| `weakImmediateDefenseValue` | 12 | Defensive value of applying weak (reduces incoming damage) |
| `weakDebuffValue` | 5 | General value of applying weak as a debuff |

**Multi-turn planning modifiers** (prefixed `setup` or `line`)

These adjust how much the AI values buffs and debuffs when planning ahead across multiple actions. `setup` variants apply to power-type actions, `line` variants apply to multi-action plans.

### `combat.resourceWeights` - Cards and energy

| Field | Default | What it does |
|---|---|---|
| `drawValueWhenPlayable` | 10 | Value of drawing a card when the AI can play it |
| `drawPenaltyWhenNotPlayable` | 8 | Penalty for drawing when hand is full / no energy |
| `energyGainValue` | 18 | How much the AI values gaining extra energy |
| `energyEfficiencyValue` | 4 | Bonus for cards that cost less energy |
| `setupActionBonus` | 10 | Extra value for power-type setup actions |
| `remainingEnergyPenalty` | 18 | How much the AI dislikes ending the turn with unspent energy |
| `remainingAffordableActionsPenalty` | 24 | How much the AI dislikes ending with playable cards still in hand |
| `endTurnWhenSkippingPotionsBonus` | 24 | Bonus for ending turn when no potions are worth using |
| `endTurnWhileOtherActionsExistPenalty` | 10000 | Prevents the AI from ending the turn when it still has good actions |
| `selfTargetPreferenceBonus` | 6 | When the AI can target itself or an ally with a buff/resource card, this bonus is added to the self-target score. Higher values make the AI prefer buffing itself over allies. |

### `combat.riskProfile` - Aggression vs defense personality

These are the most impactful personality knobs. They control how aggressive or defensive the AI plays.

**Weight multipliers** (scale all scores in that category)

| Field | Default | Range | What it does |
|---|---|---|---|
| `survivalWeight` | 1.0 | 0.1 - 5.0 | Multiplier on all survival-related scores |
| `defenseWeight` | 1.0 | 0.1 - 5.0 | Multiplier on all defensive scores |
| `attackWeight` | 1.0 | 0.1 - 5.0 | Multiplier on all offensive scores |
| `aggressiveness` | 1.0 | 0.1 - 5.0 | Additional multiplier on attack scores (stacks with attackWeight) |

> Example: Ironclad uses `attackWeight: 1.12` and `aggressiveness: 1.1` to be more aggressive. Regent uses `defenseWeight: 1.12` and `attackWeight: 0.94` to play defensively.

**Tactical bonuses and penalties**

| Field | Default | What it does |
|---|---|---|
| `lethalPriorityBonus` | 55 | How much the AI prioritizes killing blows |
| `lethalIncomingDamageValue` | 8 | Extra lethal priority when taking lots of damage |
| `blockedDamageValuePerPoint` | 10 | Value per point of damage that block prevents |
| `excessBlockValuePerPoint` | 2 | Value of block beyond what's needed |
| `fullBlockCoverageBonus` | 50 | Bonus for completely blocking all incoming damage |
| `lowHealthEmergencyDefenseBonus` | 35 | Bonus for defensive actions when at low HP |
| `preventedDamageValuePerPoint` | 18 | Value per point of damage prevented |
| `damageTakenPenaltyPerPoint` | 30 | How much the AI dislikes taking damage |
| `killPreventionValuePerPoint` | 10 | Value of preventing lethal damage |
| `weakPreventionValuePerPoint` | 10 | Value of preventing weak debuff |
| `deadEnemyReward` | 45 | Score bonus for killing an enemy |
| `perfectDefenseBonus` | 60 | Bonus for taking zero damage in a turn |
| `exposedDamageWithoutDefensePenalty` | 35 | Penalty when taking damage with no defensive actions |

**Danger thresholds**

These control when the AI switches into emergency/panic mode:

| Field | Default | Range | What it does |
|---|---|---|---|
| `lowHealthEmergencyThreshold` | 12 | 1 - 50 | HP at or below which the AI treats itself as in a low-health emergency. The actual threshold is `max(this value, incoming damage)`, so it also kicks in when incoming damage is high. Lower = the AI stays calm longer. Higher = the AI panics earlier. |
| `graveDangerFloor` | 10 | 1 - 50 | Minimum uncovered damage that triggers "grave danger" mode for potions. When grave danger activates, defensive potions get a large bonus and the AI becomes much more willing to use them. |
| `graveDangerHpFraction` | 0.333 | 0.05 - 0.75 | Fraction of current HP used as an alternative grave danger threshold. The AI enters grave danger when uncovered damage >= `max(graveDangerFloor, HP * graveDangerHpFraction)`. At 0.333, it triggers when uncovered damage is at least a third of your HP. |

> Example: If the AI has 60 HP, 5 block, and enemies are attacking for 30:
> - Uncovered damage = 30 - 5 = 25
> - HP fraction threshold = 60 * 0.333 = 20
> - Grave danger floor = 10
> - Threshold = max(10, 20) = 20
> - 25 >= 20 → grave danger is active, potions get big bonuses

---

## Potions

### `potions.combatUse` - When to drink potions in combat

| Field | Default | What it does |
|---|---|---|
| `normalFightBaseScore` | -160 | Base score for using a potion in a normal fight. Negative = the AI strongly avoids wasting potions on regular enemies. |
| `eliteBossBaseScore` | 18 | Base score in elite/boss fights. Positive = the AI is willing to use potions. |
| `eliteBossBonus` | 12 | Additional bonus in elite/boss fights |
| `graveDangerDefensiveBonus` | 160 | Bonus for defensive potions when in grave danger |
| `graveDangerOffensiveBonus` | 60 | Bonus for offensive potions when in grave danger |
| `eliteBossOffensiveFollowUpBonus` | 95 | Bonus for offensive potions in elite/boss when the AI has attacks to follow up |
| `normalFightOffensiveFollowUpBonus` | 25 | Same but in normal fights |
| `attackingTargetBonus` | 8 | Bonus for targeting an enemy that is attacking |
| `lowHealthTargetPenalty` | 18 | Penalty for targeting an already-low-health enemy (overkill) |
| `allyDamagePenaltyPerAlly` | 12 | Penalty per ally that would be hurt by a potion with ally-damage side effects. Higher = more careful about splashing allies. |

### `potions.acquisition` - Picking up potions

These control how the AI values gaining new potions from rewards and shops. Key fields:

| Field | Default | What it does |
|---|---|---|
| `rareBaseline` / `uncommonBaseline` / `commonBaseline` | 12 / 8 / 5 | Base value by rarity |
| `noOpenSlotPenalty` | 20.0 | Penalty when all potion slots are full |
| `defensiveCoverageLowNeedBonus` | 6.0 | Bonus for defensive potions when the deck lacks defense |
| `offensiveCoverageLowNeedBonus` | 6.0 | Bonus for offensive potions when the deck lacks offense |
| `tempoCoverageLowNeedBonus` | 7.0 | Bonus for tempo potions when the deck lacks draw/energy |
| `highLeverageEmergencyBonus` | 8.0 | Extra value for emergency potions (Fairy in a Bottle, etc.) |

### `potions.rewardHandling`

| Field | Default | What it does |
|---|---|---|
| `replacementThreshold` | 1.0 | Minimum score advantage a new potion needs over the worst held potion to replace it |

---

## Card Rewards

### `cardRewards.intrinsicWeights` - Card value by properties

These control how the AI scores individual cards based on their stats.

| Field | Default | What it does |
|---|---|---|
| `damageValuePerPoint` | 0.55 | Value per point of damage on a card |
| `blockValuePerPoint` | 0.45 | Value per point of block on a card |
| `drawValue` | 8.0 | Value per card drawn |
| `energyValue` | 12.0 | Value per energy generated |
| `vulnerableValue` / `weakValue` | 5.0 / 5.5 | Value of applying debuffs |
| `persistentStrengthValue` / `persistentDexterityValue` | 6.0 / 6.0 | Value of permanent stat buffs |
| `powerBonus` | 2.0 | Bonus for power-type cards |
| `zeroCostBonus` | 4.0 | Bonus for 0-cost cards |
| `highCostPenaltyPerExtraEnergy` | 2.25 | Penalty per energy above 1 |
| `retainBonus` | 2.0 | Bonus for cards that retain in hand |
| `etherealPenalty` | 6.0 | Penalty for ethereal cards |
| `rareBonus` / `uncommonBonus` / `basicBonus` | 6.0 / 3.0 / -3.0 | Value by rarity |
| `cursePenalty` / `statusPenalty` | -35.0 / -25.0 | Penalty for curses and status cards |

### `cardRewards.synergyWeights` - Deck synergy

These control how the AI values cards that synergize with the current deck.

| Field | Default | What it does |
|---|---|---|
| `drawWithHighCurveValue` | 3.5 | Extra draw value when deck has expensive cards |
| `energyWithHeavyCurveValue` | 5.0 | Extra energy value when deck is heavy |
| `attackScalingSynergyPerAttack` | 0.9 | Strength/vulnerable value per attack card in deck |
| `defenseScalingSynergyPerBlockSource` | 0.9 | Dexterity value per block source in deck |
| `damageNeedScale` / `damageNeedCap` | 3.0 / 12.0 | Extra value for damage cards when the deck needs more |
| `blockNeedScale` / `blockNeedCap` | 3.0 / 12.0 | Extra value for block cards when the deck needs more |
| `highAscensionBlockBonus` | 1.0 | Extra block value at ascension 10+ |

### `cardRewards.disciplineWeights` - Deck discipline

These keep the AI from bloating the deck or taking bad cards.

| Field | Default | What it does |
|---|---|---|
| `duplicatePenaltyPerCopy` | 4.0 | Penalty per copy of a card already in the deck |
| `excessDrawPenalty` / `excessEnergyPenalty` | 4.0 / 5.0 | Penalty when the deck already has enough draw/energy |
| `excessDamagePenaltyScale` / `excessBlockPenaltyScale` | 5.0 / 5.0 | Penalty when the deck has too much damage/block |
| `rewardSkipThreshold` | 12.0 | Minimum score to take a card from rewards (below this, skip) |
| `shopSkipThresholdBase` | 22.0 | Minimum score to buy a card from shop |
| `shopSkipThresholdCostFactor` | 0.1 | Extra skip threshold per gold cost |

---

## Shop

### `shop.offerPriorities` - Shopping personality

| Field | Default | What it does |
|---|---|---|
| `cardPurchaseBias` | 0.0 | Push toward (+) or away from (-) buying cards |
| `relicPurchaseBias` | 0.0 | Push toward (+) or away from (-) buying relics |
| `potionPurchaseBias` | 0.0 | Push toward (+) or away from (-) buying potions |
| `removalServiceBias` | 0.0 | Push toward (+) or away from (-) card removal |
| `saleBonus` | 2.5 | How much the AI cares about sale pricing |
| `colorlessPremiumPenalty` | 1.5 | Caution about colorless card pricing |
| `goldReserveValuePerGold` | 0.0 | How much the AI values saving gold. Higher = more hoarding. |

### `shop.relicWeights` - Relic valuation

| Field | Default | What it does |
|---|---|---|
| `ancientBaseline` / `rareBaseline` / `uncommonBaseline` / `commonBaseline` | 28 / 21 / 15 / 10 | Base value by rarity |
| `costDivisor` | 12.0 | How much price reduces value (lower = more price-sensitive) |
| `specialRelicBonusMultiplier` | 1.0 | Scales built-in bonus values for strong relics |

### `shop.removalWeights` - Card removal valuation

| Field | Default | What it does |
|---|---|---|
| `burdenMultiplier` | 1.0 | Multiplier on how bad the worst card in deck is |
| `smallDeckBonus` / `mediumDeckBonus` / `largeDeckBonus` | 2.0 / 4.0 / 7.0 | Extra removal value by deck size |
| `basicCardBonusPerCard` | 1.5 | Extra value for removing starter cards |
| `costDivisor` | 8.5 | How much removal price reduces value |

---

## Events

### `events.outcomeWeights` - Reward preferences

| Field | Default | What it does |
|---|---|---|
| `relicRewardMultiplier` | 1.0 | How much the AI values relics from events |
| `cardRewardMultiplier` | 1.0 | How much the AI values card rewards |
| `removalRewardMultiplier` | 1.0 | How much the AI values card removals |
| `upgradeRewardMultiplier` | 1.0 | How much the AI values card upgrades |
| `transformRewardMultiplier` | 1.0 | How much the AI values card transforms |
| `healValuePerPoint` | 1.8 | Value per HP healed |
| `maxHpGainValuePerPoint` | 3.5 | Value per max HP gained |
| `goldValueDivisor` | 12.0 | Lower = gold is more valuable |

### `events.riskProfile` - Event risk tolerance

| Field | Default | What it does |
|---|---|---|
| `hpPenaltyCriticalPerPoint` | 4.8 | How much the AI avoids HP loss when critically low |
| `hpPenaltyLowPerPoint` | 3.8 | HP loss avoidance when low health |
| `hpPenaltyMidPerPoint` | 2.9 | HP loss avoidance at mid health |
| `hpPenaltyHealthyPerPoint` | 2.0 | HP loss avoidance when healthy |
| `cursePenaltyMultiplier` | 1.0 | How much the AI dislikes gaining curses |
| `randomRewardDiscount` | 6.0 | Penalty for random/uncertain rewards |
| `startsCombatPenalty` | 8.0 | How much the AI avoids event options that start a fight |
| `lethalOptionPenalty` | 1000.0 | Avoidance of options that could kill the player |

---

## Character Personality Examples

Each character file only overrides the fields where it differs from default. Here's a summary of what makes each character unique:

**Ironclad** - Aggressive bruiser. Higher damage values, more willing to trade HP for kills.
- `attackWeight: 1.12`, `aggressiveness: 1.1`, `lethalPriorityBonus: 62`
- Lower `damageTakenPenaltyPerPoint: 28` (tougher)

**Silent** - Careful and defensive. Values debuffs, draw, and staying alive.
- `survivalWeight: 1.08`, `defenseWeight: 1.06`, `attackWeight: 0.98`
- Higher `damageTakenPenaltyPerPoint: 32` (more careful)

**Defect** - Tempo-oriented. Values draw, energy, and powers.
- `drawValueWhenPlayable: 13`, `energyGainValue: 22`, `powerBonus: 3.0`

**Regent** - Defensive wall. Maximizes block, cautious about attacking.
- `survivalWeight: 1.12`, `defenseWeight: 1.12`, `attackWeight: 0.94`
- `perfectDefenseBonus: 68`, `fullBlockCoverageBonus: 58`

**Necrobinder** - All-in aggressor. Highest kill priority, lowest defense concern.
- `attackWeight: 1.1`, `aggressiveness: 1.12`, `lethalPriorityBonus: 65`
- Lowest `damageTakenPenaltyPerPoint: 27`

---

## Tips for Modders

1. **Start small.** Change one or two values and test. Large changes can make the AI behave erratically.
2. **Use character files, not default.** Edit `ironclad.aiconfig` etc. Leave `default.aiconfig` as the baseline.
3. **Weight multipliers are powerful.** `survivalWeight`, `defenseWeight`, `attackWeight`, and `aggressiveness` have the biggest impact on combat personality.
4. **Skip thresholds matter.** `rewardSkipThreshold` and `shopSkipThresholdBase` control how picky the AI is about taking cards. Higher = pickier.
5. **Check the logs.** The mod logs config loading and key decisions. Launch with `--log --verbose` to see what the AI is thinking.
