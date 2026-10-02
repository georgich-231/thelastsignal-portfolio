using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Shared visual language for every runtime interface ("frostline").
//
// Deep blue-black frosted glass with a cold top light and a soft drop shadow; one
// cold accent (frost) and one warm accent (ember) for heat, flame and danger;
// condensed tracked capitals for labels, a clean sans for reading. Every surface,
// key cap and hairline in the game is drawn from these few generated sprites, so
// the whole interface stays consistent. Textures are generated once, never per frame.
public static class SurvivalUITheme
{
    // Palette. The old names are kept so every screen picks up the new colours.
    public static readonly Color Snow = new Color(.945f, .955f, .965f);
    public static readonly Color Frost = new Color(.62f, .84f, .95f);
    public static readonly Color Mist = new Color(.63f, .70f, .75f);
    public static readonly Color Slate = new Color(.40f, .48f, .54f);
    public static readonly Color Ember = new Color(1f, .67f, .40f);
    public static readonly Color Ivory = Snow;
    public static readonly Color Secondary = Mist;
    public static readonly Color Ice = Frost;
    public static readonly Color Muted = Slate;
    public static readonly Color Warning = Ember;
    public static readonly Color GlassTop = new Color(.050f, .068f, .088f, .95f);
    public static readonly Color GlassBottom = new Color(.014f, .021f, .030f, .97f);

    static Font body, heading, serif;
    static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
    static readonly List<Object> owned = new List<Object>();

