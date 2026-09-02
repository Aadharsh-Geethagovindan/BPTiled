# Breakpoint — Project Notes for Claude

This file is the persistent project context for Claude sessions. Read it first before making suggestions or changes to the project.

Last verified against actual code: 2026-08-19 (full pass — previous version of this doc predated the entire fighter-content build-out: roster went from 8 to 30, and a large family of new engine mechanics landed alongside it. If you're picking this up in a fresh session, read §3 carefully before touching combat code — several systems here didn't exist a few sessions ago.)

---

## 1. Project Overview

**Breakpoint** — 2D top-down tactical turn-based strategy game. Inspired by Fire Emblem (unit activation, abilities) and Kingdom Rush (tower defense aesthetic). Built in Unity 6 with URP.

**Tech stack:** Unity 6, URP, C#, FishNet (multiplayer — implemented, see §4), UniTask (async), TextMeshPro. Multiplayer is tested locally via **ParrelSync** (a second cloned Unity Editor instance acting as the second client) — ParrelSync is purely an external testing tool, there is no in-code awareness of it anywhere in `Assets/Scripts`.

**Camera:** URP camera stack — Base game camera + Overlay UI camera. InputHandler uses a direct `[SerializeField] Camera` reference (not `Camera.main`) to avoid picking up the overlay camera.

**Turn system:** Alternating pick — Team 1 activates a fighter, then Team 2, alternating until all fighters have acted. Round ends when all fighters activated. `TurnManager` owns all round/turn logic.

**Server/client architecture:** `BattleController`/`TurnManager`/`AbilityResolver` etc. run the same logic in hotseat and online modes. In online mode, all state changes route through `BattleNetworkBridge` `[ServerRpc]`s to the server, which runs the real logic and broadcasts results back via `[ObserversRpc]`. Clients never mutate `Fighter`/`Board` state directly. Fighter state syncs **wholesale** via `JsonUtility.ToJson(fighter.State)`/`FromJson` on the entire `FighterState` object (not a hand-maintained list of individual fields) — this was a deliberate architectural decision after repeatedly hitting "we synced field X but forgot field Y" bugs. See §4.

**Canvas:** Screen Space - Camera (not Overlay) to support particles/art effects in later phases.

**UI event pattern:** Subscribe in `Awake`/`OnDestroy` (NOT `OnEnable`/`OnDisable`). Panels use `SetActive` to show/hide — `OnEnable`/`OnDisable` would cause them to miss events while inactive.

**Fighter data:** Loaded from `StreamingAssets/fighters.json` via `FighterLoader`. **Roster is now the full 30 fighters** from the original design doc (`StreamingAssets/Fighters Moveset.txt`) — every fighter listed there is implemented. `Characters.json` in the same folder is an **old prototype** with a different balance scale (speed/sigChargeReq roughly proportional but not 1:1, HP scale incompatible) — only rarity, sigChargeReq (as a scaling baseline), and speed are ever ported from it; HP is always inferred fresh by role, never copied. Default hotseat/debug battle roster in `BattleController.InitializeBoard` is still Jack/Avarice/Sanguine vs Krakoa/Captain Dinso/Vas Drel (hardcoded fallback when `MatchSetup.IsReady` is false) — full roster is reachable via CharSelect draft.

**Movement:** Pool-based (`RemainingMovePoints`). Multi-step allowed while pool > 0. Triggered by Move button, not auto-shown on selection. `Pathfinder.GetReachableTiles` returns `Dictionary<Vector2Int, float>` (cost per tile) — **cost only, not the actual route** (see §3 known gap: movement currently teleports straight to the destination in one jump; it does not yet step through intermediate tiles, so tile effects like traps only trigger on the final landing tile, never on tiles merely passed through — this is a known, explicitly-deferred gap, not an oversight, see §9).

---

## 2. Roadmap

### Phase 1 — Hotseat core loop ✅ COMPLETE

### Phase 2 — Character data + UI foundation ✅ COMPLETE

All core mechanics listed in the original plan are implemented: cooldowns, hit resolution, damage formula, knockback, status effects, passives. Plus a large amount that wasn't originally planned — see §3.

### Phase 3 — Multiplayer ✅ FUNCTIONALLY COMPLETE

