# Breakpoint — Project Notes for Claude

This file is the persistent project context for Claude sessions. Read it first before making suggestions or changes to the project.

---

## 1. Project Overview

**Breakpoint** — 2D top-down tactical turn-based strategy game. Inspired by Fire Emblem (unit activation, abilities) and Kingdom Rush (tower defense aesthetic). Built in Unity 6 with URP.

**Tech stack:** Unity 6, URP, C#, FishNet (multiplayer, Phase 3), UniTask (async), TextMeshPro.

**Camera:** URP camera stack — Base game camera + Overlay UI camera. InputHandler uses a direct `[SerializeField] Camera` reference (not `Camera.main`) to avoid picking up the overlay camera.

**Turn system:** Alternating pick — Team 1 activates a fighter, then Team 2, alternating until all fighters have acted. Round ends when all fighters activated. `TurnManager` owns all round/turn logic.

**Server/client architecture:** Even in hotseat Phase 1, client code never mutates `Fighter`/`Board` state directly. All state changes route through `BattleController` request methods. `[SERVER]` comments mark server-only methods. Designed so Phase 3 multiplayer only needs to add `[ServerRpc]` attributes, not rewrite logic.

**Canvas:** Screen Space - Camera (not Overlay) to support particles/art effects in later phases.

**UI event pattern:** Subscribe in `Awake`/`OnDestroy` (NOT `OnEnable`/`OnDisable`). Panels use `SetActive` to show/hide — `OnEnable`/`OnDisable` would cause them to miss events while inactive.

**Fighter data:** Currently hardcoded in `BattleController.InitializeBoard`. `StreamingAssets/fighters.json` exists and will be the data source starting Phase 2.

**Movement:** Pool-based (`RemainingMovePoints`). Multi-step allowed while pool > 0. Triggered by Move button, not auto-shown on selection. `Pathfinder.GetReachableTiles` returns `Dictionary<Vector2Int, float>` (cost per tile).

---

## 2. Roadmap

### Phase 1 — Hotseat core loop ✅ COMPLETE

### Phase 2 — Character data + UI foundation (current)

- [x] JSON loader, BalanceSettings, FighterData, Ability data layer, sprite loading
- [x] Fighter stats: `DamageMultiplier`, `Accuracy`, `DodgeChance`, `CritRate` (0.1), `CritDmg` (1.5), `Shield`, `CurrentCharge`, `ResArcane`/`Elemental`/`Force`/`Corrupt`, `BonusEssenceDmg` per type
- [x] Essence moved to move level in `fighters.json`; all abilities have proper essence types assigned
- [x] `AbilitySlot` expanded: Passive/Normal/Skill/Skill2/Sig; second Skill auto-promoted in `FighterManager`
- [x] `StatusEffect` runtime class + `Fighter.ApplyStatusEffect`/`RemoveStatusEffect`/`TickStatusEffects` + `OnStatusEffectsChanged` event
- [x] UI pass COMPLETE: `FighterInfoPanel` (portrait, stats grid, res grid, HP/charge sliders with value text), `AbilityPanel` (essence icon, cooldown text, slot rules, sig-charge USE block, cancel interactable), `TurnTrackerPanel` (portrait cards, HP overlay, border for active fighter), `StatusEffectsPanel` + `StatusChip` (spawned per effect, icon by type+essence, stacks field)
- [x] `AbilityResolver`: target type filtering (Enemy/Ally/Self/Tile), damage formula (base × DamageMultiplier × (1+essenceBonus), True bypasses), healing, shielding, sig charge grant on use

**Mechanics — implementation order:**

- [ ] **Next: Cooldown enforcement** — tick all fighter cooldowns at round start (`TurnManager.OnRoundStarted`), block USE when `IsOnCooldown`, show cooldown number overlay on Skill/Skill2 slot buttons
- [ ] **Hit resolution** — accuracy vs dodge roll, crit roll: if hit → apply damage; if crit → damage × `CritDmg`
- [ ] **Damage formula completion** — add resistance to `AbilityResolver`: `dmg × (1 - target.GetResistance(essence))`; add resistance getter to `Fighter`
- [ ] **Knockback** — `ability.Knockback` field exists; resolver moves target along board
- [ ] **Status effects from abilities** — resolver reads `FighterEffectData.statusEffects` list, constructs `StatusEffect` objects, calls `target.ApplyStatusEffect`
- [ ] **Cooldown UI indicator** — show remaining turns number on Skill/Skill2 buttons when on cooldown
- [ ] **Passive system** — stubs exist; decide trigger model
- [ ] **Affinity/Essence track system**
- [ ] **Breakpoint bar** — movement/map-objective based