    public static Font Body => body != null ? body : body = Resources.Load<Font>("Fonts/Inter-Variable") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    public static Font Heading => heading != null ? heading : heading = Resources.Load<Font>("Fonts/BarlowCondensed-SemiBold") ?? Body;
    public static Font Serif => serif != null ? serif : serif = Resources.Load<Font>("Fonts/PrologueSerif") ?? Body;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        foreach (var o in owned) if (o) Object.Destroy(o);
        owned.Clear(); sprites.Clear(); body = heading = serif = null;
    }

    // ------------------------------------------------------------------ sprites

    static Sprite Make(string key, int w, int h, Vector4 border, System.Func<int, int, Color> paint)
    {
        if (sprites.TryGetValue(key, out var s) && s) return s;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Frostline " + key, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = paint(x, y);
        t.SetPixels(px); t.Apply(false, true);
        s = Sprite.Create(t, new Rect(0, 0, w, h), Vector2.one * .5f, 100, 0, SpriteMeshType.FullRect, border);
        owned.Add(t); owned.Add(s); sprites[key] = s;
        return s;
    }

    // Signed distance to a rounded rectangle (negative inside), in pixels.
    static float RoundRect(float x, float y, float w, float h, float r)
    {
        float qx = Mathf.Abs(x - w * .5f) - (w * .5f - r), qy = Mathf.Abs(y - h * .5f) - (h * .5f - r);
        float ox = Mathf.Max(qx, 0), oy = Mathf.Max(qy, 0);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
    }
    // Dark overlays are composited in linear space over a bright HDR-graded scene, so
    // partial alpha darkens far less than it looks on paper. Scrims, shadows and the
    // vignette lift their alpha curve to read as intended over snow.
    static float Dark(float a) => 1f - Mathf.Pow(1f - Mathf.Clamp01(a), 2.6f);
    static float Dither(int x, int y) => ((x * 73 + y * 151) % 17) / 17f * .004f;

    // Frosted glass: a cold light at the top fading down, a hairline rim and a brighter
    // top edge, rounded corners. 9-sliced so it scales to any panel.
    public static Sprite PanelSprite => Make("glass", 96, 96, new Vector4(16, 16, 16, 16), (x, y) =>
    {
        const int n = 96; float d = RoundRect(x + .5f, y + .5f, n, n, 9);
        float a = Mathf.Clamp01(.5f - d);
        if (a <= 0) return Color.clear;
        float v = y / (n - 1f);
        var c = Color.Lerp(GlassBottom, GlassTop, Mathf.SmoothStep(0, 1, v));
        c.r += Dither(x, y); c.g += Dither(x, y); c.b += Dither(x, y);
        float rim = Mathf.Clamp01(1f - Mathf.Abs(d + 1f));                  // 1 px rim
        float top = rim * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.55f, 1f, v));                      // lit from above
        c = Color.Lerp(c, new Color(.62f, .78f, .88f, c.a), rim * .08f + top * .20f);
        c.a *= a; return c;
    });

    // Recessed well: darker, with a soft inner shadow under the top edge.
    public static Sprite InsetSprite => Make("inset", 64, 64, new Vector4(10, 10, 10, 10), (x, y) =>
    {
        const int n = 64; float d = RoundRect(x + .5f, y + .5f, n, n, 6);
        float a = Mathf.Clamp01(.5f - d);
        if (a <= 0) return Color.clear;
        float v = y / (n - 1f);
        var c = new Color(.018f + Dither(x, y), .027f + Dither(x, y), .037f + Dither(x, y), .72f);
        float innerShadow = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(n - 8, n - 1, y));
        c = Color.Lerp(c, new Color(0, 0, 0, .8f), innerShadow * .5f);
        float rim = Mathf.Clamp01(1f - Mathf.Abs(d + 1f));
        c = Color.Lerp(c, new Color(.55f, .70f, .80f, c.a), rim * (.04f + .06f * (1 - v)));   // bottom lip catches light
        c.a *= a; return c;
    });

    // Rounded 1 px outline for borders, matching the glass corners.
    public static Sprite StrokeSprite => Make("stroke", 48, 48, new Vector4(12, 12, 12, 12), (x, y) =>
    {
        float d = RoundRect(x + .5f, y + .5f, 48, 48, 8);
        float a = Mathf.Clamp01(1f - Mathf.Abs(d + .8f) * 1.1f);
        return new Color(1, 1, 1, a);
    });

    // Soft filled rounded shape: selection washes and hover fills.
    public static Sprite SoftSelectionSprite => Make("soft", 48, 48, new Vector4(12, 12, 12, 12), (x, y) =>
    {
        float d = RoundRect(x + .5f, y + .5f, 48, 48, 8);
        return new Color(1, 1, 1, Mathf.Clamp01(.5f - d));
    });

    // Fully rounded capsule for bars and tracks.
    public static Sprite PillSprite => Make("pill", 32, 16, new Vector4(8, 8, 8, 8), (x, y) =>
    {
        float d = RoundRect(x + .5f, y + .5f, 32, 16, 8);
        return new Color(1, 1, 1, Mathf.Clamp01(.5f - d));
    });

    // Feathered drop shadow, drawn behind panels with a margin of ShadowPad.
    public const float ShadowPad = 22;
    public static Sprite ShadowSprite => Make("shadow", 96, 96, new Vector4(40, 40, 40, 40), (x, y) =>
    {
        // The panel edge sits ShadowPad px inside the sprite edge: full strength there,
        // easing out to nothing at the sprite edge. Subtle: it grounds a panel on bright
        // snow without reading as a second frame.
        float d = RoundRect(x + .5f, y + .5f, 96, 96, 30);
        float t = Mathf.Clamp01(-d / ShadowPad);
        return new Color(0, 0, 0, Dark(t * t * (3 - 2 * t) * .30f));
    });

    // Horizontal hairline that fades out at both ends.
    public static Sprite HairlineSprite => Make("hairline", 128, 4, new Vector4(0, 0, 0, 0), (x, y) =>
    {
        float u = x / 127f; float a = Mathf.SmoothStep(0, 1, u / .14f) * Mathf.SmoothStep(0, 1, (1 - u) / .14f);
        return new Color(1, 1, 1, a);
    });

    // Radial glow: accents, spotlight behind item previews, pulsing highlights.
    public static Sprite GlowSprite => Make("glow", 64, 64, Vector4.zero, (x, y) =>
    {
        float dx = (x + .5f) / 32f - 1, dy = (y + .5f) / 32f - 1, r = Mathf.Sqrt(dx * dx + dy * dy);
        float a = Mathf.Clamp01(1 - r); return new Color(1, 1, 1, a * a);
    });

    // Legibility scrim: dark on the left, feathered out to the right and top/bottom.
    public static Sprite ScrimSprite => Make("scrim", 128, 64, Vector4.zero, (x, y) =>
    {
        float u = x / 127f, v = y / 63f;
        float a = (1 - Mathf.SmoothStep(0, 1, u)) * Mathf.SmoothStep(0, 1, Mathf.Min(v, 1 - v) / .35f);
        return new Color(0, .01f, .02f, Dark(a));
    });

    // Elliptical scrim for centred text (subtitles, title cards).
    public static Sprite OvalScrimSprite => Make("ovalscrim", 128, 64, Vector4.zero, (x, y) =>
    {
        float dx = (x + .5f) / 64f - 1, dy = (y + .5f) / 32f - 1; float r = Mathf.Sqrt(dx * dx + dy * dy);
        float a = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.25f, 1f, r)); return new Color(0, .01f, .02f, Dark(a));
    });

    // Vignette for full-screen menus: clear centre, dark edges.
    public static Sprite VignetteSprite => Make("vignette", 128, 128, Vector4.zero, (x, y) =>
    {
        float dx = (x + .5f) / 64f - 1, dy = (y + .5f) / 64f - 1; float r = Mathf.Sqrt(dx * dx * .8f + dy * dy);
        return new Color(0, .005f, .012f, Dark(Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.35f, 1.25f, r)) * .9f));
    });

    // Key cap: rounded, bevelled, with a lit top face.
    public static Sprite KeySprite => Make("key", 48, 48, new Vector4(12, 12, 12, 12), (x, y) =>
    {
        float d = RoundRect(x + .5f, y + .5f, 48, 48, 8);
        float a = Mathf.Clamp01(.5f - d);
        if (a <= 0) return Color.clear;
        float v = y / 47f;
        var c = Color.Lerp(new Color(.07f, .09f, .115f, .92f), new Color(.15f, .19f, .23f, .92f), Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.1f, 1f, v)));
        if (y < 4) c = Color.Lerp(c, new Color(.02f, .03f, .04f, .95f), .7f);                  // base lip
        float rim = Mathf.Clamp01(1f - Mathf.Abs(d + 1f));
        c = Color.Lerp(c, new Color(.72f, .84f, .92f, c.a), rim * (.25f + .35f * v));
        c.a *= a; return c;
    });

    // ------------------------------------------------------------------ builders

    public static void Place(RectTransform r, float x, float y, float w, float h)
    { r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h); }

    public static void Stretch(RectTransform r, float inset = 0)
    { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.one * inset; r.offsetMax = -Vector2.one * inset; }

    public static Image Surface(Transform parent, string name, float x, float y, float w, float h, bool recessed = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false); Place(go.GetComponent<RectTransform>(), x, y, w, h);
        var image = go.GetComponent<Image>(); image.sprite = recessed ? InsetSprite : PanelSprite; image.type = Image.Type.Sliced; image.color = Color.white;
        if (!recessed) DropShadow(image);
        return image;
    }

    // A feathered shadow sibling behind a panel that follows it wherever it moves,
    // resizes or fades.
    public static Image DropShadow(Image target, float strength = 1f)
    {
        var go = new GameObject(target.name + " shadow", typeof(RectTransform), typeof(Image), typeof(UIFollowShadow));
        go.transform.SetParent(target.transform.parent, false);
        go.transform.SetSiblingIndex(target.transform.GetSiblingIndex());
        var image = go.GetComponent<Image>(); image.sprite = ShadowSprite; image.type = Image.Type.Sliced; image.raycastTarget = false;
        image.color = new Color(1, 1, 1, strength);
        var follow = go.GetComponent<UIFollowShadow>(); follow.target = target; follow.strength = strength; follow.Sync();
        return image;
    }

    public static Image Stroke(Transform parent, Color color)
    {
        var go = new GameObject("Fine Border", typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>());
        var image = go.GetComponent<Image>(); image.sprite = StrokeSprite; image.type = Image.Type.Sliced; image.color = color; image.raycastTarget = false; return image;
    }

    public static Text Text(Transform parent, string value, float x, float y, float w, float h, int size, bool headline = false, Color? color = null, TextAnchor alignment = TextAnchor.UpperLeft)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(UnityEngine.UI.Text)); go.transform.SetParent(parent, false); Place(go.GetComponent<RectTransform>(), x, y, w, h);
        var label = go.GetComponent<UnityEngine.UI.Text>(); label.font = headline ? Heading : Body; label.fontSize = size; label.color = color ?? Snow; label.alignment = alignment; label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Overflow;
        label.text = Unspace(value);
        // Small capitals are tracked out; condensed headlines get a touch of air.
        // Titles that were hand-spaced ("L A T E R") keep a wide, even tracking.
        if (label.text != value) Track(label, .32f);
        else if (IsCaps(label.text) && size <= 14) Track(label, headline ? .10f : .16f);
        else if (headline) Track(label, .025f);
        return label;
    }

    public static Image Line(Transform parent, float x, float y, float width, Color? color = null)
    {
        var go = new GameObject("Hairline", typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false); Place(go.GetComponent<RectTransform>(), x, y, width, 1);
        var image = go.GetComponent<Image>(); image.sprite = HairlineSprite; image.color = color ?? new Color(.55f, .72f, .82f, .28f); image.raycastTarget = false; return image;
    }

    public static Text Key(Transform parent, string glyph, float x, float y, float size)
    {
        float w = Mathf.Max(size, 14 + glyph.Length * size * .32f);
        var go = new GameObject("Key " + glyph, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false); Place(go.GetComponent<RectTransform>(), x, y, w, size);
        var image = go.GetComponent<Image>(); image.sprite = KeySprite; image.type = Image.Type.Sliced; image.color = Color.white; image.raycastTarget = false;
        var label = Text(go.transform, glyph, 0, -1, w, size, Mathf.RoundToInt(size * .5f), true, Snow, TextAnchor.MiddleCenter);
        return label;
    }

    // A small rotated-square accent with a soft glow.
    public static RectTransform Diamond(Transform parent, float x, float y, float size, Color color, bool glow = true)
    {
        var holder = new GameObject("Diamond", typeof(RectTransform)); holder.transform.SetParent(parent, false);
        var hr = holder.GetComponent<RectTransform>(); Place(hr, x, y, size, size);
        if (glow)
        {
            var g = Glow(holder.transform, color, size * 2.4f); g.color = new Color(color.r, color.g, color.b, .16f);
        }
        var d = new GameObject("Mark", typeof(RectTransform), typeof(Image)); d.transform.SetParent(holder.transform, false);
        var dr = d.GetComponent<RectTransform>(); dr.anchorMin = dr.anchorMax = dr.pivot = Vector2.one * .5f; dr.sizeDelta = Vector2.one * size * .72f; dr.localRotation = Quaternion.Euler(0, 0, 45);
        var di = d.GetComponent<Image>(); di.color = color; di.raycastTarget = false;
        return hr;
    }

    public static Image Glow(Transform parent, Color color, float size)
    {
        var go = new GameObject("Glow", typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
        var r = go.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = r.pivot = Vector2.one * .5f; r.sizeDelta = Vector2.one * size;
        var i = go.GetComponent<Image>(); i.sprite = GlowSprite; i.color = color; i.raycastTarget = false; return i;
    }

    // A capsule bar (track + fill) of the given width; returns the fill.
    public static Image Bar(Transform parent, string name, float x, float y, float w, float h, Color fill, out Image track)
    {
        track = Surface(parent, name + " track", x, y, w, h, true); track.sprite = PillSprite; track.color = new Color(.30f, .40f, .47f, .35f);
        var f = Surface(parent, name, x, y, 0, h, true); f.sprite = PillSprite; f.color = fill;
        return f;
    }

    public static void Track(UnityEngine.UI.Text label, float em)
    {
        var t = label.GetComponent<UITracking>() ?? label.gameObject.AddComponent<UITracking>();
        t.em = em; label.SetVerticesDirty();
    }

    public static void Soften(Graphic g, float alpha = .55f, float distance = 1.5f)
    {
        var s = g.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, .01f, .02f, alpha); s.effectDistance = new Vector2(0, -distance);
    }

    static bool IsCaps(string s)
    {
        bool letter = false;
        foreach (char c in s) { if (char.IsLower(c)) return false; if (char.IsLetter(c)) letter = true; if (c == '\n') return false; }
        return letter;
    }

    // "F I E L D   G U I D E" -> "FIELD GUIDE": spacing is done properly by UITracking.
    static string Unspace(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length < 5) return s;
        for (int i = 1; i < s.Length; i += 2) if (s[i] != ' ') return s;
        var b = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (i % 2 == 0) b.Append(s[i]);
            else if (i + 1 < s.Length && s[i + 1] == ' ') { b.Append(' '); i += 2; }
        }
        return b.ToString();
    }
}

