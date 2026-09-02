using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

// [CLIENT] SelectionManager owns input routing and selection state only.
// It never mutates Fighter or Board state directly — all game state changes
// go through BattleController request methods.
public enum SelectionState
{
    Idle,
    FighterPreviewed,    // clicked to inspect — info shown, no actions committed yet
    FighterSelected,
    Targeting,
    RepositionTargeting, // second click required to place a hit fighter somewhere
    SecondaryTargeting,  // second click required for an ability's own follow-up target
                         // (e.g. Vemk Parlas's Sig — pick which ally receives the transferred buffs)
    MultiTargeting       // repeated clicks, one fighter at a time, until MaxTargets is reached or
                         // the player re-confirms early — see AbilityEffect.MaxTargets
}

public class SelectionManager : MonoBehaviour
{
    public static SelectionManager Instance { get; private set; }

    public SelectionState CurrentState { get; private set; } = SelectionState.Idle;
    public Fighter SelectedFighter    { get; private set; }
    public Ability SelectedAbility    { get; private set; }

    // True while the player has pressed Move and is picking a destination
    public bool InMoveMode { get; private set; }

    // ── Events (UI subscribes to these) ────────────────────────────────────
    public static event Action<Fighter> OnFighterPreviewed;  // click to inspect (no activation)
    public static event Action<Fighter> OnFighterSelected;   // fighter activated for their turn
    public static event Action          OnFighterDeselected;
    public static event Action<Ability> OnAbilitySelected;
    public static event Action          OnTargetingCancelled;

    /// Fired when the player confirms a target. Carry these into AbilityResolver.
    public static event Action<Fighter, Ability, Vector2Int, List<Vector2Int>> OnAbilityConfirmed;

    /// Fired when move mode starts (true) or ends (false). UI wires Move button state here.
    public static event Action<bool>    OnMoveModeChanged;

    /// Fired when the player clicks an empty tile while not in move mode or targeting.
    /// TileInfoPanel subscribes to show tile details.
    public static event Action<Vector2Int> OnTileSelected;
    public static event Action             OnTileDeselected;

    /// Fired after every move step so UI can update the movement-points display.
    /// Args: (remaining, max)
    public static event Action<float, float> OnMovePointsChanged;

    // ── Private refs ───────────────────────────────────────────────────────
    private Fighter          _previewedFighter;
    private Board            _board;
    private TileHighlighter  _tileHighlighter;
    private Pathfinder       _pathfinder;

    // Movement range tiles for current move-mode session
    private HashSet<Vector2Int> _moveRangeTiles = new HashSet<Vector2Int>();

    // Area box bias — toggled with B key during targeting (even-width boxes only)
    // TODO: add a visible UI indicator so players know B toggles box bias
    private bool _areaBiasLeft = true;