### Phase 3 — Multiplayer

Add FishNet `[ServerRpc]`/`[ObserversRpc]`. `BattleController` request methods become ServerRpcs. Logic unchanged.

### Phase 4 — Draft / roster

Pre-battle fighter selection screen.

### Phase 5 — Full content

All fighters, abilities, maps, balance.

### Notes

- `GameOverPanel` → replace with `VictoryScreen` scene load on full UI pass
- `DefaultSigCharge` in `AbilityResolver`: damage ability = 10, heal/shield = 5, otherwise 0
- True essence bypasses both `essenceBonus` and resistance in damage formula

---

## 3. Combat Design

### Stats per Fighter

HP, Speed, SigChargeReq, DamageMultiplier, Accuracy, DodgeChance, CritRate (default 10%), CritDmg (default 150%), Shield, CurrentCharge

Resistances: Arcane, Elemental, Force, Corrupt (float modifiers, 0 = no resistance, negative = vulnerability)

### Damage Formula

```
FinalDamage = BaseDamage × DamageMultiplier × (1 + EssenceDmgBonus) × (isCrit ? CritDmg : 1.0) × (1 - TargetResistance)
```

Shield absorbs before HP. True essence bypasses `EssenceDmgBonus` and resistance entirely. Resistance not yet applied in `AbilityResolver` — next step after hit resolution.

### Essence Damage Bonuses (per fighter, runtime only)

`BonusArcaneDmg` / `BonusElementalDmg` / `BonusForceDmg` / `BonusCorruptDmg` — all start at 0. Modified by zones, buffs, tile effects. Use `ModifyEssenceDmgBonus(essence, amt)` / `GetEssenceDmgBonus(essence)`.

### Hit Resolution

1. Roll accuracy vs dodge → miss/hit
2. Roll crit (< CritRate) → apply CritDmg multiplier
3. Apply DamageMultiplier (user)
4. Apply resistance (target, by essence type)
5. Shield absorbs remainder before HP

### Essence / Damage Types

None, Arcane, Elemental, Force, Corrupt, True (True bypasses all resistance). Every ability has one essence. Drives resistance lookup and affinity track accumulation.

### Charge / Signature System

