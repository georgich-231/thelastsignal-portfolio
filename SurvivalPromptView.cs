using UnityEngine;
using UnityEngine.UI;

// Interaction prompt: a compact glass pill with a key cap, a tracked category, the
// action as a headline and a detail line; a hold-progress line runs along its base.
// World-anchored prompts hang from a thin tether above the object they refer to.
public sealed class SurvivalPromptView : MonoBehaviour
{
    public CanvasGroup Group { get; private set; }
    public Text Title { get; private set; }
    public Text Detail { get; private set; }
    public Text Category { get; private set; }
    public Text KeyLabel { get; private set; }
    public Image Progress { get; private set; }
    public int FeedbackCount { get; private set; }
    RectTransform rect, keyRect, tether;
    Image panel, feedbackBorder, tetherDot, tetherLine, accent;
    Vector2 rest;
    float feedbackAt = -10, shownAt = -10, lastAlpha;
    Vector3 worldAnchor;
    bool anchored;
    Camera worldCamera;
    string lastTitle, lastDetail, lastCategory;
    Canvas canvas;
    const float Pad = 14, KeyW = 34, Gap = 12;

    public void SetWorldAnchor(Vector3 point) { worldAnchor = point; anchored = true; }
    public bool IsWorldAnchored => anchored;

    public static SurvivalPromptView Create(Transform parent, string name)
    {
        var p = SurvivalUITheme.Surface(parent, name, 0, 0, 240, 62);
        var r = p.rectTransform; r.anchorMin = r.anchorMax = new Vector2(.5f, .5f); r.pivot = new Vector2(.5f, 0); r.anchoredPosition = new Vector2(0, -170);
        var view = p.gameObject.AddComponent<SurvivalPromptView>(); view.rect = r; view.rest = r.anchoredPosition; view.panel = p;
        view.canvas = parent.GetComponentInParent<Canvas>();
        view.Group = p.gameObject.AddComponent<CanvasGroup>(); view.Group.alpha = 0; view.Group.blocksRaycasts = false;

        // Frost accent along the left edge of the pill.
        view.accent = SurvivalUITheme.Surface(p.transform, "Accent", 0, 12, 2, 38, true); view.accent.sprite = SurvivalUITheme.PillSprite; view.accent.color = new Color(.62f, .84f, .95f, .85f);
        view.feedbackBorder = SurvivalUITheme.Stroke(p.transform, Color.clear);
        view.KeyLabel = SurvivalUITheme.Key(p.transform, "E", Pad, 14, KeyW);
        view.keyRect = (RectTransform)view.KeyLabel.transform.parent;
        float tx = Pad + KeyW + Gap;
        view.Category = SurvivalUITheme.Text(p.transform, "INTERACTION", tx, 10, 170, 12, 9, false, SurvivalUITheme.Frost);
        view.Title = SurvivalUITheme.Text(p.transform, "", tx, 22, 170, 24, 20, true);
        view.Detail = SurvivalUITheme.Text(p.transform, "", tx, 46, 170, 16, 11, false, SurvivalUITheme.Mist);
        view.Progress = SurvivalUITheme.Surface(p.transform, "Hold progress", 10, 60, 220, 2, true);
        view.Progress.sprite = SurvivalUITheme.PillSprite; view.Progress.type = Image.Type.Filled; view.Progress.fillMethod = Image.FillMethod.Horizontal; view.Progress.fillAmount = 0; view.Progress.color = SurvivalUITheme.Frost;

        // Tether (only when anchored to a world object).
        var t = new GameObject("Tether", typeof(RectTransform)); t.transform.SetParent(p.transform, false);
        view.tether = t.GetComponent<RectTransform>(); view.tether.anchorMin = view.tether.anchorMax = new Vector2(.5f, 0); view.tether.pivot = new Vector2(.5f, 1); view.tether.sizeDelta = new Vector2(10, 22); view.tether.anchoredPosition = Vector2.zero;
        var line = new GameObject("Line", typeof(RectTransform), typeof(Image)); line.transform.SetParent(t.transform, false);
        var lr = line.GetComponent<RectTransform>(); lr.anchorMin = new Vector2(.5f, 0); lr.anchorMax = new Vector2(.5f, 1); lr.sizeDelta = new Vector2(1, -6); lr.anchoredPosition = new Vector2(0, 3);
        view.tetherLine = line.GetComponent<Image>(); view.tetherLine.color = new Color(.62f, .84f, .95f, .55f); view.tetherLine.raycastTarget = false;
        var dot = SurvivalUITheme.Diamond(t.transform, 0, 0, 7, SurvivalUITheme.Frost);
        dot.anchorMin = dot.anchorMax = dot.pivot = new Vector2(.5f, 0); dot.anchoredPosition = Vector2.zero;
        view.tetherDot = dot.GetComponentInChildren<Image>();
        t.SetActive(false);
        return view;
    }