// Letter spacing for legacy UI Text: shifts every glyph quad along its line,
// keeping the line's alignment.
[RequireComponent(typeof(Text))]
public sealed class UITracking : BaseMeshEffect
{
    public float em = .1f;
    static readonly List<UIVertex> verts = new List<UIVertex>();
    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0) return;
        var text = GetComponent<Text>();
        float spacing = em * text.fontSize;
        verts.Clear(); vh.GetUIVertexStream(verts);
        int quads = verts.Count / 6;
        // Group consecutive glyphs into lines by their baseline.
        int start = 0;
        for (int q = 0; q <= quads; q++)
        {
            bool newLine = q == quads || (q > start && Mathf.Abs(verts[q * 6].position.y - verts[(q - 1) * 6].position.y) > text.fontSize * .6f);
            if (!newLine) continue;
            int count = q - start;
            float total = spacing * Mathf.Max(0, count - 1);
            float shift = 0;
            switch (text.alignment)
            {
                case TextAnchor.UpperCenter: case TextAnchor.MiddleCenter: case TextAnchor.LowerCenter: shift = -total * .5f; break;
                case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: shift = -total; break;
            }
            for (int g = 0; g < count; g++)
                for (int k = 0; k < 6; k++)
                {
                    int i = (start + g) * 6 + k; var v = verts[i]; v.position.x += shift + g * spacing; verts[i] = v;
                }
            start = q;
        }
        vh.Clear(); vh.AddUIVertexTriangleStream(verts);
    }
}

