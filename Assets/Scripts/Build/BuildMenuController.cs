using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Ticket 176 — per-player NEW ROOM drawer. Replaces the 168/169 stub popup.
/// Confirm still fires <see cref="PlacementConfirmed"/> (DefaultBuilding).
/// Open: E / South. Confirm: E / South / DefaultBuildingButton. Close: Esc / East / CloseButton.
/// </summary>
[DefaultExecutionOrder(50)]
public class BuildMenuController : MonoBehaviour
{
    public const string CanvasName = "BuildMenuCanvas";
    public const float DrawerWidth = 486f;
    public const float SlideDuration = 0.3f;
    /// <summary>Figma wallpaper: 182px down-right / 10s ≈ 18.2px/s per axis (~26px/s diagonal).</summary>
    public const float WallpaperPxPerSec = 18.2f;
    public const float WallpaperTileW = 486f;
    public const float WallpaperTileH = 909f;

    static readonly Color DrawerCyan = new Color(7f / 255f, 214f / 255f, 255f / 255f, 1f);

    /// <summary>169: spawn DefaultBuilding at this 6×6 slot anchor.</summary>
    public static event System.Action<GridCoord> PlacementConfirmed;

    public static BuildMenuController Instance { get; private set; }

    static readonly List<BuildMenuController> All = new List<BuildMenuController>(2);

    [SerializeField] int playerIndex;
    [SerializeField] bool fromRight;

    public int PlayerIndex => playerIndex;
    public bool FromRight => fromRight;
    public bool IsOpen { get; private set; }
    public GridCoord TargetSlot { get; private set; }

    Canvas canvas;
    RectTransform drawer;
    RawImage wallpaper;
    float slide;
    float slideFrom;
    float slideTo;
    float slideT = 1f;
    int openedFrame = -1;
    int confirmGen;
    Vector2 wallpaperUv;

    struct RoomDef
    {
        public string Title;
        public string Price;
        public string IconResource;
    }

    static readonly RoomDef[] Rooms =
    {
        new RoomDef { Title = "HR Dept", Price = "50,000cr", IconResource = "UI/NewRoom/IconHrDept" },
        new RoomDef { Title = "Garage", Price = "100,000cr", IconResource = "UI/NewRoom/IconGarage" },
        new RoomDef { Title = "Design Studio", Price = "150,000cr", IconResource = "UI/NewRoom/IconDesignStudio" },
        new RoomDef { Title = "Fabrication Room", Price = "150,000cr", IconResource = "UI/NewRoom/IconFabrication" },
    };

    void OnEnable()
    {
        if (!All.Contains(this))
            All.Add(this);
        Instance = this;
    }

    void OnDisable()
    {
        All.Remove(this);
        if (Instance == this)
            Instance = All.Count > 0 ? All[0] : null;
    }

    void Awake()
    {
        Instance = this;
        EnsureUi();
        SetOpen(false, true);
    }