    // Explicit facing override for Line/Cone/Box — cycled with R key during targeting. Null means
    // "not yet overridden, use the auto-inferred (anchor - caster) direction" (today's behavior).
    // Necessary because a range-0 effect only ever has one valid anchor (the caster's own tile),
    // so auto-inference has nothing to read a direction from and silently defaults to Up — R lets
    // the player pick a real facing regardless of range.
    // TODO: add a visible UI indicator so players know R rotates facing.
    private static readonly Vector2Int[] FacingDirections =
        { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
    private Vector2Int? _facingOverride = null;

    // Cache the last hovered position so B-key toggling refreshes the preview in place
    private Vector2Int       _lastHoveredPos      = new Vector2Int(-1, -1);
    private List<Vector2Int> _currentShapePreview = new List<Vector2Int>();

    // Reposition targeting state
    private Fighter              _repositionTarget;
    private int                  _repositionRange;
    private HashSet<Vector2Int>  _repositionTiles = new HashSet<Vector2Int>();

    // Secondary targeting state — the ability whose SecondaryEffect is awaiting its own target.
    // SelectedFighter/SelectedAbility are already cleared by the time this phase starts (mirrors
    // how RepositionTargeting stashes its own state separately), so this is what the second click
    // resolves against.
    private Ability _secondaryAbility;

    // Multi-select targeting state — picks accumulated so far this phase. Which ability/effect
    // they resolve against is SelectedAbility.PrimaryEffect or _secondaryAbility.SecondaryEffect
    // depending on _multiSelectIsSecondary (mirrors how MultiTargeting can be entered from either
    // the primary click phase or the secondary one — see EnterMultiTargeting).
    private readonly List<Vector2Int> _multiSelectPicks = new List<Vector2Int>();
    private bool _multiSelectIsSecondary;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Initialize(Board board, TileHighlighter tileHighlighter)
    {
        _board           = board;
        _tileHighlighter = tileHighlighter;
        _pathfinder      = new Pathfinder(board);

        InputHandler.OnTileClicked      += HandleTileClicked;
        InputHandler.OnTileRightClicked += HandleTileRightClicked;
        InputHandler.OnTileHovered      += HandleTileHovered;
        TurnManager.OnFighterTurnEnded  += HandleFighterTurnEnded;

        Debug.Log("[SelectionManager] Initialized");
    }

    private void OnDestroy()
    {
        InputHandler.OnTileClicked      -= HandleTileClicked;
        InputHandler.OnTileRightClicked -= HandleTileRightClicked;
        InputHandler.OnTileHovered      -= HandleTileHovered;
        TurnManager.OnFighterTurnEnded  -= HandleFighterTurnEnded;
    }

    // ── Input routing ──────────────────────────────────────────────────────

    private void HandleTileClicked(Vector2Int pos)
    {
        // Block all input while the selected fighter is mid-stepped-move (see Fighter.IsMoving) —
        // covers local host/hotseat (RequestMove is still awaiting) and a remote client (state
        // syncs keep IsMoving true until the server's loop finishes), same flag either way.
        if (SelectedFighter != null && SelectedFighter.IsMoving) return;

        switch (CurrentState)
        {
            case SelectionState.Idle:
            case SelectionState.FighterPreviewed:
            {
                var idleTile = _board.GetTile(pos);
                if (idleTile?.OccupyingCharacter != null)
                    TryPreviewFighter(pos);
                else if (idleTile != null)
                    OnTileSelected?.Invoke(pos);
                break;
            }

            case SelectionState.FighterSelected:
                var tile = _board.GetTile(pos);
                bool isOccupied = tile?.OccupyingCharacter != null;

                if (InMoveMode && _moveRangeTiles.Contains(pos) && !isOccupied)
                {
                    TryMove(pos).Forget();
                }
                else if (!InMoveMode && tile != null && !isOccupied)
                {
                    OnTileSelected?.Invoke(pos);
                }
                // Clicking any fighter while one is active does nothing — you're committed
                break;

            case SelectionState.Targeting:
                TryConfirmTarget(pos);
                break;

            case SelectionState.RepositionTargeting:
                TryConfirmReposition(pos);
                break;

            case SelectionState.SecondaryTargeting:
                TryConfirmSecondaryTarget(pos);
                break;

            case SelectionState.MultiTargeting:
                TryPickMultiTarget(pos);
                break;
        }
    }

    // Right-click only ever means "un-pick this tile" and only means anything mid multi-select —
    // everywhere else it's a no-op, unlike left-click which routes through the full state machine.
    private void HandleTileRightClicked(Vector2Int pos)
    {
        if (CurrentState != SelectionState.MultiTargeting) return;
        if (!_multiSelectPicks.Remove(pos)) return;

        _tileHighlighter.ShowMultiSelect(_multiSelectPicks);
        Debug.Log($"[SelectionManager] Multi-select pick removed at {pos} ({_multiSelectPicks.Count} remaining)");
    }

    // The effect currently driving targeting — PrimaryEffect during the first click,
    // SecondaryEffect during the follow-up phase. Null outside of targeting entirely.
    private AbilityEffect CurrentTargetingEffect => CurrentState switch
    {
        SelectionState.Targeting          => SelectedAbility?.PrimaryEffect,
        SelectionState.SecondaryTargeting => _secondaryAbility?.SecondaryEffect,
        SelectionState.MultiTargeting     => _multiSelectIsSecondary ? _secondaryAbility?.SecondaryEffect : SelectedAbility?.PrimaryEffect,
        _                                 => null
    };

    private void Update()
    {
        var effect = CurrentTargetingEffect;

        if ((effect?.Shape == AbilityShape.Box || effect?.Shape == AbilityShape.Ring) && Input.GetKeyDown(KeyCode.B))
        {
            _areaBiasLeft = !_areaBiasLeft;
            if (_lastHoveredPos.x >= 0)
                UpdateShapePreview(_lastHoveredPos, effect);
        }

        // A Line is a fixed beam, not manually rotatable — except at range 0, where it's the ONLY
        // way to pick a direction at all (only one valid anchor: the caster's own tile, so hover
        // can't communicate a facing either). Cone/Box stay rotatable at any range.
        bool rotatable = effect != null &&
            (effect.Shape == AbilityShape.Cone || effect.Shape == AbilityShape.Box ||
             (effect.Shape == AbilityShape.Line && effect.Range == 0));
        if (rotatable && Input.GetKeyDown(KeyCode.R))
        {
            int currentIndex = _facingOverride.HasValue ? Array.IndexOf(FacingDirections, _facingOverride.Value) : 0;
            _facingOverride = FacingDirections[(currentIndex + 1) % FacingDirections.Length];
            if (_lastHoveredPos.x >= 0)
                UpdateShapePreview(_lastHoveredPos, effect);
        }
    }

    private void HandleTileHovered(Vector2Int pos)
    {
        if (CurrentState == SelectionState.Targeting || CurrentState == SelectionState.SecondaryTargeting ||
            CurrentState == SelectionState.MultiTargeting)
        {
            // MultiTargeting's effect is always Shape==Single (see EnterMultiTargeting's gate),
            // so GetShapeTiles just returns [pos] — this shows the same anchor indicator normal
            // targeting does, previewing which tile you're about to add to the pick list.
            _lastHoveredPos = pos;
            UpdateShapePreview(pos, CurrentTargetingEffect);
        }
        else if (CurrentState == SelectionState.RepositionTargeting)
        {
            if (_repositionTiles.Contains(pos))
                _tileHighlighter.ShowMoveHover(pos);
            else
                _tileHighlighter.ClearMoveHover();
        }
        else if (InMoveMode)
        {
            if (_moveRangeTiles.Contains(pos))
                _tileHighlighter.ShowMoveHover(pos);
            else
                _tileHighlighter.ClearMoveHover();
        }
    }

    private void HandleFighterTurnEnded(Fighter fighter)
    {
        if (fighter == SelectedFighter)
            Deselect();
    }

    // ── Fighter selection ──────────────────────────────────────────────────

    private void TryPreviewFighter(Vector2Int pos)
    {
        var tile = _board.GetTile(pos);
        if (tile == null || tile.OccupyingCharacter == null) return;

        var fighter = tile.OccupyingCharacter.GetComponent<Fighter>();
        if (fighter == null || fighter.IsDead) return;

        // Online: only allow previewing your own team's fighters
        if (MatchSetup.LocalTeamId != 0 && fighter.TeamId != MatchSetup.LocalTeamId) return;

        // Already previewing this fighter — do nothing
        if (fighter == _previewedFighter) return;

        _previewedFighter = fighter;
        CurrentState      = SelectionState.FighterPreviewed;
        ExitMoveMode();
        _tileHighlighter.ClearRange();
        _tileHighlighter.ClearShape();

        OnTileDeselected?.Invoke();
        OnFighterPreviewed?.Invoke(fighter);
    }

    // Called by the Activate button in FighterInfoPanel — commits the previewed fighter for their turn.
    public void ActivatePreviewedFighter()
    {
        if (_previewedFighter == null) return;
        if (!TurnManager.Instance.CanActivate(_previewedFighter)) return;

        // Online client: send to server and wait for RpcFighterActivated to come back
        if (MatchSetup.Mode == GameMode.Online && !BattleNetworkBridge.IsServer)
        {
            BattleNetworkBridge.Instance?.CmdActivateFighter(_previewedFighter.FighterName);
            return;
        }

        SelectedFighter   = _previewedFighter;
        SelectedAbility   = null;
        CurrentState      = SelectionState.FighterSelected;

        TurnManager.Instance.ActivateFighter(SelectedFighter);

        if (SelectedFighter.IsDead) return;

        Debug.Log($"[SelectionManager] Activated fighter: {SelectedFighter.FighterName}");
        OnFighterSelected?.Invoke(SelectedFighter);
    }

    // Called by BattleNetworkBridge when the server confirms activation of one of our fighters.
    public void NetworkApplyActivation(Fighter fighter)
    {
        _previewedFighter = fighter;
        SelectedFighter   = fighter;
        SelectedAbility   = null;
        CurrentState      = SelectionState.FighterSelected;
        OnFighterSelected?.Invoke(fighter);
    }

    public void FireMovePointsChanged(float remaining, float max)
        => OnMovePointsChanged?.Invoke(remaining, max);

    public void RefreshMoveRange()
    {
        if (!InMoveMode || SelectedFighter == null) return;
        var reachable = _pathfinder.GetReachableTiles(SelectedFighter.GridPosition, SelectedFighter.RemainingMovePoints, SelectedFighter);
        _moveRangeTiles = new HashSet<Vector2Int>(reachable.Keys);
        _tileHighlighter.ClearMoveRange();
        _tileHighlighter.ClearMoveHover();
        _tileHighlighter.ShowMoveRange(_moveRangeTiles);
    }

    public void Deselect()
    {
        SelectedFighter   = null;
        SelectedAbility   = null;
        _previewedFighter = null;
        CurrentState      = SelectionState.Idle;
        _areaBiasLeft    = true;
        _facingOverride  = null;
        _lastHoveredPos  = new Vector2Int(-1, -1);

        ExitMoveMode();
        ClearRepositionState();
        _secondaryAbility = null;
        _multiSelectPicks.Clear();
        _multiSelectIsSecondary = false;
        _tileHighlighter.ClearRange();
        _tileHighlighter.ClearShape();
        _tileHighlighter.ClearMultiSelect();

        OnFighterDeselected?.Invoke();
    }

    // ── Movement (called by UI Move button) ────────────────────────────────

    /// Called by the Move button. Toggles move mode on/off.
    public void EnterMoveMode()
    {
        if (CurrentState != SelectionState.FighterSelected) return;
        if (SelectedFighter == null || SelectedFighter.RemainingMovePoints <= 0f) return;
        if (SelectedFighter.IsMoving) return;

        if (InMoveMode) { ExitMoveMode(); return; } // toggle off

        InMoveMode = true;
        var reachable = _pathfinder.GetReachableTiles(SelectedFighter.GridPosition, SelectedFighter.RemainingMovePoints, SelectedFighter);
        _moveRangeTiles = new HashSet<Vector2Int>(reachable.Keys);
        _tileHighlighter.ShowMoveRange(_moveRangeTiles);

        OnMoveModeChanged?.Invoke(true);
        Debug.Log($"[SelectionManager] Move mode ON — {_moveRangeTiles.Count} reachable tiles");
    }

    public void ExitMoveMode()
    {
        if (!InMoveMode) return;
        InMoveMode = false;
        _moveRangeTiles.Clear();
        _tileHighlighter.ClearMoveRange();
        _tileHighlighter.ClearMoveHover();
        OnMoveModeChanged?.Invoke(false);
    }

    // async: RequestMove now steps the fighter through the path over real time (see
    // MoveResolver/ProgressiveResolver) instead of resolving instantly. Input is blocked while
    // that's in flight (see the IsMoving guard at the top of HandleTileClicked/SelectAbility/
    // EnterMoveMode), so this can safely await the whole thing before refreshing the UI.
    private async UniTaskVoid TryMove(Vector2Int destination)
    {
        var fighter = SelectedFighter;
        if (fighter == null) return;

        float prevRemaining = fighter.RemainingMovePoints;
        await BattleController.Instance.RequestMove(fighter, destination);

        // Deselected (e.g. turn ended) while the move was animating — nothing left to refresh.
        if (SelectedFighter != fighter) return;

        if (fighter.RemainingMovePoints == prevRemaining)
            return; // move failed — no points were spent, position unchanged

        // Broadcast updated move points for the UI text
        OnMovePointsChanged?.Invoke(fighter.RemainingMovePoints, fighter.Speed);

        if (fighter.RemainingMovePoints > 0f)
        {
            // Refresh range from new position with remaining points
            var reachable = _pathfinder.GetReachableTiles(fighter.GridPosition, fighter.RemainingMovePoints, fighter);
            _moveRangeTiles = new HashSet<Vector2Int>(reachable.Keys);
            _tileHighlighter.ClearMoveRange();
            _tileHighlighter.ClearMoveHover();
            _tileHighlighter.ShowMoveRange(_moveRangeTiles);
        }
        else
        {
            ExitMoveMode();
        }
    }

    // ── Ability selection (called by UI ability buttons) ───────────────────

    public void SelectAbility(Ability ability)
    {
        if (SelectedFighter != null && SelectedFighter.IsMoving) return;

        // Re-clicking the SAME ability while its own multi-select is already in progress means
        // "confirm with whatever's picked so far" (the Use-button-again half of the design),
        // not "restart targeting". Only applies to the primary phase — SelectedAbility is always
        // null during a secondary-phase multi-select (cleared before that phase starts), so this
        // can't accidentally fire there.
        if (CurrentState == SelectionState.MultiTargeting && !_multiSelectIsSecondary &&
            SelectedAbility == ability && _multiSelectPicks.Count >= 1)
        {
            ConfirmMultiSelect();
            return;
        }

        if (CurrentState != SelectionState.FighterSelected && CurrentState != SelectionState.Targeting)
            return;

        ExitMoveMode(); // leave move mode when switching to ability targeting

        SelectedAbility = ability;

        var primaryEffect = ability.PrimaryEffect;

        // Fire OnAbilitySelected AFTER CurrentState settles in either branch — listeners (e.g.
        // AbilityPanel deciding whether to keep the Use button clickable) need to read the real
        // final state, not whatever it was before this call.
        if (primaryEffect != null && primaryEffect.MaxTargets > 1 && primaryEffect.Shape == AbilityShape.Single)
        {
            EnterMultiTargeting(isSecondary: false);
            Debug.Log($"[SelectionManager] Targeting with ability: {ability.Name}");
            OnAbilitySelected?.Invoke(ability);
            return;
        }

        CurrentState = SelectionState.Targeting;

        _tileHighlighter.ClearShape();
        var validTargets = AbilityTargeting.GetValidTargetTiles(SelectedFighter, ability.PrimaryEffect, _board);
        _tileHighlighter.ShowRange(validTargets);

        Debug.Log($"[SelectionManager] Targeting with ability: {ability.Name}");
        OnAbilitySelected?.Invoke(ability);
    }

    public void CancelTargeting()
    {
        if (CurrentState == SelectionState.MultiTargeting)
        {
            // Covers both phases: cancelling out of a primary multi-select drops back to
            // FighterSelected same as normal Targeting; cancelling a secondary-phase one gives up
            // on the follow-up target entirely (the primary effect already resolved by this point,
            // same as RepositionTargeting/SecondaryTargeting having no partial-undo today).
            _multiSelectPicks.Clear();
            _multiSelectIsSecondary = false;
            SelectedAbility   = null;
            _secondaryAbility = null;
            CurrentState      = SelectionState.FighterSelected;

            _tileHighlighter.ClearRange();
            _tileHighlighter.ClearMultiSelect();
            _tileHighlighter.ClearShape(); // clears the last hover-anchor preview, if any

            OnTargetingCancelled?.Invoke();
            return;
        }

        if (CurrentState != SelectionState.Targeting) return;

        SelectedAbility = null;
        CurrentState    = SelectionState.FighterSelected;

        _tileHighlighter.ClearRange();
        _tileHighlighter.ClearShape();

        OnTargetingCancelled?.Invoke();
    }

    // ── Multi-select targeting ──────────────────────────────────────────────

    private void EnterMultiTargeting(bool isSecondary)
    {
        _multiSelectIsSecondary = isSecondary;
        _multiSelectPicks.Clear();
        CurrentState = SelectionState.MultiTargeting;

        var ability = isSecondary ? _secondaryAbility : SelectedAbility;
        var effect  = isSecondary ? ability?.SecondaryEffect : ability?.PrimaryEffect;
        if (effect == null) return;

        _tileHighlighter.ClearShape();
        _tileHighlighter.ClearMultiSelect();
        var validTargets = AbilityTargeting.GetValidTargetTiles(SelectedFighter, effect, _board);
        _tileHighlighter.ShowRange(validTargets);

        Debug.Log($"[SelectionManager] Multi-select targeting ({(isSecondary ? "secondary" : "primary")}) " +
                  $"for {ability.Name} — up to {effect.MaxTargets} target(s)");
    }

    private void TryPickMultiTarget(Vector2Int pos)
    {
        var effect = CurrentTargetingEffect;
        if (effect == null) return;

        if (_multiSelectPicks.Contains(pos)) return; // already picked — right-click removes instead

        var validTargets = AbilityTargeting.GetValidTargetTiles(SelectedFighter, effect, _board);
        if (!validTargets.Contains(pos)) return;

        var tile   = _board.GetTile(pos);
        var target = tile?.OccupyingCharacter?.GetComponent<Fighter>();
        if (target == null || target.IsDead) return;
        if (!AbilityResolver.IsValidTarget(SelectedFighter, target, effect.TargetType)) return;

        _multiSelectPicks.Add(pos);
        _tileHighlighter.ShowMultiSelect(_multiSelectPicks);

        Debug.Log($"[SelectionManager] Multi-select pick: {target.FighterName} ({_multiSelectPicks.Count}/{effect.MaxTargets})");

        if (_multiSelectPicks.Count >= effect.MaxTargets)
            ConfirmMultiSelect();
    }

    // Fires the ability (primary phase) or the deferred secondary effect (secondary phase)
    // against every tile picked so far. Mirrors TryConfirmTarget/TryConfirmSecondaryTarget's
    // tail ends exactly — the only real difference is where shapeTiles comes from (the pick
    // list here, instead of one anchor's derived shape).
    private void ConfirmMultiSelect()
    {
        if (_multiSelectPicks.Count == 0) return;

        var fighter = SelectedFighter;
        var picks   = new List<Vector2Int>(_multiSelectPicks);
        var anchor  = picks[0]; // stand-in for logging/event purposes — multi-select has no single anchor

        if (_multiSelectIsSecondary)
        {
            var ability = _secondaryAbility;

            CurrentState            = SelectionState.FighterSelected;
            _secondaryAbility       = null;
            _multiSelectPicks.Clear();
            _tileHighlighter.ClearRange();
            _tileHighlighter.ClearMultiSelect();
            _tileHighlighter.ClearShape(); // clears the last hover-anchor preview, if any

            BattleController.Instance.RequestUseSecondaryEffect(fighter, ability, anchor, picks);

            if (SelectedFighter != null)
                OnMovePointsChanged?.Invoke(SelectedFighter.RemainingMovePoints, SelectedFighter.Speed);
        }
        else
        {
            var ability = SelectedAbility;

            SelectedAbility = null;
            CurrentState    = SelectionState.FighterSelected;
            _multiSelectPicks.Clear();
            _tileHighlighter.ClearRange();
            _tileHighlighter.ClearMultiSelect();
            _tileHighlighter.ClearShape(); // clears the last hover-anchor preview, if any

            OnAbilityConfirmed?.Invoke(fighter, ability, anchor, picks);

            // May synchronously fire OnFighterTurnEnded → Deselect().
            BattleController.Instance.RequestUseAbility(fighter, ability, anchor, picks);

            // Reposition-after-hit (RepositionRange > 0) isn't supported paired with multi-select
            // — no ability combines them, and "which of N picked targets gets repositioned" has
            // no single answer — so unlike TryConfirmTarget, there's no reposition check here.

            if (ability.SecondaryEffect != null)
            {
                EnterSecondaryTargeting(ability);
                return;
            }

            if (SelectedFighter != null)
                OnMovePointsChanged?.Invoke(SelectedFighter.RemainingMovePoints, SelectedFighter.Speed);
        }
    }

    // ── Targeting ──────────────────────────────────────────────────────────

    private void UpdateShapePreview(Vector2Int hoveredPos, AbilityEffect effect)
    {
        var validTargets = AbilityTargeting.GetValidTargetTiles(SelectedFighter, effect, _board);

        if (!validTargets.Contains(hoveredPos))
        {
            _tileHighlighter.ClearShape();
            _currentShapePreview.Clear();
            return;
        }

        var shapeTiles = AbilityTargeting.GetShapeTiles(SelectedFighter, effect, hoveredPos, _board, _areaBiasLeft, _facingOverride);
        _tileHighlighter.ShowShape(shapeTiles, hoveredPos);
        _currentShapePreview = shapeTiles;
    }

    private void TryConfirmTarget(Vector2Int pos)
    {
        var validTargets = AbilityTargeting.GetValidTargetTiles(SelectedFighter, SelectedAbility.PrimaryEffect, _board);
        if (!validTargets.Contains(pos))
        {
            Debug.Log("[SelectionManager] Clicked tile is not a valid target");
            return;
        }

        var shapeTiles = AbilityTargeting.GetShapeTiles(SelectedFighter, SelectedAbility.PrimaryEffect, pos, _board, _areaBiasLeft, _facingOverride);

        Debug.Log($"[SelectionManager] Ability confirmed: {SelectedAbility.Name} on anchor {pos}, affecting {shapeTiles.Count} tile(s)");

        var fighter = SelectedFighter;
        var ability = SelectedAbility;

        // Capture reposition target before the ability fires (target may die from the hit)
        Fighter repositionTarget = null;
        if (ability.RepositionRange > 0)
        {
            var targetTile = _board.GetTile(pos);
            repositionTarget = targetTile?.OccupyingCharacter?.GetComponent<Fighter>();
        }

        // Reset targeting state first
        SelectedAbility = null;
        CurrentState    = SelectionState.FighterSelected;
        _tileHighlighter.ClearRange();
        _tileHighlighter.ClearShape();

        OnAbilityConfirmed?.Invoke(fighter, ability, pos, shapeTiles);

        // May synchronously fire OnFighterTurnEnded → Deselect() if no reposition pending
        BattleController.Instance.RequestUseAbility(fighter, ability, pos, shapeTiles);

        // If hit target survived, enter the reposition phase for second click
        if (ability.RepositionRange > 0 && repositionTarget != null && !repositionTarget.IsDead)
        {
            EnterRepositionTargeting(repositionTarget, ability.RepositionRange);
            return;
        }

        // If this ability has its own distinct follow-up target (e.g. Vemk Parlas's Sig), enter
        // that phase instead of ending here.
        if (ability.SecondaryEffect != null)
        {
            EnterSecondaryTargeting(ability);
            return;
        }

        if (SelectedFighter != null)
            OnMovePointsChanged?.Invoke(SelectedFighter.RemainingMovePoints, SelectedFighter.Speed);
    }

    // ── Secondary targeting ────────────────────────────────────────────────

    private void EnterSecondaryTargeting(Ability ability)
    {
        _secondaryAbility = ability;

        var effect = ability.SecondaryEffect;
        if (effect != null && effect.MaxTargets > 1 && effect.Shape == AbilityShape.Single)
        {
            EnterMultiTargeting(isSecondary: true);
            return;
        }

        CurrentState = SelectionState.SecondaryTargeting;

        var validTargets = AbilityTargeting.GetValidTargetTiles(SelectedFighter, effect, _board);
        _tileHighlighter.ShowRange(validTargets);

        Debug.Log($"[SelectionManager] Secondary targeting phase for {ability.Name} — {validTargets.Count} valid tile(s)");
    }

    private void TryConfirmSecondaryTarget(Vector2Int pos)
    {
        var effect = _secondaryAbility.SecondaryEffect;
        var validTargets = AbilityTargeting.GetValidTargetTiles(SelectedFighter, effect, _board);
        if (!validTargets.Contains(pos))
        {
            Debug.Log("[SelectionManager] Secondary target: clicked tile is not valid");
            return;
        }

        var shapeTiles = AbilityTargeting.GetShapeTiles(SelectedFighter, effect, pos, _board, _areaBiasLeft, _facingOverride);

        var fighter = SelectedFighter;
        var ability = _secondaryAbility;

        CurrentState      = SelectionState.FighterSelected;
        _secondaryAbility = null;
        _tileHighlighter.ClearRange();
        _tileHighlighter.ClearShape();

        BattleController.Instance.RequestUseSecondaryEffect(fighter, ability, pos, shapeTiles);

        if (SelectedFighter != null)
            OnMovePointsChanged?.Invoke(SelectedFighter.RemainingMovePoints, SelectedFighter.Speed);
    }

    // ── Reposition targeting ───────────────────────────────────────────────

    private void EnterRepositionTargeting(Fighter target, int range)
    {
        _repositionTarget = target;
        _repositionRange  = range;
        CurrentState      = SelectionState.RepositionTargeting;

        _repositionTiles = GetRepositionTiles(target.GridPosition, range);
        _tileHighlighter.ShowMoveRange(_repositionTiles);

        Debug.Log($"[SelectionManager] Reposition phase: place {target.FighterName} — {_repositionTiles.Count} valid tiles");
    }

    private void TryConfirmReposition(Vector2Int pos)
    {
        if (!_repositionTiles.Contains(pos))
        {
            Debug.Log("[SelectionManager] Reposition: clicked tile is not valid");
            return;
        }

        var fighter = SelectedFighter;
        var target  = _repositionTarget;

        CurrentState = SelectionState.FighterSelected;
        ClearRepositionState();

        BattleController.Instance.RequestReposition(target, pos);

        if (SelectedFighter != null)
            OnMovePointsChanged?.Invoke(SelectedFighter.RemainingMovePoints, SelectedFighter.Speed);
    }

    private void ClearRepositionState()
    {
        _repositionTarget = null;
        _repositionRange  = 0;
        _repositionTiles.Clear();
        _tileHighlighter.ClearMoveRange();
        _tileHighlighter.ClearMoveHover();
    }

    private HashSet<Vector2Int> GetRepositionTiles(Vector2Int center, int range)
    {
        var tiles = new HashSet<Vector2Int>();
        for (int dx = -range; dx <= range; dx++)
        for (int dy = -range; dy <= range; dy++)
        {
            if (Mathf.Abs(dx) + Mathf.Abs(dy) > range) continue; // Manhattan diamond
            var pos  = center + new Vector2Int(dx, dy);
            var tile = _board.GetTile(pos);
            if (tile != null && tile.IsPassable && !tile.IsOccupied)
                tiles.Add(pos);
        }
        return tiles;
    }
}
