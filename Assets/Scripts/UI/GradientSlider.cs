using UnityEngine;
using UnityEngine.UI;

// Attach to any Slider GameObject.
// Lerps the fill image color from colorEmpty (value=0) to colorFull (value=max).
// Drag the Fill image from the slider's Fill Area into the fillImage field.
[RequireComponent(typeof(Slider))]
public class GradientSlider : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    [SerializeField] private Color colorFull  = Color.green;
    [SerializeField] private Color colorEmpty = Color.red;

    private Slider _slider;

    private void Awake()
    {
        _slider = GetComponent<Slider>();
        _slider.onValueChanged.AddListener(_ => UpdateColor());
    }

    private void Start()
    {
        UpdateColor();
    }

    private void OnDestroy()
    {
        _slider.onValueChanged.RemoveListener(_ => UpdateColor());
    }

    public void UpdateColor()
    {
        if (!enabled || fillImage == null || _slider.maxValue <= 0f) return;

        float t = Mathf.Clamp01(_slider.value / _slider.maxValue);
        fillImage.color = Color.Lerp(colorEmpty, colorFull, t);
    }
}
