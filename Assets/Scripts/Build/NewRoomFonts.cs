using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Ticket 176 — fonts for the NEW ROOM drawer. Roc Grotesk Wide Heavy is licensed
/// and is not bundled; load from the OS when present. Paytone One ships in Resources.
/// </summary>
public static class NewRoomFonts
{
    static Font sTitle;
    static Font sPaytone;
    static Font sNunito;
    static Font sFallback;

    public static Font Title()
    {
        if (sTitle != null)
            return sTitle;
        sTitle = Font.CreateDynamicFontFromOSFont(new[]
        {
            "FONTSPRING DEMO - Roc Grotesk Wide Heavy",
            "FSP DEMO - Rc Grtsk Wd Hvy",
            "Roc Grotesk Wide Heavy",
            "Roc Grotesk"
        }, 128);
        if (sTitle == null)
            sTitle = Fallback();
        return sTitle;
    }

    public static Font Paytone()
    {
        if (sPaytone != null)
            return sPaytone;
        sPaytone = Resources.Load<Font>("Fonts/PaytoneOne-Regular");
#if UNITY_EDITOR
        if (sPaytone == null)
            sPaytone = AssetDatabase.LoadAssetAtPath<Font>("Assets/Art/Fonts/PaytoneOne-Regular.ttf");
#endif
        if (sPaytone == null)
            sPaytone = Fallback();
        else
            sPaytone.RequestCharactersInTexture(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789,cr ",
                72);
        return sPaytone;
    }

    public static Font NunitoExtraBold()
    {
        if (sNunito != null)
            return sNunito;
        sNunito = Resources.Load<Font>("Fonts/Nunito-ExtraBold");
        if (sNunito == null)
        {
            sNunito = Font.CreateDynamicFontFromOSFont(new[]
            {
                "Nunito ExtraBold",
                "Nunito Extra Bold",
                "Nunito"
            }, 40);
        }
        if (sNunito == null)
            sNunito = Fallback();
        return sNunito;
    }

    public static Font Fallback()
    {
        if (sFallback == null)
            sFallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return sFallback;
    }

    public static string TitleSource()
    {
        Font f = Title();
        if (f == null)
            return "none";
        return f.name;
    }
}