FishNet wired end-to-end for Lobby → CharSelect draft → Battle, tested via ParrelSync. Server-side ownership checks on battle actions, disconnect/forfeit handling, and game-over sync are all implemented (these were open gaps in an earlier pass of this doc — they're closed now, see §4). Remaining gaps are narrow — see §9.

### Phase 4 — Draft / roster 🟢 MOSTLY COMPLETE

Same status as before: alternating 3v3 draft, restriction engine, map generation all implemented and working online. Restriction toggle UI and the customization panel still aren't built (§6, §9).

### Phase 5 — Full content ✅ ROSTER COMPLETE

**All 30 fighters from `Fighters Moveset.txt` are implemented** in `fighters.json`, built in three batches (front-loading fighters most likely to expose new engine mechanics, deliberately, each batch): the original 8 (Arkhe, Avarice, Krakoa, Sanguine, Rellin, Captain Dinso, Jack, Vas Drel), then Bessil/Faru/K.A.S./Olthar/Rei/Sedra, then Breach Specialist/Mizca/Skirvex/Trustless Engineer/Ulmika/Vemk Parlas, then the last 10 (Constellian Trooper/Huron/Legionary/Nou/Rover/Raish/Temple Guard/Trex/Virae/VyGar). Balance is **not** tuned — stats and numbers are first-pass inferences, expect a real balance pass later.

### Notes

- `DefaultSigCharge` in `AbilityResolver`: damage ability = 10, heal/shield = 5, otherwise 0 — actually, `BaseSigCharge` is now authored per-move in JSON; if it's 0/absent, charge is computed from actual damage/heal/shield dealt (see §3 Charge System).
- True essence bypasses both `essenceBonus` and resistance in damage formula.

---

## 3. Combat Design

This section changed the most since the last full pass. Read the whole thing before touching combat/ability code — several systems here are recent and easy to accidentally duplicate or bypass.

### Stats per Fighter

HP, Speed, SigChargeReq, DamageMultiplier, Accuracy, DodgeChance, CritRate (default 10%), CritDmg (default 150%), Shield, CurrentCharge, **Species** (string, e.g. `"Riftbeast"` — new, see below), **TerrainCostMultiplier/TerrainCostThreshold** (new, see below), **TotalTilesMoved**, **TotalDamageDealt** (cumulative per-match counters, new — see "Stats tracking" below).

Resistances: Arcane, Elemental, Force, Corrupt (float modifiers, 0 = no resistance, negative = vulnerability). **Bug fixed this pass:** `GetModifiedResistance` used to sum *every* active `ResistanceModifier` status effect into *every* essence's resistance check, regardless of which essence the modifier was actually for — a "+30% Arcane res" buff was silently also boosting Corrupt/Force/Elemental resistance. Now correctly filters by `e.Essence == essence.ToString()`.

### Damage Formula (`HitResolver.CalculateDamage`)

```
FinalDamage = (BaseDamage + DynamicBonus) × DamageMultiplier × (1 + EssenceDmgBonus) × (isCrit ? CritDmg : 1.0) × (1 - TargetResistance)
```

True essence: skips `EssenceDmgBonus` and resistance entirely, crit still applies. Shield absorbs before HP. `DynamicBonus` is the new dynamicValue system (below) — it's folded into the base value *before* the multiplier chain, so it scales with everything else rather than being a flat post-mitigation add-on (explicit design call, not a default).

### Pre-Damage Interception (new)

`Fighter.TakeDamage(amount, essence, source)` now runs two checks *before* shield/HP are touched:

1. **`PassiveManager.ShouldPreventDamage(target, essence, amount)`** — fighter-specific conditional immunity, hardcoded per-name like every other passive check. Currently backs Rei's Dark Empress (immune to Arcane damage ≤30).
2. **Generic `StatusEffectType.DamageRedirect`** — if the target has a status of this type, a fraction (`Magnitude`) of the hit is rerouted to whoever applied it (`SourceFighterName`), recursively calling `TakeDamage` on them. Backs Olthar's JudgeWard. Reusable by any future status-driven redirect, no fighter-name hardcoding needed for new users of it.

`essence`/`source` are threaded through all four places damage can originate: direct ability hits, DoT ticks, `TriggerDamageOnly` (Exsanguinate-style instant DoT detonation), and tile effects.

### Status Immunity (new)

`Fighter.ApplyStatusEffect` checks `PassiveManager.IsImmuneToStatus(target, effect.Type)` before adding a status at all (blocks it entirely — no stacking, no events). Currently backs Bessil's Nightmare's Grasp (immune to `Root` and `Stun`).

### Stun (new — was explicitly "not implemented" in the previous version of this doc)

`StatusEffectType.Stun` exists. When a stunned fighter is activated (`BattleController.HandleFighterActivated`, subscribed to `TurnManager.OnFighterActivated`), it logs the message, waits ~1.5s (`UniTask.Delay`), then calls the normal `RequestEndTurn()` gateway — same path a real End Turn click uses, so it already routes correctly for both hotseat and online with no extra authority gating (the routing and idempotency were already handled by the existing Request-gateway pattern). `AbilityPanel`/`FighterInfoPanel` force Move/Use disabled when the active fighter is stunned.

### Charge / Signature System

- Each fighter has `CurrentCharge` filling toward `SigChargeReq`.
- **General principle (load-bearing — apply this to any future gap you find):** any time damage/heal/shield is dealt as a *direct result* of a fighter's move, it counts toward that fighter's charge — even if the damage lands later (a tile effect ticking, a DoT ticking, an early DoT detonation via `TriggerDoTs`). This was implemented piecemeal as gaps were found: `TileEffectManager` credits the effect's original placer (`TileEffect.SourceFighterName`); `StatusEffect.Apply()` credits DoT/HoT ticks the same way (`StatusEffect.SourceFighterName`, set inside `Fighter.ApplyStatusEffect` when a caster is passed); `AbilityResolver`'s `TriggerDoTs` instant effect folds detonated DoT damage into the caster's `totalCharge`. All three reuse the same `AbilityResolver.DamageChargeWeight`/`HealingChargeWeight`/`ShieldChargeWeight` public constants.
- `BaseSigCharge` on a move overrides the calculated value entirely.
- Using a Sig resets `CurrentCharge` to 0.

### Ability Slots & Cooldowns

Passive (display only), Normal (no cooldown), Skill / Skill2 (BaseCooldown), Sig (charge-gated, no cooldown of its own — reuse is gated by charge). Convention across the whole roster: Normal/Sig = 0 cooldown, Skill = 2 (occasionally 3 for an unusually strong/wide one). Cooldowns tick at `TurnManager.ActivateFighter`.

`AbilityTargetType`: Enemy, Ally, Self, AllyOrSelf, All, Tile, Ground.

### Status Effects (`StatusEffect.cs`)

Types: `DamageOverTime, HealOverTime, AccuracyModifier, DodgeModifier, CritRateModifier, CritDamageModifier, DamageMultiplier, ResistanceModifier, SpeedModifier, Root, DamageRedirect, Stun`. `Root` zeroes `GetModifiedSpeed()` to 0. **Bug fixed this pass:** Root used to only affect *next* turn's movement pool (via `ResetTurnState`) — if it landed mid-turn (e.g. stepping onto a trap), the fighter could still spend movement points they'd already been allocated that turn. `Fighter.ApplyStatusEffect` now zeroes `RemainingMovePoints` immediately when Root is applied, same as the existing `SpeedModifier` mid-turn adjustment.

DoT/HoT tick at turn **start** (`Fighter.TickPeriodicEffects`, can kill and skip the turn). Everything else ticks at turn **end** (`Fighter.TickDurationEffects`).

Each `StatusEffect` carries `SourceFighterName` (who applied it — used for sig-charge crediting on DoT/HoT ticks and for `DamageRedirect`'s target) and an optional `Condition` (see Conditional Effects below).

**Generic removal helpers on `Fighter`** (new): `RemoveStatusEffect(name)` (exact name, removes all stacks), `RemoveRandomStatusEffect(isDebuff)` (picks one at random matching the category — used where "which one" isn't known ahead of time, e.g. Ulmika's random-debuff cleanse), `RemoveAllBuffs()` (returns the removed list — used for buff-stealing, see below).

### DynamicValue System (new)

Damage/heal/shield values that scale off live status-effect counts instead of being fixed JSON numbers. Schema on an `AbilityEffect`/`TileEffect`:

```json
"dynamicValue": {
  "valueType": "Damage" | "Healing" | "Shielding",
  "source": "CasterBuffs" | "CasterDebuffs" | "TargetBuffs" | "TargetDebuffs" | "NamedStatus",
  "statusName": "...",       // only used when source == NamedStatus
  "amountPerStack": 10,
  "isConsumed": false        // NamedStatus only — removes the named status after resolving
}
```

Interpreted by `DynamicValueResolver` (`ComputeBonus`, `Consume`, and the shared `CountStacks` helper). Counting is **sum of stacks**, not distinct-effect-count (explicit design call — bigger, swingier numbers were preferred). `NamedStatus` always reads off the **caster**, never the target (there's no ambiguity to encode — every current use case is "consume my own resource"). Consumption is deferred to `AbilityResolver.Apply` (mutation), not done in `HitResolver.Calculate` (which stays pure/no-mutation, safe for a future animation pre-roll). Used by Bessil's DarkShadow, Faru's Sword Strike/All Out, Mizca's Piledriver.

### Conditional Effects (new)

A `StatusEffect` entry can carry a `Condition` that gates whether it applies at all, checked *before* the `ApplyChance` roll, against **pre-cast** state (so an ability granting both a status and a conditional second status can't have the first one contaminate the condition check for the second):

```json
"condition": { "source": "NamedStatus", "statusName": "Howl", "minCount": 1 }
```

Reuses the exact same `DynamicValueSource` enum and `CountStacks` helper as the dynamicValue system above — evaluated via `DynamicValueResolver.ConditionMet`. Used by Faru's Sharpen Blade (Evasive-if-buffed) and Raish's Feral Lunge (Bleed-if-Howling).

### Buff Transfer / Removal (new)

Three `InstantEffectType` values back a small family of buff-manipulation mechanics:

- `RemoveRandomBuff` — strips one random buff from the target immediately (Vemk Parlas's Skill).
- `StealBuffs` — removes *all* buffs from the target and stashes them on the caster via `Fighter.StashTransferredBuffs`/`TakeTransferredBuffs` (a transient, server-only field — not part of `FighterState`, never needs network sync, only has to survive between two calls on the same authoritative peer).
- `ReceiveStolenBuffs` — grants the target every buff currently stashed on the caster, then clears the stash.

`StealBuffs`+`ReceiveStolenBuffs` together implement Vemk Parlas's Sig (steal an enemy's buffs, hand them to a chosen ally) — see SecondaryEffect below for how the two different targets get picked in one ability use.

Tile effects have their own parallel, simpler mechanism: `TileEffect.RemoveRandomBuffChance` (0–1, independently rolled) strips one random buff from whoever triggers the effect. Used by Trustless Engineer's Trappings — note this is rolled *independently* from Rooted's own `ApplyChance` on that same tile effect, not a single combined roll, a known simplification (§9).

`ExtendAllBuffs` (another `InstantEffectType`) adds `Magnitude` turns to the `Duration` of every currently-active non-debuff status on the target — backs Virae's Preservation.

### SecondaryEffect — two-click abilities (new)

Some abilities need a genuinely independent second target pick after the first resolves (not just a second effect that rides along the same click). `Ability.SecondaryEffect` returns the first effect after `PrimaryEffect` that has `RequiresSecondaryTarget = true` set explicitly in JSON.

**This flag is load-bearing and easy to get wrong.** A non-Self second effect that *isn't* flagged resolves against the *same* `shapeTiles` as the primary effect, just filtered to its own `TargetType` — this is how Legionary's Hold The Line (damage enemies + shield allies, same line, one click) and the original Vanguard Assault pattern (damage enemies + shield self) both already worked before this system existed, and still do. Only flag `RequiresSecondaryTarget` when the second target is a genuinely free, independent pick — currently only Vemk Parlas's Sig (steal from one enemy, grant to a chosen ally anywhere in range) uses it.

Mechanically: `SelectionManager` gets a `SecondaryTargeting` state after the primary resolves (mirrors the existing `RepositionTargeting` state/pattern exactly — same shape, `CmdRequestSecondaryEffect` RPC mirrors `CmdRequestReposition`). `AbilityResolver.Execute`/`Calculate`/`Apply` take an `onlyEffect` parameter to resolve just the deferred effect on the second pass, and `grantChargeAndFireEvent` to make sure charge/`OnAbilityUsed` only fire once (on the primary pass), not twice.

### Targeting Shapes (`AbilityTargeting.cs`)

`Single`/`Ring` anchor selection is a whole-board Manhattan-distance scan (**not yet cardinal-restricted** — this is the in-progress "rook-style" change discussed but not yet built, see §9). `Line`/`Cone`/`Cross`/`Box` anchor selection is already cardinal-direction-only from the caster.

**Ring shape (redefined this pass):** used to be a Manhattan-distance diamond outline — now a **hollow square perimeter with the 4 corners cut**, per an explicit spec from the user (verified against hand-drawn diagrams for size 2 and size 3). Formula: perimeter of an `(N+2)×(N+2)` box centered on the anchor, corners excluded; size 1 is a special case that degenerates to exactly the Cross-size-1 shape (there's no room for a hollow interior at that size). Even-size boxes reuse the same `biasLeft`/B-key convention Box already had.

**Facing rotation (new, R key):** `GetShapeTiles` takes an optional `facingOverride` (`Vector2Int?`) that replaces the auto-inferred `anchor - caster` direction for Line/Cone/Box. This exists because a **range-0** directional effect only has one valid anchor tile (the caster's own position) — auto-inference has nothing to read a direction from and silently defaults to Up. `SelectionManager` cycles Up→Right→Down→Left on R, persists for the whole targeting session (like the B-key bias), resets on `Deselect()`. **Line is deliberately excluded from rotation except at range 0** — a Line is meant to read as a fixed beam (railgun/lightning-bolt), aimed by where you hover, not manually rotatable; range 0 is the one case where hover can't communicate direction either, so rotation is the only way to aim it at all (Legionary's Hold The Line is currently the only range-0 Line ability in the roster). Cross has no facing concept (always symmetric in all 4 directions) — nothing to rotate.

### Species (new)

`Fighter.Species` (set at spawn from `FighterData.species`, same treatment as `FighterName`/`TeamId` — identity data, not match state, not in `FighterState`). `TileEffect.ExcludedSpecies` — if set, fighters of that species are unaffected by the tile effect regardless of team affinity. Currently used by Skirvex's Karrow (excludes `"Riftbeast"`). Five fighters are currently `"Riftbeast"`: Raish, Bessil, Mizca, Skirvex, Trex — this was worth building as real data rather than a one-off hardcode given how many fighters share it.

### Terrain Cost Modifier (new)

`Fighter.GetEffectiveTerrainCost(tile)`: `tile.MovementCost > State.TerrainCostThreshold ? tile.MovementCost * State.TerrainCostMultiplier : tile.MovementCost`. Both fields are plain per-fighter data (default multiplier 1, threshold 0 = "applies everywhere"), set via `SetTerrainCostMultiplier`/`SetTerrainCostThreshold`. **The formula itself carries no fighter-specific assumption** — Sedra's specific choice (0.5 multiplier, threshold 1, i.e. "only hard terrain, halved") lives entirely in `PassiveManager`, not in the formula. `Pathfinder.FindPath`/`GetReachableTiles` both take the moving `Fighter` and route every tile-cost read through this instead of raw `Tile.MovementCost`.

### Stats Tracking (new, small, likely to grow)

`FighterState.TotalTilesMoved` (pre-existing, used by Arkhe) and `TotalDamageDealt` (new, used by Constellian Trooper) are cumulative per-match counters on `Fighter`, incremented at the point of the relevant mutation (`AddTilesMoved`/`AddDamageDealt`). If more of these get added (healing dealt/received, etc. — floated as a likely future need), keep this same pattern: a plain `FighterState` int field + an `AddX` method, not a separate stats subsystem, until there's an actual reason for one.

### Passive System (`PassiveManager.cs`)

Event-subscription model, unchanged in shape from before but with several new event sources. **All fighter-name checks are isolated to this file** — this is a hard rule, not a suggestion; if you're writing fighter-specific logic anywhere else (`Fighter.cs`, `AbilityResolver.cs`, `TileEffectManager.cs`), it's wrong, route it through here instead. Every handler is a chain of `if (fighter.FighterName == "X")` blocks (no early `return` between them — multiple fighters' checks can share one handler method).

Event sources subscribed in `Initialize` (gated to server/hotseat-only, same reasoning throughout: this mutates state in reaction to events that also fire on pure clients as local mirrors, so only the authoritative peer should actually run it):

- `Fighter.OnFighterDamaged/OnFighterDied/OnStatusEffectApplied/OnFighterMoved`
- `TurnManager.OnFighterActivated` (per-turn passives)
- **`TurnManager.OnGameStart`** (new — fires once, before Round 1; for passives with no other natural trigger, e.g. Bessil's Tainted, Sedra's move-after-act grant, Temple Guard's team-wide starting shield)
- **`AbilityResolver.OnAbilityUsed`** (new — fires once per ability use regardless of hit/miss; K.A.S.'s Overdrive Matrix, Raish's Pack Call ally-buff)
- **`AbilityResolver.OnEnemyHit`** (new — fires per confirmed hit landed on an enemy, exposes the *caster*, unlike `OnFighterDamaged` which only exposes the target; Breach Specialist's hit-count threshold, Constellian Trooper's damage-dealt threshold)
- **`AbilityResolver.OnCrit`** (new — fires per hit that lands as a crit; Mizca's Rage stacks)
- **`AbilityResolver.OnBuffRemoved`** (new — fires per action that strips one or more buffs, with the count; Vemk Parlas's Sabotaged Advantage)

Threshold/counter-based passives (Krakoa's Battle-Scarred, Constellian Trooper, Breach Specialist, Huron, Trex's once-per-game trigger) all follow the same pattern: a `Dictionary<Fighter,int>` or `HashSet<Fighter>` tracker field, diffed against a freshly-computed value each time the event fires, cleaned up in `OnFighterDied`.

All 30 fighters now have real passive logic (or, for the handful whose "passive" is a pure base-stat like Nou's starting dodge, no `PassiveManager` code needed at all — just set in `fighters.json`).

### Affinity / Essence Track System — ⚠️ NOT IMPLEMENTED (design only, unchanged)

Still fully unbuilt. See old design notes if picked back up — Force→Stagger, Elemental→Sustain, Arcane→Tempo, Corrupt→DoT Amplification, with Dual/Triple Fusion combos. Not touched this pass.

### Breakpoint Bar — ⚠️ NOT IMPLEMENTED (design only, unchanged)

Still fully unbuilt, still needs its movement/objective-based redesign finished before implementation.

### Turn Order

Unchanged: deliberate alternating pick, Team 1 → Team 2 → alternates until all acted → round ends.

### Burndown (future, unchanged)

Not implemented.

---

## 4. Multiplayer Architecture (Phase 3 — functionally complete)

### Flow

Unchanged from the previous pass — `MainMenuUI` → (Hotseat: straight to CharSelect) or (Online: `LobbyScene` → FishNet Tugboat host/join → `CharSelectNetworkBridge` draft → `BattleNetworkBridge` battle). See old doc structure if you need the full diagram; the shape hasn't changed, just the RPC surface (below) grew.

### Key files

- `Assets/Scripts/UI/MainMenuUI.cs`, `Assets/Scripts/Network/LobbyUI.cs` — unchanged.
- `Assets/Scripts/Network/CharSelectNetworkBridge.cs` — unchanged.
- `Assets/Scripts/Network/BattleNetworkBridge.cs` — grew significantly this pass:
  - `BroadcastAllFighterStates()` → renamed `BroadcastBattleState()`, now bundles `RpcSyncFighterStates` (whole-`FighterState` JSON per fighter) **and** `RpcSyncTileEffects` (via `TileEffectManager.CaptureSnapshot()`) in one call.
  - **Server-side ownership checks added** to `CmdActivateFighter`/`CmdRequestMove`/`CmdRequestAbility`/`CmdRequestReposition`/`CmdRequestEndTurn` (checks `fighter.TeamId != RemoteClientTeamId` or `TurnManager.Instance.ActiveTeamId != RemoteClientTeamId`) — this closes the gap the previous version of this doc flagged as the #1 known issue. `CmdDebugCommand` is deliberately *not* gated (shared testing tool).
  - **`CmdRequestSecondaryEffect`** — mirrors `CmdRequestReposition`, drives the SecondaryEffect two-click flow (§3) over the network.
  - **Game-over sync added** — `OnServerGameOver`/`RpcGameOver`, closes the previous doc's #2 known issue (host-only game-over).
  - **Disconnect handling added** — own-connection-lost → reset to MainMenu; remote-client-disconnect (server-side) → `TurnManager.ForceGameOver`.
- Bridges are still spawned server-side onto their own `NetworkObject`; `BattleController`/`TurnManager`/`CharSelectManager` stay plain MonoBehaviours unaware of networking.

### Known gaps (narrow now — the two big ones from the previous pass are closed)

1. Minor: `CharSelectNetworkBridge.cs` still has leftover commented-out debug logs (unconfirmed if cleaned up — check before assuming).
2. `BattleLogger` is local-only, not networked — passive/tile-effect/hit log lines only show on whichever peer is authoritative (host, or either peer in hotseat), not necessarily on a pure remote client's log panel. Known, explicitly deferred, not a regression from anything specific.

---

## 5. Tile Effect / Zone System

Same foundational shape as before, extended this pass:

- **`TileEffectTrigger`** gained `OnEnterOrTurnEnd` (fires on *both* entry and turn-end from one effect — needed because Breach Specialist's Scorched deals damage both ways, and the trigger enum only supported one or the other before).
- **`TileEffect.ExcludedSpecies`** and **`TileEffect.RemoveRandomBuffChance`** — see §3 Species / Buff Transfer sections.
- **`TileEffect.DynamicValue`** — tile effects can scale off status-effect counts the same way ability effects can (§3).
- Tile effects now correctly grant sig charge to whoever placed them (`TileEffectManager.ApplyEffectToFighter`, via `TileEffect.SourceFighterName` — part of the general charge-crediting principle in §3).
- `TileEffectManager.PlaceEffect` still takes a `Fighter source` (not just a team id) — needed for `SourceFighterName`/species/dynamicValue to all work.

**Known limitation, unchanged from before, worth flagging prominently:** `MoveResolver.ExecuteMove` still only calls `TileEffectManager.HandleFighterEntered` for the *final* destination tile — a fighter walking a multi-tile path never triggers effects on tiles merely passed through. This makes trap-style tile effects (Trustless Engineer's Rigged/Trappings) meaningfully weaker than their design text implies. Explicitly discussed and deferred — see §9, it's the next planned engine change (path-stepped movement), not forgotten.

---

## 6. Character Select Design

Unchanged from the previous pass — see old structure. Restriction toggle UI and customization panel are still the two open gaps (§9).

---

## 7. UI Plan

Unchanged from the previous pass except:

- `AbilityPanel`/`FighterInfoPanel` now also disable Move/Use when the active fighter is Stunned (§3).
- Targeting has two new player-facing controls with no visible UI indicator yet (both TODOs already in code comments): **B** toggles even-width Box/Ring bias, **R** cycles Line(range-0-only)/Cone/Box facing. Worth a real UI hint eventually, currently keyboard-only with no on-screen affordance.
- `TileHighlighter` overlays now correctly render above fighter sprites where they need to (anchor tile, shape preview, multi-select picks) and below them where they should (Range/MoveRange zone highlights) — see §3 note on `SelectionState.MultiTargeting`. Root cause was a Sorting Layer issue (`Board` sits below `Characters` in the project's Sorting Layers stack; Order-in-Layer can't override that), not a numeric ordering bug — fixed via an explicit `aboveCharacters` flag on `TileHighlighter`'s overlay primitives, not by changing every overlay's layer wholesale (an earlier, broader attempt at this fix caused the opposite problem — Range covering fighters — before landing on the current per-overlay-purpose split).
- **`StatusChip`'s stack count is pips, not text** — `stacksText` is gone; `pipContainer`/`pipSprite`/`pipSize`/`pipSpacing` generate up to 5 tinted circular pips per chip (`MaxPips = 5`, no overflow "+N" indicator yet). Mirrors `StatusEffectsPanel`'s existing destroy-and-reinstantiate idiom (no pooling).

### Visual Identity / Color Scheme (decided 2026-08-31 — the game's own signature colors, distinct from any single fighter/tile-effect color)

This is the second time this exact decision has been made — the first pass was made verbally earlier in the project and never written down anywhere, and was lost. Writing it down properly this time specifically so that doesn't happen again.

**Neutral base:** burnished/gunmetal steel gray, for panel chrome and backgrounds. Chosen to match the actual metal-panel texture already showing up in the art direction reference images (brushed gunmetal with colored linework) — not an arbitrary "dark UI" default.

**Primary accent: Blue.** The default, always-there interactive color — buttons, standard highlights, structural framing/borders.

**Secondary accent: Purple, reserved for special/heightened moments only** — a Sig-charge-ready glow, a crit flash, a big-play callout. Deliberately **not** used for everyday/default UI elements — restraint is what makes it read as "this moment matters" rather than decorative noise sitting next to blue for no reason. This is Option A of two considered (Option B was splitting the two by interaction state — idle vs. active/selected — simpler mechanically but less "special occasion" payoff; not chosen).

**Gold:** kept out of both accent slots entirely. Used only as a sparing metallic highlight (trim/linework, rarity borders, a "ready" glow) — never as a fill, background, or a primary/secondary accent. This was a deliberate constraint, not an oversight: gold already carries heavy visual weight from two factions (see below) and reads as trim in every reference image, not a fill color.

**Execution rule:** wherever blue and purple sit directly adjacent to each other (a split header, a dual-tone border), use a **hard flat edge, not a gradient/blend** — the cel-shaded art style this project is generating assets in explicitly avoids gradients ("no muddy gradients, no airbrush" is literally in the style LoRA's own description), so a smooth blue→purple blend would clash with the rest of the flat, sharp-edged look. The project's own logo concept already does this correctly — a cracked/fractured hard line dividing a blue "B" from a purple "P" — reuse that visual language, don't soften it into a gradient.

**Why blue and purple, given they're already faction colors:** deliberate, not accidental overlap. Faction color map as of this decision: Constellian (Alliance/Armed Forces/Navy) = Blue+Gold, Silgar (Military/Sprawler Corps) = Gold+Red, Intergalactic Wizarding Organization = Purple+Silver, The Rift = Black+Purple. Blue and purple were chosen specifically *because* they're already meaningful in the game rather than picking something arbitrary and unclaimed (pink/magenta and plain gray-neutral-blue were both considered and rejected — pink for having zero thematic tie to anything, blue-alone for being generic/overused as a default game-UI color with no counterpart, purple-alone for not having a usable muted/desaturated range for large fills, only working as a saturated accent).

**Related, easy to get wrong — verify the SCENE, not the C# script defaults, for existing colors.** `BoardRenderer.cs`'s `arcaneColor`/`elementalColor`/`forceColor`/`corruptColor` fields have C# default values that are **stale and wrong** — they were tuned live in the Unity Inspector at some point and the code defaults were never updated to match. The actual colors in use (serialized in `BattleScene.unity`, confirmed directly) are:

```
Arcane    → Gold   (0.868, 0.779, 0.045)
Elemental → Cyan   (0.036, 0.840, 0.740)
Force     → Green  (0.037, 1.000, 0.000)
Corrupt   → Purple (0.660, 0.028, 0.635)
```

Note **Corrupt is the essence that's actually purple**, not Arcane — this was gotten wrong once already earlier in this same decision process by trusting the C# defaults instead of checking the scene, exactly the same mistake `TileEffectChip.prefab` had already demonstrated earlier in this project (a serialized prefab/scene override always wins over a script's default value — check the actual instance, not the source file, whenever colors/values seem off or are being relied on for a decision).

**Not yet decided:** exact hex values for the blue/purple/gunmetal trio. Deliberately left open — likely to be picked by sampling from whatever art actually comes out of the ComfyUI generation pipeline (SDXL + a cel-shading LoRA, in progress — see chat history/external notes, not tracked in this repo) rather than picked in the abstract and forced onto the art after the fact.

### UI Upgrade Backlog (started — user is documenting UI polish needs, prioritizing before building)

Grounded against actual code (not assumed) in a pass on 2026-08-2x. Recommended order below converges from both an effort and an impact lens (rare that they agree this cleanly — worth doing in this order rather than picking one lens over the other).

**Tier 1 — trivial, pure visual polish, no new architecture:**
- `TurnTrackerPanel` has **no background/border `Image` at all** today — confirmed bare in the scene (just `RectTransform` + the script + two `HorizontalLayoutGroup` children). Adding a frame is a scene/prefab-only change, no script changes needed.
- `FighterInfoPanel`'s portrait `Image` is bare, no frame sibling. **Reuse opportunity:** `FighterPortraitCard.cs` (the smaller portrait used in `TurnTrackerPanel`) already has a working `borderImage` toggle pattern — mirror that instead of inventing a new one.
- CharSelect's "3 boxes per team showing where picks go" **already exists and works** — `TeamPicksPanel.cs` has `Image[3] portraits` + `Image[3] rarityPips`, and empty slots are already shown at a faint (alpha 0.2) placeholder tint rather than hidden. If it still doesn't *read* as "3 boxes" visually, the remaining work is just giving each slot a background/border shape so an empty slot looks like an empty slot rather than a very faint portrait — not new functionality.

**Tier 2 — small, self-contained UI-only changes:**
- Battle log entries **already spawn a per-entry prefab**, not raw appended text — `HistoryPanel.cs` instantiates `LogEntry.prefab` per `BattleLogger.OnEntry` and colors it by category. The "different size, plain text" complaint is because `LogEntry.prefab` itself has no background/pill `Image` — it's one bare `TextMeshProUGUI` that auto-sizes to content. Fix is a prefab-only visual pass (add a background + padding), same visual language as `StatusChip`/`TileEffectChip` — no new spawning logic needed, that part's already right.
- `TileInfoPanel` has no essence indicator. `Tile.EssenceAffinity` already exists as data (`Tile.cs`), currently only consumed by `BoardRenderer` for the tile's ground-color tint. Needs: a new `Image` in the panel, wiring `TileInfoPanel`'s rebuild to look up the displayed tile (it doesn't currently hold a `Board`/tile reference, that's the one new piece), and an essence→sprite mapping (could extract/share `BoardRenderer`'s existing essence→color mapping rather than duplicating it).

**Tier 3 — asset-only, not a code task, low urgency, needs a creative call (not mine to make unilaterally):**
- Fighter portraits in `Resources/fighters/` are inconsistent aspect ratio: **21 of 28 are 1024×1536** (portrait), **7 are 1024×1024** (square) — Breacher, Raish, Rover, Sedra, Skirvex, Trex, Virae. `preserveAspect = true` is already set everywhere it's used (`FighterInfoPanel`, `FighterPortraitCard`, `TeamPicksPanel`), so nothing is stretching — but letterboxing/framing differs per-fighter. Fixing this means re-cropping the minority (7 square ones, if conforming to the majority) or the majority (21, if conforming to square) — a content/art decision, not a script change.

**Tier 4 — its own arc, biggest item, start once you're sourcing assets for it:**
- **Sound** (music + SFX for clicks, button-active states, fighter movement; attack SFX explicitly deferred until animations exist). Needs a new `AudioManager`-shaped system plus hook points scattered across most of the existing request/UI-event surface (button `OnClick`s, `SelectionManager`'s mode-change events, `BattleController.RequestMove`'s per-step loop for movement SFX, `TurnManager`'s activation-changed event for a "your turn" cue). Architecturally the largest item here by a wide margin — worth treating as its own multi-session build, not folded into the same pass as the visual items above.

---

## 8. Coding Conventions & Feedback

Everything from the previous pass still holds (event subscription in Awake/OnDestroy, no auto-triggering game state from selection, static C# events for UI communication, don't hide panels with `SetActive(false)`, don't touch `Camera.rect`, preserve inspector alpha in `GradientSlider`). New ones from this pass:

**All fighter-name checks live in `PassiveManager`, full stop.** Established early, reinforced constantly — if you're about to write `if (fighter.FighterName == "X")` anywhere else, stop and route it through a `PassiveManager` hook instead, adding a new event source on the relevant manager class if one doesn't exist yet (see the `OnEnemyHit`/`OnCrit`/`OnBuffRemoved`/`OnAbilityUsed`/`OnGameStart` family in §3 for the established shape of "new hook, minimal, fires on the right event, PassiveManager does the naming").

**When you find a gap in the sig-charge-crediting principle, close it the same way every time.** Don't special-case — reuse `AbilityResolver.DamageChargeWeight`/`HealingChargeWeight`/`ShieldChargeWeight` and grant charge to whoever's named as the source (`SourceFighterName` pattern), guarded against that fighter being dead.

**Prefer extending an existing general mechanism over a one-off special case, but don't over-build for a hypothetical.** The dynamicValue/Condition system, the buff-removal family, the terrain-cost formula, and the Ring/facing-rotation fixes all deliberately generalize past the one fighter that first needed them (because a second and third fighter needed the same shape within the same session) — but things like `RemoveAllBuffs`'s "consumed" semantics for count-based dynamicValue sources were explicitly left unimplemented rather than guessed at, because nothing needs them yet. When in doubt, ask rather than build the speculative case.

**Give a genuine complexity/feasibility assessment before implementing, especially when the user is unsure.** Several systems this pass (SecondaryEffect targeting, the Ring shape, VyGar's reworked passive) started as "is this even feasible?" questions — the right response was to actually trace the code paths and give a grounded answer, not to hedge or immediately start coding. Several of these also surfaced real pre-existing bugs in the process (the `HasMovedThisTurn` vs `RemainingMovePoints` end-turn bug, the `GetModifiedResistance` essence filter, Root's mid-turn movement bug) — tracing things properly tends to find these, worth doing even when not explicitly asked to audit.

**If you're about to write a second JSON entry with a mismatched shape/range from the primary effect, stop.** `AbilityResolver`/`SelectionManager` only compute one `shapeTiles` set per click (from the primary effect's own shape) — a second effect with a genuinely different shape/range (not just a different `TargetType`) cannot receive its own coverage without either `RequiresSecondaryTarget` (a real second click) or being pulled out of the JSON entirely and hardcoded as a reaction to `AbilityResolver.OnAbilityUsed` (Raish's Pack Call — a 3-tile-radius ally buff bundled into a cross-size-1 damage Sig — is the precedent for this specific case).

---

## 9. Known Gaps / Next-Priority Candidates (consolidated)

**In-progress / explicitly next:**
- **Path-stepped movement.** `Pathfinder.GetReachableTiles` needs to track parent pointers (not just cost) so a route can be reconstructed consistently with the cost that was already computed — running `FindPath` as a second, independent algorithm risks disagreeing with the cost calculation on multi-route ties. `MoveResolver.ExecuteMove` then needs to step through the reconstructed path tile-by-tile (calling `HandleFighterEntered` per step, not just at the destination) instead of one direct jump. `TileHighlighter` needs a new "show path" preview, distinct from the existing range/hover highlights. Side effect once built: the Root-mid-turn fix (§3) starts actually mattering — a fighter Rooted by a trap partway through a multi-tile move will genuinely stop there instead of "arriving" anyway.
- **Cardinal-direction-only single-target restriction ("rook" targeting).** Discussed and scoped, not yet built. `Single`/`Ring` anchor selection in `AbilityTargeting.GetValidTargetTiles` currently scans the whole board within Manhattan range; the fix is to swap it for the same cardinal-direction loop `Line`/`Cone`/`Cross`/`Box` already use (nearly a one-line change, `Single` and `Ring` could likely collapse into that same switch case). Open question not yet answered: should `Ring`'s anchor placement also be constrained, or just `Single` — they serve different purposes (Ring is an AoE placement, already omnidirectional in its own spread once centered; Single is a direct snipe, which is the actual complaint). This is a roster-wide balance shift (nearly every fighter has a Single-target move), not a narrow fix — confirm scope before implementing.

**Known simplifications (deliberate, not bugs, but worth revisiting):**
- Vemk Parlas's Skill and Ulmika's Skill both had "select up to 2/N targets" in their source design text — both simplified to single-target for now. True multi-select (a variable-count individual-target picker) is a bigger targeting-model question than anything currently built and was explicitly deferred.
- Trustless Engineer's Trappings rolls Rooted-chance and buff-loss-chance as two independent 50% rolls; the source text reads more like one combined roll gating both.
- Ulmika's Sig doesn't remove enemy-placed tile effects (that clause of her design text) — there's no tile-effect-removal mechanism in `TileEffectManager` at all yet (only placement/trigger/decay).

**UI / polish (smaller scope, from this pass):**
- ~~`TileHighlighter` sits in a sorting layer below the character sprite~~ — **fixed**, see §7.
- No good way to inspect a tile's effects when it's occupied by a fighter (can't click an occupied tile at all right now).
- No sound system at all yet — see §7's UI Upgrade Backlog, Tier 4.
- B-key bias toggle and R-key facing rotation have no on-screen indicator (both have TODO comments in code).

**UI / polish (carried over, unchanged):**
- Restriction toggle UI in CharSelect top bar (logic already exists, no control wired to it).
- Customization panel wired to `BalanceSettings` (logic already exists, no UI at all).
- Per-slot cooldown number overlay on Skill/Skill2 buttons (single detail-panel display exists, not per-slot).

**Housekeeping:**
- Balance is entirely untuned across the full 30-fighter roster — stats, numbers, cooldowns are first-pass inferences (flagged repeatedly during content authoring), expect a real pass later.
- Large working-tree diff likely still uncommitted — confirm git status before assuming anything below this line is actually merged anywhere.