    void Update()
    {
        TickSlide();
        TickWallpaper();
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
            ConfirmPlacement();
        }
    }

    public static bool IsOpenForPlayer(int index)
    {
        for (int i = 0; i < All.Count; i++)
        {
            BuildMenuController m = All[i];
            if (m != null && m.IsOpen && m.playerIndex == index)
                return true;
        }
        return false;
    }

    public void Open(GridCoord slotAnchor)
    {
        EnsureUi();
        TargetSlot = slotAnchor;
        SetOpen(true, false);
        openedFrame = Time.frameCount;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    public void Close()
    {
        confirmGen++;
        SetOpen(false, false);
    }

    void ConfirmPlacement()
    {
        confirmGen++;
        PlacementConfirmed?.Invoke(TargetSlot);
        Close();
    }

    void OnPanelChosen(RoomPanelView panel)
    {
        if (panel == null || !IsOpen)
            return;
        StartCoroutine(ConfirmAfterSelect());
    }

    IEnumerator ConfirmAfterSelect()
    {
        int gen = ++confirmGen;
        yield return new WaitForSecondsRealtime(0.85f);
        if (gen != confirmGen || !IsOpen)
            yield break;
        ConfirmPlacement();
    }

    void SetOpen(bool open, bool instant)
    {
        IsOpen = open;
        if (canvas == null)
            return;
        if (open)
        {
            canvas.gameObject.SetActive(true);
            RoomPanelView[] panels = canvas.GetComponentsInChildren<RoomPanelView>(true);
            for (int i = 0; i < panels.Length; i++)
                panels[i].ResetMotion();
        }
        slideFrom = slide;
        slideTo = open ? 1f : 0f;
        if (instant)
        {
            slide = slideTo;
            slideT = 1f;
            ApplySlide();
            if (!open)
                canvas.gameObject.SetActive(false);
        }
        else
        {
            slideT = 0f;
            if (Mathf.Approximately(slideFrom, slideTo))
                slideT = 1f;
        }
    }

    void TickSlide()
    {
        if (canvas == null || slideT >= 1f)
            return;
        slideT = Mathf.Min(1f, slideT + Time.unscaledDeltaTime / SlideDuration);
        float k = EaseOutCubic(slideT);
        slide = Mathf.LerpUnclamped(slideFrom, slideTo, k);
        ApplySlide();
        if (slideT >= 1f && !IsOpen && canvas != null)
            canvas.gameObject.SetActive(false);
    }

    void ApplySlide()
    {
        if (drawer == null)
            return;
        float hidden = fromRight ? DrawerWidth : -DrawerWidth;
        float x = Mathf.Lerp(hidden, 0f, slide);
        drawer.anchoredPosition = new Vector2(x, 0f);
    }

    static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    void TickWallpaper()
    {
        if (wallpaper == null || canvas == null || !canvas.isActiveAndEnabled)
            return;
        wallpaperUv.x -= WallpaperPxPerSec / WallpaperTileW * Time.unscaledDeltaTime;
        wallpaperUv.y += WallpaperPxPerSec / WallpaperTileH * Time.unscaledDeltaTime;
        float uSize = DrawerWidth / WallpaperTileW;
        float vSize = drawer.rect.height / WallpaperTileH;
        if (vSize < 0.01f)
            vSize = 1080f / WallpaperTileH;
        wallpaper.uvRect = new Rect(wallpaperUv.x, wallpaperUv.y, uSize, vSize);
    }

    void EnsureUi()
    {
        if (canvas != null)
            return;

        EnsureEventSystem();

        GameObject canvasGo = new GameObject(CanvasName);
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        canvas.pixelPerfect = false;
        canvasGo.AddComponent<GraphicRaycaster>();

        drawer = CreateUi(canvasGo.transform, "Drawer", Vector2.zero, new Vector2(DrawerWidth, 0f)).GetComponent<RectTransform>();
        if (fromRight)
        {
            drawer.anchorMin = new Vector2(1f, 0f);
            drawer.anchorMax = new Vector2(1f, 1f);
            drawer.pivot = new Vector2(1f, 0.5f);
        }
        else
        {
            drawer.anchorMin = new Vector2(0f, 0f);
            drawer.anchorMax = new Vector2(0f, 1f);
            drawer.pivot = new Vector2(0f, 0.5f);
        }
        drawer.sizeDelta = new Vector2(DrawerWidth, 0f);
        drawer.anchoredPosition = Vector2.zero;

        Image bg = drawer.gameObject.AddComponent<Image>();
        bg.color = DrawerCyan;
        bg.raycastTarget = true;

        GameObject wallGo = CreateUi(drawer, "Wallpaper", Vector2.zero, Vector2.zero);
        Stretch(wallGo.GetComponent<RectTransform>());
        wallpaper = wallGo.AddComponent<RawImage>();
        Texture2D tile = Resources.Load<Texture2D>("UI/NewRoom/WallpaperTile");
#if UNITY_EDITOR
        if (tile == null)
            tile = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/NewRoom/WallpaperTile.png");
#endif
        if (tile != null)
        {
            tile.wrapMode = TextureWrapMode.Repeat;
            tile.filterMode = FilterMode.Bilinear;
            wallpaper.texture = tile;
        }
        wallpaper.color = Color.white;
        wallpaper.raycastTarget = false;

        GameObject titleGo = CreateUi(drawer, "Title", Vector2.zero, new Vector2(456f, 64f));
        RectTransform titleRt = titleGo.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(0f, 1f);
        titleRt.pivot = new Vector2(0f, 1f);
        titleRt.anchoredPosition = new Vector2(20f, -35f);
        titleRt.sizeDelta = new Vector2(912f, 128f);
        titleRt.localScale = new Vector3(0.5f, 0.5f, 1f);
        Text title = titleGo.AddComponent<Text>();
        title.font = NewRoomFonts.Title();
        title.text = "NEW ROOM";
        title.fontSize = 128;
        title.color = RoomPanelView.Accent;
        title.alignment = TextAnchor.MiddleLeft;
        title.horizontalOverflow = HorizontalWrapMode.Overflow;
        title.verticalOverflow = VerticalWrapMode.Overflow;
        title.raycastTarget = false;

        GameObject listGo = CreateUi(drawer, "RoomList", Vector2.zero, new Vector2(448f, 714f));
        RectTransform listRt = listGo.GetComponent<RectTransform>();
        listRt.anchorMin = new Vector2(0f, 1f);
        listRt.anchorMax = new Vector2(0f, 1f);
        listRt.pivot = new Vector2(0f, 1f);
        listRt.anchoredPosition = new Vector2(20f, -133f);
        listRt.sizeDelta = new Vector2(448f, 780f);
        var layout = listGo.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 34f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        for (int i = 0; i < Rooms.Length; i++)
        {
            Sprite icon = LoadIcon(Rooms[i].IconResource);
            RoomPanelView.Create(listRt, Rooms[i].Title, Rooms[i].Price, icon, OnPanelChosen);
        }

        GameObject placeBtn = CreateHiddenButton(canvasGo.transform, "DefaultBuildingButton");
        placeBtn.GetComponent<Button>().onClick.AddListener(ConfirmPlacement);

        GameObject closeBtn = CreateHiddenButton(canvasGo.transform, "CloseButton");
        closeBtn.GetComponent<Button>().onClick.AddListener(Close);
    }

    static Sprite LoadIcon(string resource)
    {
        Texture2D tex = Resources.Load<Texture2D>(resource);
#if UNITY_EDITOR
        if (tex == null)
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/" + resource + ".png");
#endif
        if (tex == null)
            return null;
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(
            tex,
            new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);
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

    static GameObject CreateHiddenButton(Transform parent, string name)
    {
        GameObject go = CreateUi(parent, name, new Vector2(8f, -8f), new Vector2(4f, 4f));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        Image img = go.AddComponent<Image>();
        img.color = Color.clear;
        img.raycastTarget = false;
        go.AddComponent<Button>();
        return go;
    }
}
