using UnityEngine;

/// <summary>
/// Ticket 176 — 9-slice rounded rects at 2× texel density so corners stay sharp.
/// </summary>
public static class NewRoomSprites
{
    const float Ppu2x = 200f;
    static Sprite sRound20;
    static Sprite sRound12;
    static Sprite sRound8;
    static Sprite sGradient;
    static Texture2D sGradTex;

    public static Sprite Round20()
    {
        if (sRound20 == null)
            sRound20 = MakeRound(128, 40, 20);
        return sRound20;
    }

    public static Sprite Round12()
    {
        if (sRound12 == null)
            sRound12 = MakeRound(96, 24, 12);
        return sRound12;
    }

    public static Sprite Round8()
    {
        if (sRound8 == null)
            sRound8 = MakeRound(64, 16, 8);
        return sRound8;
    }

    public static Sprite CardGradient()
    {
        if (sGradient != null)
            return sGradient;
        sGradTex = new Texture2D(8, 256, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "NewRoomCardGradient"
        };
        Color top = new Color(0f, 1f, 1f, 0.2f);
        Color bot = new Color(0f, 140f / 255f, 1f, 0.2f);
        for (int y = 0; y < 256; y++)
        {
            float fromTop = 1f - y / 255f;
            float u = Mathf.InverseLerp(0.457f, 0.803f, fromTop);
            Color g = Color.Lerp(top, bot, u);
            Color c = Color.Lerp(Color.white, new Color(g.r, g.g, g.b, 1f), g.a);
            c.a = 1f;
            for (int x = 0; x < 8; x++)
                sGradTex.SetPixel(x, y, c);
        }
        sGradTex.Apply(false, true);
        sGradient = Sprite.Create(
            sGradTex,
            new Rect(0f, 0f, 8f, 256f),
            new Vector2(0.5f, 0.5f),
            Ppu2x,
            0,
            SpriteMeshType.FullRect);
        sGradient.name = "NewRoomCardGradient";
        return sGradient;
    }

    static Sprite MakeRound(int size, int radiusTexels, int canvasRadius)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "NewRoomRound" + canvasRadius
        };
        float r = radiusTexels;
        float c0 = r - 0.5f;
        float c1 = size - r - 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = DistToRoundRect(x + 0.5f, y + 0.5f, c0, c1, r);
                float a = Mathf.Clamp01(0.5f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply(false, true);
        var border = new Vector4(radiusTexels, radiusTexels, radiusTexels, radiusTexels);
        Sprite sp = Sprite.Create(
            tex,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            Ppu2x,
            0,
            SpriteMeshType.FullRect,
            border);
        sp.name = tex.name;
        return sp;
    }

    static float DistToRoundRect(float x, float y, float c0, float c1, float r)
    {
        float px = x < c0 ? c0 - x : (x > c1 ? x - c1 : 0f);
        float py = y < c0 ? c0 - y : (y > c1 ? y - c1 : 0f);
        if (px == 0f && py == 0f)
            return -1f;
        if (px > 0f && py > 0f)
            return Mathf.Sqrt(px * px + py * py) - r;
        return Mathf.Max(px, py) - r;
    }
}
