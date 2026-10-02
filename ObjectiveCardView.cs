using UnityEngine;
using UnityEngine.UI;

// Current objective, top left. No box: a feathered scrim keeps it legible over snow,
// a glowing diamond marks it, and a new objective slides in with a light sweep.
public sealed class ObjectiveCardView : MonoBehaviour
{
    Text title, eyebrow;
    RectTransform scrim, diamond, rule, sweep;
    Image sweepImage;
    string previous;
    float changedAt = -10;

    public static Text Build(Transform parent)
    {
        var view = parent.gameObject.AddComponent<ObjectiveCardView>();
        view.Create(); return view.title;
    }

    void Create()
    {
        var s = new GameObject("Legibility scrim", typeof(RectTransform), typeof(Image)); s.transform.SetParent(transform, false);
        scrim = s.GetComponent<RectTransform>(); SurvivalUITheme.Place(scrim, -40, -26, 520, 130);
        var si = s.GetComponent<Image>(); si.sprite = SurvivalUITheme.ScrimSprite; si.color = new Color(1, 1, 1, .88f); si.raycastTarget = false;

        diamond = SurvivalUITheme.Diamond(transform, 0, 7, 11, SurvivalUITheme.Frost);
        eyebrow = SurvivalUITheme.Text(transform, "OBJECTIVE", 22, 3, 200, 16, 10, false, SurvivalUITheme.Frost);
        eyebrow.gameObject.name = "Objective eyebrow";
        rule = SurvivalUITheme.Line(transform, 22, 22, 120, new Color(.62f, .84f, .95f, .35f)).rectTransform;

        title = SurvivalUITheme.Text(transform, "", 22, 29, 330, 60, 25, true, SurvivalUITheme.Snow);
        title.gameObject.name = "Objective text"; title.lineSpacing = 1.02f;
        SurvivalUITheme.Soften(title, .6f, 1.5f);

        // A soft band of light that passes across a newly set objective.
        var sw = new GameObject("Objective sweep", typeof(RectTransform), typeof(Image)); sw.transform.SetParent(transform, false);
        sweep = sw.GetComponent<RectTransform>(); SurvivalUITheme.Place(sweep, 0, 20, 90, 60);
        sweepImage = sw.GetComponent<Image>(); sweepImage.sprite = SurvivalUITheme.GlowSprite; sweepImage.color = Color.clear; sweepImage.raycastTarget = false;
    }

    void LateUpdate()
    {
        if (!title) return;
        if (previous != title.text)
        {
            previous = title.text; changedAt = Time.unscaledTime;
            float h = Mathf.Max(30, title.preferredHeight);
            title.rectTransform.sizeDelta = new Vector2(330, h);
            scrim.sizeDelta = new Vector2(520, 70 + h);
        }
        // Arrival: slide in from the left and fade up; the rule draws out; a sweep passes.
        float age = Time.unscaledTime - changedAt;
        float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / .55f));
        title.rectTransform.anchoredPosition = new Vector2(22 - 14 * (1 - t), -29);
        var c = title.color; c.a = t; title.color = c;
        rule.sizeDelta = new Vector2(Mathf.Lerp(0, 120, Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / .8f))), 1);
        float pulse = Mathf.Exp(-age * 2.2f);
        diamond.localScale = Vector3.one * (1 + .5f * pulse);
        float s = Mathf.Clamp01(age / 1.1f);
        sweep.anchoredPosition = new Vector2(Mathf.Lerp(-40, 360, s), -24);
        sweepImage.color = new Color(.62f, .84f, .95f, .16f * Mathf.Sin(s * Mathf.PI) * (age < 1.2f ? 1 : 0));
    }
}
