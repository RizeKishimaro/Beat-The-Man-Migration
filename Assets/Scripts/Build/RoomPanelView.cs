using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Ticket 176 — reusable Room Panel (Figma 4850:1530).
/// Default → Hover: instant shadow 3→10.
/// Hover → Selected: 1ms then bouncy icon 158→310×207 about centre x=111, text 223→235.
/// Hover/Selected → Default: 0.3s ease-out together. Border never animates.
/// </summary>
public class RoomPanelView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public const float Width = 448f;
    public const float Height = 153f;
    const float Border = 8f;
    const float IconDefault = 158f;
    const float IconSelW = 310f;
    const float IconSelH = 207f;
    const float IconCenterX = 111f;
    const float TextDefault = 223f;
    const float TextSelected = 235f;
    const float SelectDelay = 0.001f;
    const float LeaveDuration = 0.3f;
    const float SpringW = 10.5f;
    const float SpringZ = 0.42f;

    public static readonly Color Accent = new Color(229f / 255f, 14f / 255f, 106f / 255f, 1f);

    public string Title { get; private set; }
    public string Price { get; private set; }

    System.Action<RoomPanelView> onChosen;
    RectTransform shadow;
    RectTransform iconRt;
    RectTransform textRt;
    Image borderImg;
    Image shadowImg;
    Image iconImg;
    bool locked;
    bool springing;
    bool leaving;
    float pose;
    float poseVel;
    float selectAge;
    float leaveT = 1f;
    float selectDelay = -1f;
    float leavePoseFrom;
    Vector2 leaveShadowFrom;
    Vector2 shadowDefault = new Vector2(3f, -3f);
    Vector2 shadowHover = new Vector2(10f, -10f);
    Canvas sortCanvas;

    public static RoomPanelView Create(Transform parent, string title, string price, Sprite icon, System.Action<RoomPanelView> onChosen)
    {
        GameObject go = new GameObject(title.Replace(" ", "") + "Panel");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(Width, Height);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = Width;
        le.preferredHeight = Height;
        le.minHeight = Height;
        var view = go.AddComponent<RoomPanelView>();
        view.Build(title, price, icon, onChosen);
        return view;
    }

    void Build(string title, string price, Sprite icon, System.Action<RoomPanelView> chosen)
    {
        Title = title;
        Price = price;
        onChosen = chosen;

        Color accent = Accent;
        Sprite round = NewRoomSprites.Round20();

        shadow = MakeImage("Shadow", transform, round);
        shadowImg = shadow.GetComponent<Image>();
        shadowImg.color = accent;
        shadowImg.raycastTarget = false;
        Stretch(shadow);
        shadow.anchoredPosition = shadowDefault;
        shadow.SetAsFirstSibling();

        RectTransform border = MakeImage("Border", transform, round);
        borderImg = border.GetComponent<Image>();
        borderImg.color = accent;
        Stretch(border);

        RectTransform fill = MakeImage("Fill", transform, NewRoomSprites.Round12());
        Image fillImg = fill.GetComponent<Image>();
        fillImg.color = Color.white;
        fillImg.raycastTarget = false;
        Inset(fill, Border);
        fill.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        RectTransform grad = MakeImage("Gradient", fill, NewRoomSprites.CardGradient());
        Image gradImg = grad.GetComponent<Image>();
        gradImg.type = Image.Type.Simple;
        gradImg.raycastTarget = false;
        gradImg.preserveAspect = false;
        Stretch(grad);

        RectTransform content = new GameObject("Content").AddComponent<RectTransform>();
        content.SetParent(transform, false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(0f, 1f);
        content.pivot = new Vector2(0f, 1f);
        content.anchoredPosition = new Vector2(-8f, 8f);
        content.sizeDelta = new Vector2(Width, Height);

        iconRt = new GameObject("Icon").AddComponent<RectTransform>();
        iconRt.SetParent(content, false);
        iconRt.anchorMin = new Vector2(0f, 1f);
        iconRt.anchorMax = new Vector2(0f, 1f);
        iconRt.pivot = new Vector2(0.5f, 0.5f);
        iconRt.anchoredPosition = new Vector2(IconCenterX, -Height * 0.5f);
        iconRt.sizeDelta = new Vector2(IconDefault, IconDefault);
        iconImg = iconRt.gameObject.AddComponent<Image>();
        iconImg.sprite = icon;
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;
        iconImg.color = Color.white;

        textRt = new GameObject("Text").AddComponent<RectTransform>();
        textRt.SetParent(content, false);
        textRt.anchorMin = new Vector2(0f, 1f);
        textRt.anchorMax = new Vector2(0f, 1f);
        textRt.pivot = new Vector2(0f, 0.5f);
        textRt.anchoredPosition = new Vector2(TextDefault, -Height * 0.5f);
        textRt.sizeDelta = new Vector2(213f, 128f);

        var v = textRt.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 16f;
        v.childAlignment = TextAnchor.MiddleLeft;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = false;
        v.childForceExpandHeight = false;
        v.padding = new RectOffset(0, 0, 0, 0);

        string displayTitle = DisplayTitle(title);
        bool twoLine = displayTitle.IndexOf('\n') >= 0;
        float titleH = twoLine ? 54f : 34f;

        RectTransform titleSlot = new GameObject("Title").AddComponent<RectTransform>();
        titleSlot.SetParent(textRt, false);
        var titleLe = titleSlot.gameObject.AddComponent<LayoutElement>();
        titleLe.preferredWidth = 213f;
        titleLe.minWidth = 213f;
        titleLe.minHeight = titleH;
        titleLe.preferredHeight = titleH;
        titleLe.flexibleHeight = 0f;

        RectTransform titleTextRt = new GameObject("TitleText").AddComponent<RectTransform>();
        titleTextRt.SetParent(titleSlot, false);
        titleTextRt.anchorMin = new Vector2(0f, 1f);
        titleTextRt.anchorMax = new Vector2(0f, 1f);
        titleTextRt.pivot = new Vector2(0f, 1f);
        titleTextRt.anchoredPosition = Vector2.zero;
        titleTextRt.sizeDelta = new Vector2(426f, titleH * 2f);
        titleTextRt.localScale = new Vector3(0.5f, 0.5f, 1f);
        Text titleText = titleTextRt.gameObject.AddComponent<Text>();
        ApplyText(titleText, NewRoomFonts.Paytone(), displayTitle, 72, accent);
        titleText.alignment = TextAnchor.UpperLeft;
        titleText.horizontalOverflow = HorizontalWrapMode.Overflow;
        titleText.verticalOverflow = VerticalWrapMode.Overflow;
        titleText.lineSpacing = 0.68f;
        titleText.alignByGeometry = true;

        GameObject badge = new GameObject("PriceBadge");
        badge.transform.SetParent(textRt, false);
        Image badgeImg = badge.AddComponent<Image>();
        badgeImg.sprite = NewRoomSprites.Round8();
        badgeImg.type = Image.Type.Sliced;
        badgeImg.pixelsPerUnitMultiplier = 1f;
        badgeImg.color = accent;
        badgeImg.raycastTarget = false;
        var badgeLe = badge.AddComponent<LayoutElement>();
        badgeLe.minHeight = 30f;
        badgeLe.preferredHeight = 30f;
        badgeLe.flexibleHeight = 0f;
        badgeLe.flexibleWidth = 0f;
        var badgeFit = badge.AddComponent<HorizontalLayoutGroup>();
        badgeFit.padding = new RectOffset(12, 12, 3, 6);
        badgeFit.childAlignment = TextAnchor.MiddleCenter;
        badgeFit.childControlWidth = true;
        badgeFit.childControlHeight = true;
        badgeFit.childForceExpandWidth = false;
        badgeFit.childForceExpandHeight = false;

        GameObject priceGo = new GameObject("Price");
        priceGo.transform.SetParent(badge.transform, false);
        Text priceText = priceGo.AddComponent<Text>();
        ApplyText(priceText, NewRoomFonts.NunitoExtraBold(), price, 20, Color.white);
        priceText.alignment = TextAnchor.MiddleCenter;
        priceText.horizontalOverflow = HorizontalWrapMode.Overflow;
        priceText.verticalOverflow = VerticalWrapMode.Overflow;
        priceText.fontStyle = FontStyle.Bold;
        priceText.alignByGeometry = true;

        var btn = gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        Image hit = borderImg;
        btn.targetGraphic = hit;
        btn.onClick.AddListener(OnClicked);
    }

    public void ResetMotion()
    {
        locked = false;
        springing = false;
        leaving = false;
        selectDelay = -1f;
        pose = 0f;
        poseVel = 0f;
        selectAge = 0f;
        leaveT = 1f;
        if (iconRt != null && textRt != null)
            ApplyPose(0f);
        if (shadow != null)
            shadow.anchoredPosition = shadowDefault;
        if (sortCanvas == null)
            sortCanvas = GetComponent<Canvas>();
        LowerSort();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (locked)
            return;
        ArmHoverSpring();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (locked)
            return;
        StartLeave();
    }

    public void PreviewHover()
    {
        if (locked)
            return;
        ArmHoverSpring();
    }

    public void PreviewSelected()
    {
        PlaySelected();
    }

    public void PlaySelected()
    {
        locked = true;
        leaving = false;
        SnapHoverShadow();
        RaiseSort();
        selectDelay = -1f;
        springing = true;
        poseVel = 0f;
        selectAge = 0f;
    }

    void OnClicked()
    {
        PlaySelected();
        onChosen?.Invoke(this);
    }

    void ArmHoverSpring()
    {
        leaving = false;
        SnapHoverShadow();
        RaiseSort();
        springing = false;
        poseVel = 0f;
        selectDelay = SelectDelay;
        selectAge = 0f;
    }

    void StartLeave()
    {
        leaving = true;
        springing = false;
        locked = false;
        selectDelay = -1f;
        leaveT = 0f;
        leavePoseFrom = pose;
        leaveShadowFrom = shadow.anchoredPosition;
    }

    void Update()
    {
        if (selectDelay >= 0f)
        {
            selectDelay -= Time.unscaledDeltaTime;
            if (selectDelay <= 0f)
            {
                selectDelay = -1f;
                springing = true;
            }
        }

        if (springing)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 45f);
            selectAge += dt;
            float acc = SpringW * SpringW * (1f - pose) - 2f * SpringZ * SpringW * poseVel;
            poseVel += acc * dt;
            pose += poseVel * dt;
            ApplyPose(pose);
            shadow.anchoredPosition = shadowHover;
            if (selectAge >= 0.9f && Mathf.Abs(1f - pose) < 0.01f && Mathf.Abs(poseVel) < 0.05f)
            {
                pose = 1f;
                poseVel = 0f;
                springing = false;
                ApplyPose(1f);
            }
            return;
        }

        if (leaving && leaveT < 1f)
        {
            leaveT = Mathf.Min(1f, leaveT + Time.unscaledDeltaTime / LeaveDuration);
            float k = EaseOutCubic(leaveT);
            pose = Mathf.LerpUnclamped(leavePoseFrom, 0f, k);
            poseVel = 0f;
            ApplyPose(pose);
            shadow.anchoredPosition = Vector2.LerpUnclamped(leaveShadowFrom, shadowDefault, k);
            if (leaveT >= 1f)
            {
                leaving = false;
                pose = 0f;
                LowerSort();
            }
        }
    }

    void RaiseSort()
    {
        if (sortCanvas == null)
        {
            sortCanvas = gameObject.GetComponent<Canvas>();
            if (sortCanvas == null)
                sortCanvas = gameObject.AddComponent<Canvas>();
            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();
        }
        sortCanvas.enabled = true;
        GraphicRaycaster ray = GetComponent<GraphicRaycaster>();
        if (ray != null)
            ray.enabled = true;
        sortCanvas.overrideSorting = true;
        sortCanvas.sortingOrder = 32;
    }

    void LowerSort()
    {
        GraphicRaycaster ray = GetComponent<GraphicRaycaster>();
        if (ray != null)
            Destroy(ray);
        if (sortCanvas != null)
        {
            sortCanvas.enabled = true;
            Destroy(sortCanvas);
            sortCanvas = null;
        }
    }

    void SnapHoverShadow()
    {
        shadow.anchoredPosition = shadowHover;
    }

    void ApplyPose(float k)
    {
        iconRt.sizeDelta = Vector2.LerpUnclamped(
            new Vector2(IconDefault, IconDefault),
            new Vector2(IconSelW, IconSelH),
            k);
        textRt.anchoredPosition = new Vector2(
            Mathf.Lerp(TextDefault, TextSelected, k),
            -Height * 0.5f);
    }

    static string DisplayTitle(string title)
    {
        if (title == "Design Studio")
            return "Design\nStudio";
        if (title == "Fabrication Room")
            return "Fabrication\nRoom";
        return title;
    }

    static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    static RectTransform MakeImage(string name, Transform parent, Sprite sprite)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1f;
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void Inset(RectTransform rt, float px)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(px, px);
        rt.offsetMax = new Vector2(-px, -px);
    }

    static void ApplyText(Text text, Font font, string value, int size, Color color)
    {
        if (font != null)
            text.font = font;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
    }
}
