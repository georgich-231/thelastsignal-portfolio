using UnityEngine;
using UnityEngine.UI;

// Tutorial card ("field guide"): a frost accent rail, tracked eyebrow, headline, the
// key and action, then a short description. Built into a 342-wide glass panel.
public static class SurvivalGuideCard
{
    public static Text Build(Transform panel, string category, string title, string key, string action, string body)
    {
        SurvivalUITheme.Stroke(panel, new Color(.62f, .80f, .90f, .10f));
        var rail = SurvivalUITheme.Surface(panel, "Accent rail", 0, 18, 2, 44, true); rail.sprite = SurvivalUITheme.PillSprite; rail.color = SurvivalUITheme.Frost;
        SurvivalUITheme.Diamond(panel, 20, 19, 8, SurvivalUITheme.Frost, false);
        SurvivalUITheme.Text(panel, "FIELD GUIDE  ·  " + category, 36, 16, 290, 14, 9, false, SurvivalUITheme.Frost);
        var heading = SurvivalUITheme.Text(panel, title, 20, 34, 304, 34, 27, true, SurvivalUITheme.Snow);
        SurvivalUITheme.Line(panel, 20, 74, 302, new Color(.62f, .80f, .90f, .22f));
        var keycap = SurvivalUITheme.Key(panel, key, 20, 88, 30);
        float kx = 20 + ((RectTransform)keycap.transform.parent).sizeDelta.x + 12;
        var label = SurvivalUITheme.Text(panel, action, kx, 94, 322 - kx, 18, 12, true, SurvivalUITheme.Snow);
        var description = SurvivalUITheme.Text(panel, body, 20, 128, 304, 40, 12, false, SurvivalUITheme.Mist);
        description.lineSpacing = 1.1f;
        return description;
    }
}
