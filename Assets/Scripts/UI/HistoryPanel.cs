using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HistoryPanel : MonoBehaviour
{
    [SerializeField] private Transform    content;
    [SerializeField] private ScrollRect   scrollRect;
    [SerializeField] private LogEntryUI   entryPrefab;
    [SerializeField] private int          maxEntries = 200;

    [Header("Category Colors")]
    [SerializeField] private Color colorAbility  = new Color(0.9f, 0.9f, 0.9f);
    [SerializeField] private Color colorHit      = new Color(1f,   0.6f, 0.3f);
    [SerializeField] private Color colorMiss     = new Color(0.5f, 0.5f, 0.5f);
    [SerializeField] private Color colorEffect   = new Color(0.7f, 0.5f, 1f);
    [SerializeField] private Color colorMovement = new Color(0.4f, 0.9f, 1f);
    [SerializeField] private Color colorPassive  = new Color(1f,   0.9f, 0.3f);
    [SerializeField] private Color colorDeath    = new Color(0.8f, 0.2f, 0.2f);

    private readonly List<LogEntryUI> _entries = new();

    private void Awake()
    {
        BattleLogger.OnEntry += AddEntry;
    }

    private void OnDestroy()
    {
        BattleLogger.OnEntry -= AddEntry;
    }

    private void AddEntry(LogEntry entry)
    {
        // Trim oldest entries if over cap
        if (_entries.Count >= maxEntries)
        {
            Destroy(_entries[0].gameObject);
            _entries.RemoveAt(0);
        }

        var ui = Instantiate(entryPrefab, content);
        ui.Setup(entry, ColorFor(entry.Category));
        _entries.Add(ui);

        // Auto-scroll to bottom next frame (layout must settle first)
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
    }

    private Color ColorFor(LogCategory category) => category switch
    {
        LogCategory.Ability  => colorAbility,
        LogCategory.Hit      => colorHit,
        LogCategory.Miss     => colorMiss,
        LogCategory.Effect   => colorEffect,
        LogCategory.Movement => colorMovement,
        LogCategory.Passive  => colorPassive,
        LogCategory.Death    => colorDeath,
        _                    => Color.white
    };
}
