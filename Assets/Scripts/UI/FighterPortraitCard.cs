using UnityEngine;
using UnityEngine.UI;

// Attached to the FighterPortraitCard prefab.
// Turn state: active = full opacity + border, waiting = full opacity, done = 40% opacity, dead = dim red tint.
public class FighterPortraitCard : MonoBehaviour
{
    [SerializeField] private Image  portraitImage;
    [SerializeField] private Image  borderImage;    // outline/frame sprite, toggled on for active fighter
    [SerializeField] private Slider hpOverlay;      // vertical slider, no handle, overlays portrait

    private static readonly Color ColorFull = Color.white;                          // waiting / active
    private static readonly Color ColorDone = new Color(1f, 1f, 1f, 0.6f);         // acted this round
    private static readonly Color ColorDead = new Color(0.55f, 0.15f, 0.15f, 1f);  // dead

    public void Setup(Fighter fighter)
    {
        if (portraitImage != null)
        {
            portraitImage.sprite         = fighter.Portrait;
            portraitImage.preserveAspect = true;
        }

        if (hpOverlay != null)
        {
            hpOverlay.minValue = 0f;
            hpOverlay.maxValue = fighter.MaxHP;
            hpOverlay.value    = fighter.CurrentHP;
            RefreshHPColor();
        }

        if (borderImage != null)
            borderImage.enabled = false;
    }

    public void UpdateHP(float current)
    {
        if (hpOverlay == null) return;
        hpOverlay.value = current;
        RefreshHPColor();
    }

    public void UpdateState(Fighter fighter, bool isActive)
    {
        if (portraitImage == null) return;

        if (fighter.IsDead)
        {
            portraitImage.color         = ColorDead;
            if (borderImage != null) borderImage.enabled = false;
        }
        else if (isActive)
        {
            portraitImage.color         = ColorFull;
            if (borderImage != null) borderImage.enabled = true;
        }
        else if (fighter.HasActivatedThisRound)
        {
            portraitImage.color         = ColorDone;
            if (borderImage != null) borderImage.enabled = false;
        }
        else
        {
            portraitImage.color         = ColorFull;
            if (borderImage != null) borderImage.enabled = false;
        }
    }

    private void RefreshHPColor()
    {
        if (hpOverlay == null || hpOverlay.fillRect == null) return;
        var fill = hpOverlay.fillRect.GetComponent<Image>();
        if (fill == null) return;

        float t     = hpOverlay.maxValue > 0f ? hpOverlay.value / hpOverlay.maxValue : 0f;
        float alpha = fill.color.a;
        fill.color = new Color(
            Mathf.Lerp(1f, 0f, t),
            Mathf.Lerp(0f, 0.8f, t),
            0f,
            alpha
        );
    }
}
