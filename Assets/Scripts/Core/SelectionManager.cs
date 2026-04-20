using System;
using System.Collections.Generic;
using UnityEngine;

// [CLIENT] SelectionManager owns input routing and selection state only.
// It never mutates Fighter or Board state directly — all game state changes
// go through BattleController request methods.
public enum SelectionState
{
    Idle,
    FighterSelected,
    Targeting,
    RepositionTargeting  // second click required to place a hit fighter somewhere
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
    public static event Action<Fighter> OnFighterSelected;
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
    private Board            _board;
    private TileHighlighter  _tileHighlighter;
    private Pathfinder       _pathfinder;

    // Movement range tiles for current move-mode session
    private HashSet<Vector2Int> _moveRangeTiles = new HashSet<Vector2Int>();

    // Area box bias — toggled with B key during targeting (even-width boxes only)
    // TODO: add a visible UI indicator so players know B toggles box bias
    private bool _areaBiasLeft = true;

    // Cache the last hovered position so B-key toggling refreshes the preview in place
    private Vector2Int       _lastHoveredPos      = new Vector2Int(-1, -1);
    private List<Vector2Int> _currentShapePreview = new List<Vector2Int>();

    // Reposition targeting state
    private Fighter              _repositionTarget;
    private int                  _repositionRange;
    private HashSet<Vector2Int>  _repositionTiles = new HashSet<Vector2Int>();

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

        InputHandler.OnTileClicked     += HandleTileClicked;
        InputHandler.OnTileHovered     += HandleTileHovered;
        TurnManager.OnFighterTurnEnded += HandleFighterTurnEnded;