// Keeps a drop shadow glued to its panel: position, size, visibility and fade.
[DefaultExecutionOrder(32000)]   // after every view has placed its panel this frame
public sealed class UIFollowShadow : MonoBehaviour
{
    public Image target; public float strength = 1f;
    RectTransform self, t; Image image; CanvasGroup group;
    public void Sync()
    {
        if (!target) { Destroy(gameObject); return; }
        if (!self) { self = (RectTransform)transform; t = target.rectTransform; image = GetComponent<Image>(); }
        if (!group) group = target.GetComponent<CanvasGroup>();   // views often add their group after the surface
        if (self.parent != t.parent) self.SetParent(t.parent, false);
        self.anchorMin = t.anchorMin; self.anchorMax = t.anchorMax; self.pivot = t.pivot;
        self.localRotation = t.localRotation; self.localScale = t.localScale;
        float p = SurvivalUITheme.ShadowPad;
        var size = t.sizeDelta; self.sizeDelta = size + Vector2.one * p * 2;
        // Offset keeps the padding centred around the panel for any pivot, and drops
        // the shadow a few pixels below it.
        // Same centre as the panel for any pivot, dropped a few pixels.
        self.anchoredPosition = t.anchoredPosition + new Vector2((t.pivot.x * 2 - 1) * p, (t.pivot.y * 2 - 1) * p) + new Vector2(0, -6);
        // Only glass panels cast shadows (surfaces re-used as flat bars do not), and
        // the shadow stays active so it can follow the panel back on.
        bool glass = target.sprite == SurvivalUITheme.PanelSprite && target.enabled && target.gameObject.activeInHierarchy;
        float a = glass ? strength * target.color.a * (group ? group.alpha : 1) : 0;
        image.color = new Color(1, 1, 1, a); image.enabled = a > .002f;
    }
    void LateUpdate() => Sync();
}
