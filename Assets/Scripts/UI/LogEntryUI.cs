using TMPro;
using UnityEngine;

public class LogEntryUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI label;

    public LogCategory Category { get; private set; }

    public void Setup(LogEntry entry, Color color)
    {
        Category   = entry.Category;
        label.text  = entry.Message;
        label.color = color;
    }
}
