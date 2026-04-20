using System.Collections.Generic;
using UnityEngine;

public class FighterManager : MonoBehaviour
{
    public static FighterManager Instance { get; private set; }

    [SerializeField] private GameObject fighterPrefab;
    [SerializeField] private Transform boardRoot;
    private List<Fighter> _allFighters = new List<Fighter>();
    public IReadOnlyList<Fighter> AllFighters => _allFighters;
    private Board _board;
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void Initialize(Board board) //NEW
    {
        _board = board;
    }

    // Spawns a fighter from JSON data, applying global balance modifiers and loading their sprite.
    public Fighter SpawnFighterFromData(FighterData data, int teamId, Vector2Int gridPosition, BalanceSettings balance)
    {
        var tile = _board.GetTile(gridPosition);
        if (tile == null || tile.IsOccupied)
        {
            Debug.LogWarning($"[FighterManager] Cannot spawn {data.name} at {gridPosition} — tile unavailable");
            return null;
        }

        int   finalHP    = Mathf.RoundToInt(data.hp           * balance.hpMultiplier);
        float finalSpeed = data.speed                         + balance.speedAdd;
        int   finalSig   = Mathf.RoundToInt(data.sigChargeReq * balance.sigChargeMultiplier);

        // Stat defaults for fields absent/zero in JSON
        float dmgMult  = data.damageMultiplier > 0f ? data.damageMultiplier : 1f;
        float accuracy = data.accuracy         > 0f ? data.accuracy         : 1f;
        float dodge    = data.dodgeChance; // 0 is valid default
        float critRate = data.critRate     > 0f ? data.critRate             : 0.1f;
        float critDmg  = data.critDmg      > 0f ? data.critDmg              : 1.5f;

        float resArcane    = data.resistances?.arcane    ?? 0f;
        float resElemental = data.resistances?.elemental ?? 0f;
        float resForce     = data.resistances?.force     ?? 0f;
        float resCorrupt   = data.resistances?.corrupt   ?? 0f;

        var obj = Instantiate(fighterPrefab, Vector3.zero, Quaternion.identity, boardRoot);
        obj.name = $"Fighter_{data.name}_T{teamId}";

        var fighter = obj.GetComponent<Fighter>();
        fighter.Initialize(data.name, teamId, finalHP, finalSpeed, finalSig,
                           dmgMult, accuracy, dodge, critRate, critDmg,
                           resArcane, resElemental, resForce, resCorrupt,
                           gridPosition, _board);

        // Sprite — fall back to team colour if asset is missing
        var sprite = FighterLoader.LoadSprite(data.imageName);
        if (sprite != null)
            fighter.SetSprite(sprite);
        else
            fighter.SetColor(teamId == 1 ? Color.blue : Color.red);

        // Build abilities from move data; second Skill move gets Skill2 slot
        if (data.moves != null)
        {
            bool hasSkill = false;
            foreach (var move in data.moves)
            {
                var ability = FighterLoader.BuildAbility(move);
                if (ability == null) continue;
                if (ability.Slot == AbilitySlot.Skill)
                {
                    if (hasSkill)
                        ability.Slot = AbilitySlot.Skill2;
                    else
                        hasSkill = true;
                }
                fighter.AddAbility(ability);
            }
        }

        tile.OccupyingCharacter = obj;
        _allFighters.Add(fighter);
        return fighter;
    }

    public void RegisterOnBoard(Fighter fighter)
    {
        var tile = _board.GetTile(fighter.GridPosition);
        if (tile != null)
            tile.OccupyingCharacter = fighter.gameObject;
    }

    public void ClearTile(Vector2Int position)
    {
        var tile = _board.GetTile(position);
        if (tile != null)
            tile.OccupyingCharacter = null;
    }

    public List<Fighter> GetTeam(int teamId)
    {
        return _allFighters.FindAll(f => f.TeamId == teamId);
    }
}