# Breakpoint — Project Notes for Claude

This file is the persistent project context for Claude sessions. Read it first before making suggestions or changes to the project.

Last verified against actual code: 2026-07-15 (full pass across all `Assets/Scripts` files — previous version of this doc predated the multiplayer work and several combat systems that have since shipped).

---

## 1. Project Overview

**Breakpoint** — 2D top-down tactical turn-based strategy game. Inspired by Fire Emblem (unit activation, abilities) and Kingdom Rush (tower defense aesthetic). Built in Unity 6 with URP.

**Tech stack:** Unity 6, URP, C#, FishNet (multiplayer — implemented, see §4), UniTask (async), TextMeshPro. Multiplayer is tested locally via **ParrelSync** (a second cloned Unity Editor instance acting as the second client) — ParrelSync is purely an external testing tool, there is no in-code awareness of it anywhere in `Assets/Scripts`.

**Camera:** URP camera stack — Base game camera + Overlay UI camera. InputHandler uses a direct `[SerializeField] Camera` reference (not `Camera.main`) to avoid picking up the overlay camera.

**Turn system:** Alternating pick — Team 1 activates a fighter, then Team 2, alternating until all fighters have acted. Round ends when all fighters activated. `TurnManager` owns all round/turn logic.

**Server/client architecture:** `BattleController`/`TurnManager`/`AbilityResolver` etc. run the same logic in hotseat and online modes. In online mode, all state changes route through `BattleNetworkBridge` `[ServerRpc]`s to the server, which runs the real logic and broadcasts results back via `[ObserversRpc]`. Clients never mutate `Fighter`/`Board` state directly — they call `Network Apply*` methods (e.g. `Fighter.NetworkApplyHP`, `TurnManager.NetworkApplyActivation`) that just reflect server-authoritative state locally. This was the Phase 3 plan and it's now built — see §4 for architecture and known gaps (notably: **no server-side ownership/authorization check** on any ServerRpc yet).

**Canvas:** Screen Space - Camera (not Overlay) to support particles/art effects in later phases.

**UI event pattern:** Subscribe in `Awake`/`OnDestroy` (NOT `OnEnable`/`OnDisable`). Panels use `SetActive` to show/hide — `OnEnable`/`OnDisable` would cause them to miss events while inactive. Confirmed still followed consistently in newer files (e.g. `TileInfoPanel`).

**Fighter data:** Loaded from `StreamingAssets/fighters.json` via `FighterLoader`. Current roster is **8 fighters**: Jack, Avarice, Sanguine, Krakoa, Captain Dinso, Vas Drel, Rellin, Arkhe. Default hotseat/debug battle roster in `BattleController.InitializeBoard` is Jack/Avarice/Sanguine vs Krakoa/Captain Dinso/Vas Drel; Rellin and Arkhe exist in data + have passives implemented but aren't in that hardcoded default matchup (selectable via CharSelect draft).

**Movement:** Pool-based (`RemainingMovePoints`). Multi-step allowed while pool > 0. Triggered by Move button, not auto-shown on selection. `Pathfinder.GetReachableTiles` returns `Dictionary<Vector2Int, float>` (cost per tile). Movement is resolved tile-by-tile via `MoveResolver`, which also triggers tile-effect `OnEnter`/`OnEnterDestroy` events as the fighter crosses each tile (see §5).

---

## 2. Roadmap

### Phase 1 — Hotseat core loop ✅ COMPLETE

### Phase 2 — Character data + UI foundation ✅ MECHANICS COMPLETE (minor polish items remain)