        Debug.Log("[SelectionManager] Initialized");
    }

    private void OnDestroy()
    {
        InputHandler.OnTileClicked     -= HandleTileClicked;
        InputHandler.OnTileHovered     -= HandleTileHovered;
        TurnManager.OnFighterTurnEnded -= HandleFighterTurnEnded;
    }

    // ── Input routing ──────────────────────────────────────────────────────

    private void HandleTileClicked(Vector2Int pos)
    {
        switch (CurrentState)
        {
            case SelectionState.Idle:
            {
                var idleTile = _board.GetTile(pos);
                if (idleTile?.OccupyingCharacter != null)
                    TrySelectFighter(pos);
                else if (idleTile != null)
                    OnTileSelected?.Invoke(pos);
                break;
            }

            case SelectionState.FighterSelected:
                var tile = _board.GetTile(pos);
                bool isOccupied = tile?.OccupyingCharacter != null;

                if (InMoveMode && _moveRangeTiles.Contains(pos) && !isOccupied)
                {
                    // Valid move destination
                    TryMove(pos);
                }
                else if (isOccupied)
                {
                    // Clicking a fighter tile — try to switch or cancel selection
                    TrySelectFighter(pos);
                }
                else if (!InMoveMode && tile != null)
                {
                    // Empty tile click outside move mode — show tile info
                    OnTileSelected?.Invoke(pos);
                }
                break;

            case SelectionState.Targeting:
                TryConfirmTarget(pos);
                break;

            case SelectionState.RepositionTargeting:
                TryConfirmReposition(pos);
                break;
        }
    }

    private void Update()
    {
        if (CurrentState == SelectionState.Targeting
            && SelectedAbility?.Shape == AbilityShape.Box
            && Input.GetKeyDown(KeyCode.B))
        {
            _areaBiasLeft = !_areaBiasLeft;
            if (_lastHoveredPos.x >= 0)
                UpdateShapePreview(_lastHoveredPos);
        }
    }

    private void HandleTileHovered(Vector2Int pos)
    {
        if (CurrentState == SelectionState.Targeting)
        {
            _lastHoveredPos = pos;
            UpdateShapePreview(pos);
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

    private void TrySelectFighter(Vector2Int pos)
    {
        var tile = _board.GetTile(pos);
        if (tile == null || tile.OccupyingCharacter == null) return; // ignore empty tile clicks

        var fighter = tile.OccupyingCharacter.GetComponent<Fighter>();
        if (fighter == null || fighter.IsDead) return;

        // Clicking the already-selected fighter — do nothing
        if (fighter == SelectedFighter) return;

        // Allow re-selecting the currently active fighter (handles accidental deselect)
        if (fighter == TurnManager.Instance.ActiveFighter)
        {
            SelectedFighter = fighter;
            SelectedAbility = null;
            CurrentState    = SelectionState.FighterSelected;
            ExitMoveMode();
            OnFighterSelected?.Invoke(fighter);
            return;
        }

        // Only eligible fighters on the active team can be newly activated
        if (!TurnManager.Instance.CanActivate(fighter)) return;

        SelectedFighter = fighter;
        SelectedAbility = null;
        CurrentState    = SelectionState.FighterSelected;

        ExitMoveMode();
        _tileHighlighter.ClearRange();
        _tileHighlighter.ClearShape();

        TurnManager.Instance.ActivateFighter(fighter);

        // DoT may have killed the fighter during activation — don't show a dead fighter's panel
        if (fighter.IsDead) return;

        Debug.Log($"[SelectionManager] Selected fighter: {fighter.FighterName}");
        OnTileDeselected?.Invoke();
        OnFighterSelected?.Invoke(fighter);
    }

    public void Deselect()
    {
        SelectedFighter = null;
        SelectedAbility = null;
        CurrentState    = SelectionState.Idle;
        _areaBiasLeft   = true;
        _lastHoveredPos = new Vector2Int(-1, -1);

        ExitMoveMode();
        ClearRepositionState();
        _tileHighlighter.ClearRange();
        _tileHighlighter.ClearShape();

        OnFighterDeselected?.Invoke();
    }

    // ── Movement (called by UI Move button) ────────────────────────────────

    /// Called by the Move button. Toggles move mode on/off.
    public void EnterMoveMode()
    {
        if (CurrentState != SelectionState.FighterSelected) return;
        if (SelectedFighter == null || SelectedFighter.RemainingMovePoints <= 0f) return;

        if (InMoveMode) { ExitMoveMode(); return; } // toggle off

        InMoveMode = true;
        var reachable = _pathfinder.GetReachableTiles(SelectedFighter.GridPosition, SelectedFighter.RemainingMovePoints);
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

    private void TryMove(Vector2Int destination)
    {
        float prevRemaining = SelectedFighter.RemainingMovePoints;
        BattleController.Instance.RequestMove(SelectedFighter, destination);

        if (SelectedFighter.RemainingMovePoints == prevRemaining)
            return; // move failed — no points were spent, position unchanged

        // Broadcast updated move points for the UI text
        OnMovePointsChanged?.Invoke(SelectedFighter.RemainingMovePoints, SelectedFighter.Speed);

        if (SelectedFighter.RemainingMovePoints > 0f)
        {
            // Refresh range from new position with remaining points
            var reachable = _pathfinder.GetReachableTiles(SelectedFighter.GridPosition, SelectedFighter.RemainingMovePoints);
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
        if (CurrentState != SelectionState.FighterSelected && CurrentState != SelectionState.Targeting)
            return;

        ExitMoveMode(); // leave move mode when switching to ability targeting

        SelectedAbility = ability;
        CurrentState    = SelectionState.Targeting;

        _tileHighlighter.ClearShape();
        var validTargets = AbilityTargeting.GetValidTargetTiles(SelectedFighter, ability, _board);
        _tileHighlighter.ShowRange(validTargets);

        Debug.Log($"[SelectionManager] Targeting with ability: {ability.Name}");
        OnAbilitySelected?.Invoke(ability);
    }

    public void CancelTargeting()
    {
        if (CurrentState != SelectionState.Targeting) return;

        SelectedAbility = null;
        CurrentState    = SelectionState.FighterSelected;

        _tileHighlighter.ClearRange();
        _tileHighlighter.ClearShape();

        OnTargetingCancelled?.Invoke();
    }

    // ── Targeting ──────────────────────────────────────────────────────────

    private void UpdateShapePreview(Vector2Int hoveredPos)
    {
        var validTargets = AbilityTargeting.GetValidTargetTiles(SelectedFighter, SelectedAbility, _board);

        if (!validTargets.Contains(hoveredPos))
        {
            _tileHighlighter.ClearShape();
            _currentShapePreview.Clear();
            return;
        }

        var shapeTiles = AbilityTargeting.GetShapeTiles(SelectedFighter, SelectedAbility, hoveredPos, _board, _areaBiasLeft);
        _tileHighlighter.ShowShape(shapeTiles, hoveredPos);
        _currentShapePreview = shapeTiles;
    }

    private void TryConfirmTarget(Vector2Int pos)
    {
        var validTargets = AbilityTargeting.GetValidTargetTiles(SelectedFighter, SelectedAbility, _board);
        if (!validTargets.Contains(pos))
        {
            Debug.Log("[SelectionManager] Clicked tile is not a valid target");
            return;
        }

        var shapeTiles = AbilityTargeting.GetShapeTiles(SelectedFighter, SelectedAbility, pos, _board, _areaBiasLeft);

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