- Each fighter has `CurrentCharge` filling toward `SigChargeReq`
- Charge is 1:1 with actual values dealt/healed/shielded (after all modifiers, not base values)
- Weights: `DamageChargeWeight` / `HealingChargeWeight` / `ShieldingChargeWeight` — all 1f, stored as constants in `AbilityResolver` for easy tuning
- `BaseSigCharge` on a move overrides the calculated value entirely (used for supports whose moves don't deal numbers)
- Using a Sig resets `CurrentCharge` to 0
- `CanUseSignature = CurrentCharge >= SigChargeReq`

### Ability Slots & Cooldowns

Passive (display only, USE disabled), Normal (no cooldown), Skill / Skill2 (BaseCooldown), Sig (charge-gated, USE blocked if charge < SigChargeReq). Cooldowns decrement at round start. Ability blocked when `IsOnCooldown`. (Not yet enforced — next step.)

`AbilityTargetType`: Enemy, Ally, Self, All (hits any), Tile (future tile placement)

### Status Effects (to implement)

Types: DoT, HoT, Shield, AccuracyMod, DamageMod, ResistanceMod, DodgeMod, Stun, SpeedMod, CritRateMod, CritDmgMod, CDModifier

Each has: Duration (rounds), Value, Source (fighter), DamageType, IsDebuff, ApplyChance (0–1). Applied per ability effect entry. Tick/expire at end of fighter's turn.

### Passive System

Character-specific triggers: `OnGameStart`, `OnRoundStart`, `OnTurnStart`, `OnDeath`, `AbilityOverride` (pre-damage intercept).

Examples: Avarice converts incoming Elemental → healing, Sedra ignores weak Force hits, Rellin gains charge from Arcane damage received. Implemented as a static `PassiveManager` with switch/case per character name.

### Affinity / Essence Track System

Each team has an `AffinityTracker` with mark counters per essence type. Marks accumulate when abilities of that essence land. Bonus marks from outcome flags (stun, DoT, buff, debuff applied). When marks hit threshold → single-track effect fires, track locks.

- Force track → Stagger
- Elemental track → Sustain
- Arcane track → Tempo
- Corrupt track → DoT Amplification

If 2+ tracks fire within a window → **Dual Fusion** (6 pairs: F+E=Eruption, F+A=Disruption, F+C=Crush, E+A=Purify, E+C=Blightstorm, A+C=Mindbreak).

If 3+ tracks → **Triple Fusion** (Cataclysm: MaxHP loss).

### Breakpoint Bar (to redesign)

Tug-of-war float between teams. Old version: every action (damage, heal, crit, status) pushed it. New version: revolves around movement and map objectives (capture zones, positional control). Not yet fully designed — implement after base mechanics are working. When bar maxes → triggering team gets a Breakpoint Choice (bonus action/effect, TBD for new version).

### Turn Order

Deliberate alternating pick: Team 1 chooses which fighter activates → Team 2 chooses → alternates until all fighters have acted → round ends. Intentional design choice (predictable, strategic activation order vs speed-based variance).

### Burndown (future)

Increasing true damage applied at round start after round ~12, scaling up each phase. Targets slowest fighters first. Prevents indefinite stalling.

---

## 4. Character Select Design

### Draft rules

- 3v3 alternating pick (T1, T2, T1, T2, T1, T2)
- First picker is randomized at scene load (coin flip)
- The team that picks SECOND gets to act FIRST in battle (Round 1 first activation)
- This compensates for draft disadvantage

### Scene layout (map-as-world-object)

- Map fills the entire scene background as a real world-space object (same `Board`/`TerrainGenerator`/`BoardRenderer` as battle)
- Map is generated first with a seed; same seed passed to battle scene so map is identical
- All UI lives as canvas overlays on top of the map

### Panels

- **Left (~40% width):** Scrollable character card grid, semi-transparent background
- **Right (~35% width):** Character preview — appears on card click, hides otherwise. Contains portrait, name, HP, Speed, combat stats, ability cards
- **Bottom bar (~10% height):** Team composition strip — Team 1 left, Team 2 right, small portrait icons with rarity pip, always visible
- **Top bar:** Restriction toggle, customization button, Confirm/Ready button

### Data passed CharSelect → Battle (via static `MatchSetup` class)

- `string[] Team1Fighters` — selected fighter names
- `string[] Team2Fighters`
- `int FirstActingTeam` — team that acts first (= team that picked second)
- `int MapSeed` — so battle regenerates the same map

### TurnManager change needed

`StartRound()` currently hardcodes `ActiveTeamId = 1`. Must read `MatchSetup.FirstActingTeam` on Round 1 only.

### Restriction engine (port from original game)

- Rarities: L=4, UR=3, R=2, UC=1, C=0
- Valid team patterns: {L, ≤R, ≤UC} | {UR, UR, ≤UC} | {UR, R, R}
- Toggle-able in top bar — when off, any 3 fighters allowed
- `AllowedNextRarities(currentPicks)` used to grey out ineligible cards during draft

### Customization panel (later addition)

- Adjust global HP, sig charge req, and other `BalanceSettings` variables
- Already have `BalanceSettings` ScriptableObject — just need UI sliders/fields wired to it

---

## 5. UI Plan (Phase 2 — COMPLETE as of 2026-04-02)

### FighterInfoPanel ✅

- Portrait (left, preserveAspect)
- Name text + HP slider with value text (e.g. "100/175") + Charge slider with value text
- StatsPanel: 3×2 grid of StatEntry prefabs — Cr, Cd, Acc, Dod, Spd (via moveText), D.M. (format: "1.3x")
- ResGridPanel: 4 cells (Arcane/Elemental/Force/Corrupt) with icon (preset) + val text
- StatusEffectsPanel: spawns StatusChip per active effect (Name, Duration, Icon by type+essence, Stacks hidden at 1 / "x2" at 2+)
- Move button wired to `SelectionManager.EnterMoveMode()`
- Sliders use `GradientSlider` (optional) — alpha preserved from inspector, not hardcoded

### AbilityPanel ✅

- 5 slot toggle buttons: Passive (always on), Normal (on if exists), Skill (on if exists), Skill2 (on if exists), Sig (always on)
- Ability display: essence icon (`Resources/effecticons/`), name, description (mechanics field), cooldown text ("CD 2" / "Ready" / empty)
- USE button: disabled for Passive, disabled for Sig when charge < SigChargeReq
- Cancel button: interactable toggle only (never hidden)
- Default on fighter select: Normal ability

### FighterOptions (action buttons) ✅

- Use, Move, End Turn, Cancel
- Move wired in `FighterInfoPanel`
- End Turn wired in `EndTurnButton.cs`
- Cancel interactable-only (not hidden)

### TurnTrackerPanel ✅

- Round text
- Two `HorizontalLayoutGroup` rows (Team 1, Team 2) of `FighterPortraitCard` prefabs
- `FighterPortraitCard`: Portrait image + HP overlay (vertical slider, bottom-to-top, gradient fill) + Border image (enabled only for active fighter)
- Turn state: active = full opacity + border on, done = 60% opacity + border off, waiting = full opacity, dead = red tint

### Notes

- `GradientSlider.cs`: attach to slider, drag Fill image in, set colors in inspector
- `StatusChip` prefab: Icon + NameText + DurationText + StacksText (StacksText hidden when stacks == 1)
- Camera viewport: **DO NOT touch `Camera.rect`** — caused persistent black bar
- `GameOverPanel` → replace with `VictoryScreen` scene on full art pass

---

## 6. Coding Conventions & Feedback

**Don't auto-trigger game state from selection.** Player actions (move, ability use) must be deliberate — triggered by explicit UI button presses, not just clicking a fighter.

- *Why:* Auto-showing move range on fighter select felt undeliberate. Wanted "click fighter → panels appear → click Move button → range shows → click tile".
- *Apply:* Never auto-enter a targeting mode on selection. Always require an explicit button press.

**Area shape uses center anchor with B-key bias toggle** for even-width boxes (not corner anchor).

- *Why:* Corner anchor was unintuitive — player had to click the "wrong" tile to hit the right area. Center anchor + bias toggle is more ergodic.

**Use `Awake`/`OnDestroy` for event subscriptions in UI panels, never `OnEnable`/`OnDisable`.**

- *Why:* Panels use `SetActive` to hide — `OnDisable` fires when hidden, unsubscribing the panel so it misses events and never reopens.

**Static C# events on manager classes for UI communication** (not UnityEvents).

**Don't call `Deselect()` on empty tile clicks** when a fighter is already selected.

- *Why:* Felt jarring — clicking anywhere accidentally closed panels with no recovery path.

**`GameOverPanel` is a placeholder.** On the full UI pass, replace with a dedicated `VictoryScreen` scene load via `SceneManager`.

**Don't use `SetActive(false)` to hide UI panels on deselect.** Instead, clear values to defaults and use interactable toggling.

- *Why:* Disappearing elements feel unpolished. State changes (greyed out, cleared text) are preferred over panels vanishing.
- *Apply:* Panels stay visible always. On deselect: clear text fields, zero sliders, disable buttons. Never call `gameObject.SetActive(false)` on main panels.

**`GradientSlider` and similar scripts that set color/alpha on other components must respect the alpha already set in the inspector.**

- *Why:* Hardcoded alpha in script overrides inspector value and causes confusion about why changes don't stick. Read `fill.color.a` and preserve it.
- *Apply:* When lerping colors, read the existing alpha first: `float alpha = fill.color.a`, then use it in the new Color.

**Don't set `Camera.rect` in `CameraController`** to carve out UI space. The approach caused a persistent black bar that survived inspector resets.

- *Why:* Camera viewport manipulation at runtime serializes unexpectedly and is hard to recover from. Use a fixed Game View resolution instead and accept minor spacing variance.
- *Apply:* `CameraController` only sets `orthographicSize` and `position`. Never touch `mainCamera.rect`.
