using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Ticket 168/169 — stub build menu. Confirm fires <see cref="PlacementConfirmed"/>.
/// Open: E / South. Confirm: E / South / Default Building. Close: Esc / East / Close.
/// </summary>
[DefaultExecutionOrder(50)]
public class BuildMenuController : MonoBehaviour
{
    public const string CanvasName = "BuildMenuCanvas";

    /// <summary>169: spawn DefaultBuilding at this 8×8 slot anchor.</summary>
    public static event System.Action<GridCoord> PlacementConfirmed;

    public static BuildMenuController Instance { get; private set; }

    public bool IsOpen { get; private set; }
    public GridCoord TargetSlot { get; private set; }

    Canvas canvas;
    GameObject panel;
    Text statusText;
    int openedFrame = -1;

    void Awake()
    {
        Instance = this;
        EnsureUi();
        SetOpen(false);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (!IsOpen)
            return;
        if (Time.frameCount == openedFrame)
            return;
        Keyboard kb = Keyboard.current;
        Gamepad pad = Gamepad.current;
        if ((kb != null && kb.escapeKey.wasPressedThisFrame)
            || (pad != null && pad.buttonEast.wasPressedThisFrame))
        {
            Close();
            return;
        }
        if ((kb != null && kb.eKey.wasPressedThisFrame)
            || (pad != null && pad.buttonSouth.wasPressedThisFrame))
        {
            ConfirmDefaultBuilding();
            return;
        }
    }

    public void Open(GridCoord slotAnchor)
    {
        EnsureUi();
        TargetSlot = slotAnchor;
        if (statusText != null)
            statusText.text = "Slot " + slotAnchor;
        SetOpen(true);
        openedFrame = Time.frameCount;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    public void Close()
    {
        SetOpen(false);
    }

    void ConfirmDefaultBuilding()
    {
        PlacementConfirmed?.Invoke(TargetSlot);
        Close();
    }

    void SetOpen(bool open)
    {
        IsOpen = open;
        if (canvas != null)
            canvas.gameObject.SetActive(open);
    }

    void EnsureUi()
    {
        if (canvas != null)
            return;

        EnsureEventSystem();

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject canvasGo = new GameObject(CanvasName);
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGo.AddComponent<GraphicRaycaster>();

        GameObject dim = CreateUi(canvasGo.transform, "Dim", Vector2.zero, Vector2.zero);
        Stretch(dim.GetComponent<RectTransform>());
        Image dimImg = dim.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.45f);

        panel = CreateUi(canvasGo.transform, "Panel", Vector2.zero, new Vector2(420f, 280f));
        Image panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.12f, 0.14f, 0.12f, 0.96f);

        GameObject titleGo = CreateUi(panel.transform, "Title", new Vector2(0f, 90f), new Vector2(380f, 48f));
        Text title = titleGo.AddComponent<Text>();
        ApplyText(title, font, "Build", 36);

        GameObject statusGo = CreateUi(panel.transform, "Status", new Vector2(0f, 48f), new Vector2(380f, 28f));
        statusText = statusGo.AddComponent<Text>();
        ApplyText(statusText, font, "", 18);

        GameObject placeBtn = CreateButton(panel.transform, "DefaultBuildingButton",
            "Default Building", new Vector2(0f, -10f), new Vector2(320f, 56f), font);
        placeBtn.GetComponent<Button>().onClick.AddListener(ConfirmDefaultBuilding);

        GameObject closeBtn = CreateButton(panel.transform, "CloseButton",
            "Close", new Vector2(0f, -80f), new Vector2(200f, 44f), font);
        closeBtn.GetComponent<Button>().onClick.AddListener(Close);
    }

    static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
            return;
        GameObject esGo = new GameObject("EventSystem");
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<InputSystemUIInputModule>();
    }

    static GameObject CreateUi(Transform parent, string name, Vector2 anchored, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = anchored;
        rt.sizeDelta = size;
        return go;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static GameObject CreateButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, Font font)
    {
        GameObject go = CreateUi(parent, name, pos, size);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.28f, 0.42f, 0.26f, 1f);
        Button btn = go.AddComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.highlightedColor = new Color(0.38f, 0.55f, 0.34f, 1f);
        colors.pressedColor = new Color(0.20f, 0.32f, 0.18f, 1f);
        btn.colors = colors;

        GameObject textGo = CreateUi(go.transform, "Text", Vector2.zero, size);
        Stretch(textGo.GetComponent<RectTransform>());
        Text text = textGo.AddComponent<Text>();
        ApplyText(text, font, label, 24);
        text.raycastTarget = false;
        return go;
    }

    static void ApplyText(Text text, Font font, string value, int size)
    {
        if (font != null)
            text.font = font;
        text.text = value;
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
    }
}