- [x] JSON loader, BalanceSettings, FighterData, Ability data layer, sprite loading
- [x] Fighter stats, resistances, essence bonuses, UI pass (see original notes below — unchanged, still accurate)
- [x] **Cooldown enforcement** — `Ability.TickCooldown()` called per-fighter at turn start (`TurnManager.ActivateFighter`, not literally "round start" as originally planned — it's per-activation). `BattleController.RequestUseAbility` blocks on `ability.IsOnCooldown` and sets cooldown via `ability.SetCooldown()` on use.
- [x] **Hit resolution** — `HitResolver.RollHit`/`RollCrit`: accuracy-vs-dodge roll (5% floor) then separate crit roll, both before damage calc. Ally/Self/Tile-targeted abilities always hit.
- [x] **Damage formula completion** — resistance now applied in `HitResolver.CalculateDamage`: `damage = base × dmgMult × (1+essenceBonus) × critMult × (1-resistance)`. `True` essence correctly bypasses both essence bonus and resistance.
- [x] **Knockback** — `DisplacementResolver.Resolve`, called from `AbilityResolver.Apply` whenever `ability.Knockback != 0`. Walks tile-by-tile, respects board edges/impassable/occupied tiles, pull never lands target on caster's tile.
- [x] **Status effects from abilities** — `HitResolver.RollStatusEffects` reads `ability.StatusEffectsToApply`, rolls `ApplyChance`, `AbilityResolver.Apply` calls `Fighter.ApplyStatusEffect`. DoT/HoT tick at turn start (`Fighter.TickPeriodicEffects`, can kill and skip the fighter's turn), other durations tick at turn end (`Fighter.TickDurationEffects`).
- [ ] **Stun / action-blocking status effect — NOT implemented.** `StatusEffectType` has `DamageOverTime, HealOverTime, AccuracyModifier, DodgeModifier, CritRateModifier, CritDamageModifier, DamageMultiplier, ResistanceModifier, SpeedModifier, Root` — no type blocks ability use or forces a turn skip. `Root` only zeroes movement speed. If "Stun" is wanted as a mechanic it needs a new status type plus a check in `TurnManager`/`BattleController` before activation.
- [ ] **Per-slot cooldown UI indicator** — `AbilityPanel` shows `"CD n"`/`"Ready"` text, but only in the single detail panel for the currently-selected ability, not as a number overlay on each Skill/Skill2 slot button as originally planned. Minor UI polish item.
- [x] **Passive system** — trigger model settled on **event-subscription** (not switch/case): `PassiveManager.Initialize` subscribes to `Fighter.OnFighterDamaged/OnFighterDied/OnStatusEffectApplied/OnFighterMoved` and `TurnManager.OnFighterActivated`, then each handler does hardcoded `if (fighter.FighterName == "X")` checks. No `OnGameStart`, no `OnRoundStart`, no `AbilityOverride` mechanism exist. **8 characters have real passive logic**: Rellin (Deadeye — accuracy-over-100% → dmg stacks), Captain Dinso (Rally — AoE ally buff on activation), Vas Drel (Ward Resonance — chance to buff adjacent ally), Krakoa (Battle-Scarred — resistance per HP lost), Jack (Reactive Dodge — dodge on taking damage), Avarice (Martyr's Bloom — heal zone on death via tile-effect system), Sanguine (Hemorrhage — chance to duplicate own DoT), Arkhe (Leyline Flow — speed per cumulative tiles moved). Any other character name is simply a silent no-op (no stub markers).
- [ ] **Affinity/Essence track system — NOT implemented.** No `AffinityTracker`, no mark counters, no threshold/lock/fusion logic anywhere in the codebase. Design spec below (§3) is still just a spec.
- [ ] **Breakpoint bar — NOT implemented.** No meter, no capture-zone hook, no "Breakpoint Choice" trigger anywhere. Still needs full design + implementation.

### Phase 3 — Multiplayer 🟡 IN PROGRESS (functionally working, has real gaps — see §4)

FishNet wired end-to-end for Lobby → CharSelect draft → Battle. Tested via ParrelSync. See §4 for full architecture and the known-gaps list (no server-side team ownership check; game-over doesn't sync to clients).

### Phase 4 — Draft / roster 🟢 MOSTLY COMPLETE

Pre-battle fighter selection screen. See §6 (updated) for full status — alternating 3v3 draft, restriction engine logic, map generation, all panels, and `MatchSetup` contract are implemented and working (including online play). Two things remain:
- [ ] **Restriction toggle UI** — `CharSelectManager.RestrictionsEnabled` (+ `OnRestrictionsChanged` event) fully works on the logic side; no UI Toggle control calls the setter yet.
- [ ] **Customization panel** — nothing built. `BalanceSettings.cs` has a comment explicitly earmarking itself for this ("Future: populate these fields from a pre-match customization panel"). Needs a panel + fields wired to the `BalanceSettings` asset before match start.

### Phase 5 — Full content

All fighters, abilities, maps, balance. (8/? fighters currently in `fighters.json`.)

### Notes

- `GameOverPanel` → replace with `VictoryScreen` scene load on full UI pass. **Also now a real bug in online mode** — see §4 known gaps, it never fires on the client machine.
- `DefaultSigCharge` in `AbilityResolver`: damage ability = 10, heal/shield = 5, otherwise 0
- True essence bypasses both `essenceBonus` and resistance in damage formula

---

## 3. Combat Design

### Stats per Fighter

HP, Speed, SigChargeReq, DamageMultiplier, Accuracy, DodgeChance, CritRate (default 10%), CritDmg (default 150%), Shield, CurrentCharge

Resistances: Arcane, Elemental, Force, Corrupt (float modifiers, 0 = no resistance, negative = vulnerability)

### Damage Formula (implemented, `HitResolver.CalculateDamage`)

```
FinalDamage = BaseDamage × DamageMultiplier × (1 + EssenceDmgBonus) × (isCrit ? CritDmg : 1.0) × (1 - TargetResistance)
```

True essence: `damage = BaseDamage × (isCrit ? CritDmg : 1.0)` only — skips `EssenceDmgBonus` and resistance entirely. Shield absorbs before HP.

### Essence Damage Bonuses (per fighter, runtime only)

`BonusArcaneDmg` / `BonusElementalDmg` / `BonusForceDmg` / `BonusCorruptDmg` — all start at 0. Modified by zones, buffs, tile effects. Use `ModifyEssenceDmgBonus(essence, amt)` / `GetEssenceDmgBonus(essence)`.

### Hit Resolution (implemented, `HitResolver.cs`)

1. Roll accuracy vs dodge (`Random.value < max(5%, accuracy - dodge)`) → miss/hit. Ally/Self/Tile-targeted abilities always hit; only Enemy/All rolls actually check.
2. Roll crit (`Random.value < CritRate`), only if hit and `ability.Damage > 0` → apply CritDmg multiplier
3. Apply DamageMultiplier (user)
4. Apply resistance (target, by essence type) — via `Fighter.GetModifiedResistance`, sums base resistance + active `ResistanceModifier` status effects
5. Shield absorbs remainder before HP

Runs as a pure calculation (`HitResolver.Calculate` → immutable `HitResult`) before `AbilityResolver.Apply` commits any state.

### Essence / Damage Types

None, Arcane, Elemental, Force, Corrupt, True (True bypasses all resistance). Every ability has one essence. Currently drives resistance lookup only — does NOT yet drive affinity track accumulation (that system doesn't exist, see below).

### Charge / Signature System

- Each fighter has `CurrentCharge` filling toward `SigChargeReq`
- Charge is 1:1 with actual values dealt/healed/shielded (after all modifiers, not base values)
- Weights: `DamageChargeWeight` / `HealingChargeWeight` / `ShieldingChargeWeight` — all 1f, stored as constants in `AbilityResolver` for easy tuning
- `BaseSigCharge` on a move overrides the calculated value entirely (used for supports whose moves don't deal numbers)
- Using a Sig resets `CurrentCharge` to 0
- `CanUseSignature = CurrentCharge >= SigChargeReq`

### Ability Slots & Cooldowns

Passive (display only, USE disabled), Normal (no cooldown), Skill / Skill2 (BaseCooldown), Sig (charge-gated, USE blocked if charge < SigChargeReq). **Cooldowns decrement per-fighter at turn activation** (`TurnManager.ActivateFighter`, not literally round start). Ability blocked when `IsOnCooldown` — enforced in `BattleController.RequestUseAbility`. Per-slot cooldown number overlay still not built (see §2 Phase 2 remaining items).

`AbilityTargetType`: Enemy, Ally, Self, All (hits any), Tile (future tile placement)

### Status Effects (implemented, `StatusEffect.cs`)

Types actually implemented: `DamageOverTime, HealOverTime, AccuracyModifier, DodgeModifier, CritRateModifier, CritDamageModifier, DamageMultiplier, ResistanceModifier, SpeedModifier, Root`. **No Stun/action-block type exists** — `Root` only zeroes speed via `Fighter.GetModifiedSpeed`, it doesn't prevent ability use.

Each has: Duration (rounds), Magnitude, Stacks, Source (fighter), DamageType, ApplyChance (0–1) at the ability-effect-entry level. DoT/HoT tick + apply at the affected fighter's turn **start** (`Fighter.TickPeriodicEffects`, called from `TurnManager.ActivateFighter` — can kill the fighter and correctly skips their turn). All other durations tick down at turn **end** (`Fighter.TickDurationEffects`, called from `TurnManager.EndFighterTurn`).

### Passive System (implemented, `PassiveManager.cs`)

Event-subscription model (not a per-character switch/case as originally sketched): `PassiveManager.Initialize` subscribes to `Fighter.OnFighterDamaged`, `OnFighterDied`, `OnStatusEffectApplied`, `OnFighterMoved`, and `TurnManager.OnFighterActivated`. Each handler is a chain of `if (fighter.FighterName == "X")` checks. No `OnGameStart`/`OnRoundStart`/`AbilityOverride` triggers exist yet — if a character needs one of those, the trigger model needs extending first.

8 characters implemented: Rellin, Captain Dinso, Vas Drel, Krakoa, Jack, Avarice, Sanguine, Arkhe (see §2 for what each does). Avarice's passive is notable — it places a real tile effect (heal zone) via `TileEffectManager` on death, connecting the passive system to the zone system (§5).

### Affinity / Essence Track System — ⚠️ NOT IMPLEMENTED (design only)

Each team has an `AffinityTracker` with mark counters per essence type. Marks accumulate when abilities of that essence land. Bonus marks from outcome flags (stun, DoT, buff, debuff applied). When marks hit threshold → single-track effect fires, track locks.

- Force track → Stagger
- Elemental track → Sustain
- Arcane track → Tempo
- Corrupt track → DoT Amplification

If 2+ tracks fire within a window → **Dual Fusion** (6 pairs: F+E=Eruption, F+A=Disruption, F+C=Crush, E+A=Purify, E+C=Blightstorm, A+C=Mindbreak).

If 3+ tracks → **Triple Fusion** (Cataclysm: MaxHP loss).

Nothing built. (Note: `TileEffectAffinity`/`Tile.EssenceAffinity` are unrelated existing concepts — zone-targeting and terrain type respectively — don't confuse with this planned system.)

### Breakpoint Bar — ⚠️ NOT IMPLEMENTED (design only)

Tug-of-war float between teams. Old version: every action (damage, heal, crit, status) pushed it. New version: revolves around movement and map objectives (capture zones, positional control). Not yet designed in code at all — implement after Affinity system or independently. When bar maxes → triggering team gets a Breakpoint Choice (bonus action/effect, TBD for new version).

### Turn Order

Deliberate alternating pick: Team 1 chooses which fighter activates → Team 2 chooses → alternates until all fighters have acted → round ends. Intentional design choice (predictable, strategic activation order vs speed-based variance).

### Burndown (future)

Increasing true damage applied at round start after round ~12, scaling up each phase. Targets slowest fighters first. Prevents indefinite stalling. Not implemented.

---

## 4. Multiplayer Architecture (Phase 3 — implemented, tested via ParrelSync)

### Flow

```
MainMenuUI
 ├─ Play Local  → MatchSetup.Mode = Hotseat → CharacterSelect scene directly
 └─ Play Online → MatchSetup.Mode = Online  → LobbyScene
                    LobbyUI: host/join via FishNet Tugboat transport.
                    Server auto-loads CharacterSelect once 2 clients connected (LobbyUI.cs).
                        ↓
                    CharSelectNetworkBridge: host = Team 1 (MatchSetup.LocalTeamId = 1),
                    joining client = Team 2 (LocalTeamId = 2). Draft picks sync via RPCs.
                    On draft complete → CharSelectNetworkBridge.ServerStartBattle() loads BattleScene.
                        ↓
                    BattleNetworkBridge: server runs BattleController/TurnManager logic,
                    clients send CmdRequest* ServerRpcs, server broadcasts results via ObserversRpcs.
```

Hotseat mode skips the lobby entirely and both teams are controlled locally (`MatchSetup.LocalTeamId = 0` means "no restriction, all teams selectable").

### Key files

- `Assets/Scripts/UI/MainMenuUI.cs` — entry point, sets `MatchSetup.Mode`, no networking code itself.
- `Assets/Scripts/Network/LobbyUI.cs` — host/join UI, Tugboat transport, auto-transitions to CharSelect at 2 clients.
- `Assets/Scripts/Network/CharSelectNetworkBridge.cs` + `CharSelectBridgeSpawner.cs` — draft-scene RPC bridge (`CmdTryPick`, `CmdResetDraft`, `CmdRequestState`; `RpcGenerateMap`, `RpcSyncReset`, `RpcPickMade`).
- `Assets/Scripts/Network/BattleNetworkBridge.cs` + `BattleNetworkBridgeSpawner.cs` — battle-scene RPC bridge (`CmdActivateFighter`, `CmdRequestMove`, `CmdRequestAbility`, `CmdRequestReposition`, `CmdRequestEndTurn`; `RpcSyncFighterStates`, `RpcFighterActivated`, `RpcFighterMoved`, `RpcTurnEnded`, `RpcRoundStarted`, `RpcSyncAbilityCooldown`, `RpcBattleLog`).
- Bridges are spawned server-side onto their own `NetworkObject` (not scene-placed) so FishNet syncs them; `BattleController`/`TurnManager`/`CharSelectManager` stay plain MonoBehaviours unaware of networking, calling `Network Apply*` methods on `Fighter`/`TurnManager`/`SelectionManager` to reflect server state on clients.

### Known gaps (real, not stylistic — worth prioritizing before calling multiplayer done)

1. **No server-side ownership/authorization check on any ServerRpc.** Every `[ServerRpc]` in both bridges is `RequireOwnership = false` and looks up fighters purely by name — none check the calling connection's team against `fighter.TeamId` or `TurnManager.ActiveTeamId`. The only team gating is client-side/cosmetic (`SelectionManager.TryPreviewFighter` blocks selecting enemy fighters in the UI). A modified or buggy client could currently send `CmdRequestMove`/`CmdRequestAbility`/`CmdActivateFighter` for the other team's fighters and the server would execute it. Low real-world risk for a 2-friend hobby match, but should be fixed before any wider testing — needs a connection→team mapping and a check in each `CmdRequest*` handler.
2. **Game-over does not sync to the client in online mode.** `TurnManager.OnGameOver` only fires where `CheckGameOver()` actually runs — the server (since `EndFighterTurn()` only executes server-side in online mode; clients get state via `NetworkApplyTurnEnded`/`NetworkApplyRoundStarted`, neither of which calls `CheckGameOver()`). `BattleNetworkBridge` doesn't subscribe to `OnGameOver` and there's no `RpcGameOver`. Net effect: `GameOverPanel` shows on the host machine only; the client's End Turn button stays enabled forever with no win screen. Needs a `RpcGameOver` broadcast from the server's `OnGameOver` handler.
3. Minor: `CharSelectNetworkBridge.cs` has several commented-out (not deleted) `Debug.Log` calls left over from draft-flow debugging (lines ~23, 42, 117, 126, 152), and `OnServerDraftComplete` is an empty pass-through handler kept only for subscribe/unsubscribe symmetry. Harmless but worth cleaning up.

---

## 5. Tile Effect / Zone System (new — not in any earlier version of this doc)

A full system for abilities (and one passive) to drop a lingering effect onto board tiles — DoT zones, heal zones, buff zones, etc. Unrelated to the (unbuilt) Affinity Track / Breakpoint Bar systems — don't conflate them.

- **`Tile.cs`** — base per-tile data (`EssenceAffinity` = terrain type for visuals/movement, `MovementCost`, occupancy). Pre-existing/foundational.
- **`TileEffect.cs`** — one active zone instance on a tile: `Name`, remaining `Duration`, `Trigger` (`OnTurnEnd` / `OnEnter` / `OnEnterDestroy` / `Persistent`), `Affinity` (which team it affects), `Damage`/`Healing`/`Shielding`, `DestroyOnTrigger`, attached status effects. Built from ability-authored `AbilityTileEffect` data.
- **`TileEffectManager.cs`** (singleton) — the engine. Subscribes to `TurnManager.OnFighterActivated/OnFighterTurnEnded/OnRoundEnded`. `PlaceEffect()` called by `AbilityResolver` (any ability with `TileEffectToPlace`) and by `PassiveManager` (Avarice's death heal-zone). `HandleFighterEntered()` called from `MoveResolver` per tile step for `OnEnter`/`OnEnterDestroy` triggers. Fires `OnTurnEnd`-trigger effects at the occupying fighter's turn end, applies `Persistent` effects as a hidden status effect at turn start, decrements/expires everything at round end.
- **`TileEffectChip.cs`** / **`TileInfoPanel.cs`** — UI. Clicking an empty tile shows a bottom panel listing active effects on it (name, duration, damage/heal/shield/status summary), live-updated via `TileEffectManager.OnTileEffectsChanged`.
- **`MapTheme.cs`** — a `ScriptableObject` for per-tier tile sprites, referenced by `MapData.theme`, but **nothing in `Assets/Scripts` actually reads it** — `BoardRenderer` pulls sprites directly from per-tile baked data instead. Appears to be dead/unused at the runtime-script level (may only be used by editor map-authoring tooling not present in `Assets/Scripts`).

---

## 6. Character Select Design (updated — mostly implemented, was previously just a spec)

### Draft rules — ✅ implemented

- 3v3 alternating pick (T1, T2, T1, T2, T1, T2) — `CharSelectManager.GetTeamIndexForPick`
- First picker randomized at scene load (coin flip) — `Random.Range(0,2)` in `StartDraftInternal`/`ProcessReset`/`ApplyResetFromNetwork`
- Team that picks SECOND acts FIRST in battle (Round 1) — `MatchSetup.FirstActingTeam` set accordingly, consumed in `TurnManager.cs`

### Scene layout (map-as-world-object) — ✅ implemented

- Map fills the scene as a real world-space object using the same `Board`/`TerrainGenerator`/`BoardRenderer` as battle (`CharSelectManager.GenerateMapWithSeed`)
- Seed stored to `MatchSetup.MapSeed`, consumed by `BattleController` to regenerate the identical map

### Panels — ✅ implemented (functionally; exact width percentages are a scene/prefab layout concern, not verifiable from script)

- **Left:** `CharacterGridPanel` — scrollable grid of `SelectionCard`s built from `FighterLoader.LoadRoster()`
- **Right:** `CharacterPreviewPanel` — portrait, name, rarity, HP/Speed/SigChargeReq/Accuracy/Crit, resistances, ability-slot buttons (Passive/Normal/Skill/Skill2/Sig)
- **Bottom:** `TeamPicksPanel` (×2, one per team) — portraits + rarity pips, always visible
- **Top bar:** Confirm/Ready button implemented (`CharSelectUI`, blinks + relabels to "Start" on draft complete). **Restriction toggle and customization button are NOT in the UI** — see gaps below.

### Data passed CharSelect → Battle (`MatchSetup`, static)

`Team1Fighters[3]`, `Team2Fighters[3]`, `FirstActingTeam`, `MapSeed` — as originally planned, plus networking additions: `Mode` (Hotseat/Online), `LocalTeamId` (0 = hotseat/no restriction, 1 or 2 = networked team), `IsReady` validation helper.

### Restriction engine — ✅ logic implemented, ⚠️ no UI toggle

- Rarities: L=4, UR=3, R=2, UC=1, C=0 (`RestrictionEngine.cs`, exact match to spec)
- Valid team patterns: {L,≤R,≤UC} | {UR,UR,≤UC} | {UR,R,R} — implemented via greedy best-fit assignment
- `CharSelectManager.RestrictionsEnabled` + `OnRestrictionsChanged` event exist and gate `SelectionCard` greying (`blockedTint`), but no UI Toggle control calls the setter — currently only settable via inspector default (`true`)

### Customization panel — ❌ not built

`BalanceSettings.cs` (`hpMultiplier`, `speedAdd`, `sigChargeMultiplier`) exists and is consumed in battle, and its own code comment earmarks it for this feature, but there is no panel/button/slider anywhere in `Assets/Scripts` wired to it. Needs building from scratch.

### Online play integration (new, not in original spec)

`CharSelectNetworkBridge` drives the whole draft over the network when `MatchSetup.Mode == Online` — host is Team 1, joining client is Team 2, map seed + picks propagate via RPCs (see §4).

---

## 7. UI Plan (Phase 2 — core panels COMPLETE as of 2026-04-02; additional panels since added)

### FighterInfoPanel ✅

- Portrait (left, preserveAspect)
- Name text + HP slider with value text (e.g. "100/175") + Charge slider with value text
- StatsPanel: 3×2 grid of StatEntry prefabs — Cr, Cd, Acc, Dod, Spd (via moveText), D.M. (format: "1.3x")
- ResGridPanel: 4 cells (Arcane/Elemental/Force/Corrupt) with icon (preset) + val text
- StatusEffectsPanel: spawns StatusChip per active effect (Name, Duration, Icon by type+essence, Stacks hidden at 1 / "x2" at 2+)
- Move button wired to `SelectionManager.EnterMoveMode()`
- Sliders use `GradientSlider` (optional) — alpha preserved from inspector, not hardcoded

### AbilityPanel ✅ (see §2/§3 for the still-missing per-slot cooldown number overlay)

- 5 slot toggle buttons: Passive (always on), Normal (on if exists), Skill (on if exists), Skill2 (on if exists), Sig (always on)
- Ability display: essence icon (`Resources/effecticons/`), name, description (mechanics field), cooldown text ("CD 2" / "Ready" / empty) — shown only for the currently-selected/previewed ability, not per-slot
- USE button: disabled for Passive, disabled for Sig when charge < SigChargeReq, disabled while on cooldown
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

### TileInfoPanel ✅ (new, see §5)

Always-on bottom panel. Shows active tile-zone effects when the player clicks an empty tile; live-updates via `TileEffectManager.OnTileEffectsChanged`.

### Other UI files present (not yet documented in detail here — check the file directly if working on these)

- `BattleHUD.cs`, `HistoryPanel.cs`, `BattleLogger.cs`/`LogEntryUI.cs` — battle log / HUD chrome
- `GameOverPanel.cs` — see §2/§4 for the online-mode desync gap
- `MainMenuUI.cs` — see §4 for the full scene-flow it owns

### Notes

- `GradientSlider.cs`: attach to slider, drag Fill image in, set colors in inspector
- `StatusChip` prefab: Icon + NameText + DurationText + StacksText (StacksText hidden when stacks == 1)
- Camera viewport: **DO NOT touch `Camera.rect`** — caused persistent black bar
- `GameOverPanel` → replace with `VictoryScreen` scene on full art pass

---

## 8. Coding Conventions & Feedback

**Don't auto-trigger game state from selection.** Player actions (move, ability use) must be deliberate — triggered by explicit UI button presses, not just clicking a fighter.

- *Why:* Auto-showing move range on fighter select felt undeliberate. Wanted "click fighter → panels appear → click Move button → range shows → click tile".
- *Apply:* Never auto-enter a targeting mode on selection. Always require an explicit button press.

**Area shape uses center anchor with B-key bias toggle** for even-width boxes (not corner anchor).

- *Why:* Corner anchor was unintuitive — player had to click the "wrong" tile to hit the right area. Center anchor + bias toggle is more ergodic.
- Still has an open `// TODO: add a visible UI indicator so players know B toggles box bias` in `SelectionManager.cs`.

**Use `Awake`/`OnDestroy` for event subscriptions in UI panels, never `OnEnable`/`OnDisable`.**

- *Why:* Panels use `SetActive` to hide — `OnDisable` fires when hidden, unsubscribing the panel so it misses events and never reopens.
- Confirmed still followed in every newer panel checked (e.g. `TileInfoPanel`).

**Static C# events on manager classes for UI communication** (not UnityEvents). Also now the backbone of the multiplayer bridges — server subscribes to the same static events (`TurnManager.OnFighterActivated` etc.) to know when to broadcast.

**Don't call `Deselect()` on empty tile clicks** when a fighter is already selected.

- *Why:* Felt jarring — clicking anywhere accidentally closed panels with no recovery path.

**`GameOverPanel` is a placeholder.** On the full UI pass, replace with a dedicated `VictoryScreen` scene load via `SceneManager`. Now also needs the online-mode sync fix (§4) regardless of when the visual replacement happens.

**Don't use `SetActive(false)` to hide UI panels on deselect.** Instead, clear values to defaults and use interactable toggling.

- *Why:* Disappearing elements feel unpolished. State changes (greyed out, cleared text) are preferred over panels vanishing.
- *Apply:* Panels stay visible always. On deselect: clear text fields, zero sliders, disable buttons. Never call `gameObject.SetActive(false)` on main panels.

**`GradientSlider` and similar scripts that set color/alpha on other components must respect the alpha already set in the inspector.**

- *Why:* Hardcoded alpha in script overrides inspector value and causes confusion about why changes don't stick. Read `fill.color.a` and preserve it.
- *Apply:* When lerping colors, read the existing alpha first: `float alpha = fill.color.a`, then use it in the new Color.

**Don't set `Camera.rect` in `CameraController`** to carve out UI space. The approach caused a persistent black bar that survived inspector resets.

- *Why:* Camera viewport manipulation at runtime serializes unexpectedly and is hard to recover from. Use a fixed Game View resolution instead and accept minor spacing variance.
- *Apply:* `CameraController` only sets `orthographicSize` and `position`. Never touch `mainCamera.rect`.

---

## 9. Known Gaps / Next-Priority Candidates (consolidated)

Pulled together from the sections above so a future session can pick a lane quickly without re-reading everything:

**Correctness bugs (worth fixing soon):**
- Game-over doesn't sync to the online client — host sees `GameOverPanel`, client never does, client's End Turn stays enabled. (§4 gap 2)
- No server-side team-ownership check on any battle/draft ServerRpc — client could currently act on the other team's fighters. (§4 gap 1)

**Missing mechanics (larger scope):**
- Stun / action-blocking status effect type doesn't exist yet.
- Affinity/Essence Track system — fully unbuilt, design-only.
- Breakpoint Bar — fully unbuilt, design-only, needs its new (movement/objective-based) design finished before implementation.

**UI polish (smaller scope):**
- Per-slot cooldown number overlay on Skill/Skill2 buttons.
- Restriction toggle UI in CharSelect top bar (logic already exists).
- Customization panel wired to `BalanceSettings` (logic already exists, no UI at all).

**Housekeeping:**
- Commented-out debug logs in `CharSelectNetworkBridge.cs`.
- `MapTheme.cs` appears unused by any runtime script — confirm if it's still needed via editor tooling before deleting.
- Large uncommitted working-tree diff exists (multiplayer + related refactors) — not yet committed as of this doc's last verification pass.