    public void Denied()
    {
        FeedbackCount++; feedbackAt = Time.unscaledTime;
        Detail.color = SurvivalUITheme.Ember;
        Group.alpha = 1;
    }

    void LateUpdate()
    {
        if (lastTitle != Title.text || lastDetail != Detail.text || lastCategory != Category.text)
        {
            lastTitle = Title.text; lastDetail = Detail.text; lastCategory = Category.text;
            float tx = Pad + KeyW + Gap;
            float textWidth = Mathf.Clamp(Mathf.Max(Title.preferredWidth, Category.preferredWidth + 20, Detail.preferredWidth) + 4, 150, 300);
            foreach (var label in new[] { Title, Category, Detail }) label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth);
            bool hasDetail = !string.IsNullOrEmpty(Detail.text);
            float detailHeight = hasDetail ? Mathf.Max(15, Detail.preferredHeight) : 0;
            Detail.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(1, detailHeight));
            float height = hasDetail ? 50 + detailHeight + 10 : 56;
            float width = tx + textWidth + Pad + 4;
            rect.sizeDelta = new Vector2(width, height);
            keyRect.anchoredPosition = new Vector2(Pad, -(height - KeyW) * .5f + (hasDetail ? 0 : 0));
            ((RectTransform)accent.transform).sizeDelta = new Vector2(2, height - 22);
            ((RectTransform)accent.transform).anchoredPosition = new Vector2(0, -11);
            Progress.rectTransform.sizeDelta = new Vector2(width - 20, 2);
            Progress.rectTransform.anchoredPosition = new Vector2(10, -height + 3);
        }
        if (anchored)
        {
            if (worldCamera == null) worldCamera = Camera.main;
            if (worldCamera != null)
            {
                Vector3 screen = worldCamera.WorldToScreenPoint(worldAnchor);
                if (screen.z <= 0) { Group.alpha = 0; return; }
                float factor = canvas.scaleFactor;
                float half = rect.rect.width * factor * .5f;
                screen.x = Mathf.Clamp(screen.x, half + 12, Screen.width - half - 12);
                screen.y = Mathf.Clamp(screen.y + 30 * factor, 12, Screen.height - rect.rect.height * factor - 12);
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)rect.parent, screen, null, out rest);
            }
            if (!tether.gameObject.activeSelf) tether.gameObject.SetActive(true);
        }
        // Arrival: rise and settle whenever the prompt fades in.
        if (Group.alpha > .02f && lastAlpha <= .02f) shownAt = Time.unscaledTime;
        lastAlpha = Group.alpha;
        float rise = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((Time.unscaledTime - shownAt) / .3f));

        float age = Time.unscaledTime - feedbackAt;
        float pulse = Mathf.Clamp01(1 - age / .4f);
        rect.anchoredPosition = rest + new Vector2(Mathf.Sin(age * 60) * 4f * pulse, -8 * rise);
        feedbackBorder.color = new Color(1f, .67f, .40f, .8f) * pulse;
        accent.color = Color.Lerp(new Color(.62f, .84f, .95f, .85f), new Color(1f, .67f, .40f, 1f), pulse);
        KeyLabel.color = Color.Lerp(SurvivalUITheme.Snow, SurvivalUITheme.Ember, pulse);
    }
}
