using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// Ticket 168 — world-space prompt at the centre of the 8×8 slot the player is in,
/// only when that slot is unoccupied. E / gamepad South (A) opens the stub menu.
/// Dashed border: Figma 8px corner radius, cyan dashes. Centre: SlotBuildIcon.
/// </summary>
[DefaultExecutionOrder(40)]
public class RoomSlotPlusPrompt : MonoBehaviour
{
    public const string PlusName = "RoomSlotPlus";
    const float PlusHeight = 1.15f;
    const float BorderY = 0.04f;
    const float CornerRadiusPx = 8f;
    const float StrokePx = 4f;
    const float IconPx = 39f;
    const float DashPx = 28f;
    const float GapPx = 14f;
    static readonly Color DashColor = new Color(0.227f, 0.780f, 0.965f, 1f);
    const string IconResource = "UI/SlotBuildIcon";

    BuildMenuController menu;
    Transform plusRoot;
    GridCoord shownAnchor;
    bool showing;
    Mesh borderMesh;
    Material borderMat;
    float builtWorldPerPx = -1f;
    float builtSlotSize = -1f;

    public bool PromptVisible => showing;

    void Awake()
    {
        menu = GetComponent<BuildMenuController>();
        if (menu == null)
            menu = FindAnyObjectByType<BuildMenuController>();
        EnsurePlus();
        plusRoot.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        PlayerController player = FindAnyObjectByType<PlayerController>();
        GridSystem grid = GridSystem.Instance;
        bool menuOpen = menu != null && menu.IsOpen;
        bool show = false;
        GridCoord anchor = default;

        if (!menuOpen && player != null && grid != null)
        {
            anchor = RoomSlotGrid.SlotAnchorFromWorld(grid, player.transform.position);
            show = RoomSlotGrid.IsSlotFree(grid, anchor);
        }

        showing = show;
        shownAnchor = anchor;
        if (plusRoot == null)
            return;

        plusRoot.gameObject.SetActive(show);
        if (!show)
            return;

        Vector3 center = RoomSlotGrid.SlotCenterWorld(grid, anchor);
        float worldPerPx = WorldPerPixel();
        float iconWorld = IconPx * worldPerPx;
        plusRoot.position = new Vector3(center.x, PlusHeight, center.z);
        plusRoot.localScale = new Vector3(iconWorld, iconWorld, iconWorld);

        Camera cam = Camera.main;
        if (cam != null)
            plusRoot.rotation = Quaternion.LookRotation(cam.transform.forward, Vector3.up);

        DrawSlotBorder(grid, anchor, worldPerPx);
    }

    void OnDestroy()
    {
        if (borderMesh != null)
            Destroy(borderMesh);
        if (borderMat != null)
            Destroy(borderMat);
    }

    void Update()
    {
        if (!showing || plusRoot == null || !plusRoot.gameObject.activeSelf)
            return;
        if (menu != null && menu.IsOpen)
            return;
        if (!WasOpenPressed())
            return;

        if (menu == null)
            menu = FindAnyObjectByType<BuildMenuController>();
        if (menu != null)
            menu.Open(shownAnchor);
    }

    static bool WasOpenPressed()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame)
            return true;
        Gamepad pad = Gamepad.current;
        if (pad != null && pad.buttonSouth.wasPressedThisFrame)
            return true;
        return false;
    }

    void EnsurePlus()
    {
        Transform existing = transform.Find(PlusName);
        if (existing != null)
        {
            for (int i = existing.childCount - 1; i >= 0; i--)
                DestroyImmediate(existing.GetChild(i).gameObject);
            plusRoot = existing;
        }
        else
        {
            GameObject root = new GameObject(PlusName);
            root.transform.SetParent(transform, false);
            plusRoot = root.transform;
        }

        Texture2D icon = Resources.Load<Texture2D>(IconResource);
#if UNITY_EDITOR
        if (icon == null)
            icon = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/SlotBuildIcon.png");
#endif
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Icon";
        quad.transform.SetParent(plusRoot, false);
        quad.transform.localPosition = Vector3.zero;
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localScale = Vector3.one;
        Collider col = quad.GetComponent<Collider>();
        if (col != null)
            DestroyImmediate(col);
        var rend = quad.GetComponent<Renderer>();
        if (rend != null)
            rend.sharedMaterial = MakeIconMat(icon);
    }

    void DrawSlotBorder(GridSystem grid, GridCoord anchor, float worldPerPx)
    {
        EnsureBorder(worldPerPx, RoomSlotGrid.SlotCells * grid.cellSize);
        Vector3 corner = grid.CellToWorldCorner(anchor);
        corner.y = BorderY;
        Graphics.DrawMesh(borderMesh, Matrix4x4.TRS(corner, Quaternion.identity, Vector3.one), borderMat, 0);
    }

    void EnsureBorder(float worldPerPx, float slotSize)
    {
        if (borderMat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Sprites/Default");
            borderMat = new Material(shader);
            borderMat.color = DashColor;
            if (borderMat.HasProperty("_BaseColor"))
                borderMat.SetColor("_BaseColor", DashColor);
            if (borderMat.HasProperty("_Cull"))
                borderMat.SetFloat("_Cull", 0f);
            borderMat.renderQueue = 3000;
        }

        if (borderMesh != null
            && Mathf.Abs(builtWorldPerPx - worldPerPx) < 0.0001f
            && Mathf.Abs(builtSlotSize - slotSize) < 0.001f)
            return;

        if (borderMesh != null)
            Destroy(borderMesh);

        float radius = CornerRadiusPx * worldPerPx;
        float stroke = StrokePx * worldPerPx;
        float dash = DashPx * worldPerPx;
        float gap = GapPx * worldPerPx;

        var verts = new List<Vector3>(256);
        var indices = new List<int>(256);
        BuildDashedRoundedRect(verts, indices, slotSize, radius, stroke, dash, gap);

        borderMesh = new Mesh { name = "RoomSlotDashBorder" };
        borderMesh.SetVertices(verts);
        borderMesh.SetTriangles(indices, 0);
        borderMesh.RecalculateBounds();
        builtWorldPerPx = worldPerPx;
        builtSlotSize = slotSize;
    }

    static void BuildDashedRoundedRect(
        List<Vector3> verts,
        List<int> indices,
        float size,
        float radius,
        float stroke,
        float dash,
        float gap)
    {
        radius = Mathf.Clamp(radius, 0.01f, size * 0.45f);
        var path = new List<Vector3>(128);
        AppendRoundedRectPath(path, size, radius);
        float period = Mathf.Max(0.05f, dash + gap);
        float halfW = stroke * 0.5f;
        float pathLen = 0f;
        for (int i = 0; i < path.Count - 1; i++)
            pathLen += Vector3.Distance(path[i], path[i + 1]);
        if (pathLen < 0.01f)
            return;

        float t = 0f;
        while (t < pathLen)
        {
            float t1 = Mathf.Min(t + dash, pathLen);
            SamplePath(path, t, out Vector3 a, out Vector3 ta);
            SamplePath(path, t1, out Vector3 b, out Vector3 tb);
            Vector3 pa = Vector3.Cross(Vector3.up, ta).normalized;
            Vector3 pb = Vector3.Cross(Vector3.up, tb).normalized;
            AddDashQuad(verts, indices, a, b, pa, pb, halfW);
            t += period;
        }
    }

    static void AppendRoundedRectPath(List<Vector3> path, float size, float r)
    {
        const int Arc = 8;
        AddLine(path, new Vector3(r, 0f, 0f), new Vector3(size - r, 0f, 0f));
        AddArc(path, new Vector3(size - r, 0f, r), r, -Mathf.PI * 0.5f, 0f, Arc);
        AddLine(path, new Vector3(size, 0f, r), new Vector3(size, 0f, size - r));
        AddArc(path, new Vector3(size - r, 0f, size - r), r, 0f, Mathf.PI * 0.5f, Arc);
        AddLine(path, new Vector3(size - r, 0f, size), new Vector3(r, 0f, size));
        AddArc(path, new Vector3(r, 0f, size - r), r, Mathf.PI * 0.5f, Mathf.PI, Arc);
        AddLine(path, new Vector3(0f, 0f, size - r), new Vector3(0f, 0f, r));
        AddArc(path, new Vector3(r, 0f, r), r, Mathf.PI, Mathf.PI * 1.5f, Arc);
    }

    static void AddLine(List<Vector3> path, Vector3 a, Vector3 b)
    {
        if (path.Count == 0)
            path.Add(a);
        path.Add(b);
    }

    static void AddArc(List<Vector3> path, Vector3 center, float r, float a0, float a1, int steps)
    {
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            float a = Mathf.Lerp(a0, a1, t);
            path.Add(center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
        }
    }

    static void SamplePath(List<Vector3> path, float dist, out Vector3 pos, out Vector3 tan)
    {
        float remain = dist;
        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector3 d = path[i + 1] - path[i];
            float len = d.magnitude;
            if (len < 1e-5f)
                continue;
            if (remain <= len)
            {
                pos = path[i] + d * (remain / len);
                tan = d / len;
                return;
            }
            remain -= len;
        }
        pos = path[path.Count - 1];
        tan = (path[path.Count - 1] - path[path.Count - 2]).normalized;
    }

    static void AddDashQuad(
        List<Vector3> verts,
        List<int> indices,
        Vector3 a,
        Vector3 b,
        Vector3 pa,
        Vector3 pb,
        float halfW)
    {
        int i = verts.Count;
        verts.Add(a - pa * halfW);
        verts.Add(a + pa * halfW);
        verts.Add(b + pb * halfW);
        verts.Add(b - pb * halfW);
        indices.Add(i);
        indices.Add(i + 1);
        indices.Add(i + 2);
        indices.Add(i);
        indices.Add(i + 2);
        indices.Add(i + 3);
        indices.Add(i);
        indices.Add(i + 2);
        indices.Add(i + 1);
        indices.Add(i);
        indices.Add(i + 3);
        indices.Add(i + 2);
    }

    static float WorldPerPixel()
    {
        Camera cam = Camera.main;
        if (cam != null && cam.orthographic)
        {
            float h = cam.pixelHeight > 1 ? cam.pixelHeight : Screen.height;
            if (h > 1)
                return (2f * cam.orthographicSize) / h;
        }
        return 0.02f;
    }

    static Material MakeIconMat(Texture2D icon)
    {
        Shader shader = Shader.Find("BeatTheMan/SlotPromptIcon")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Universal Render Pipeline/Unlit");
        Material mat = new Material(shader);
        Color c = Color.white;
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", c);
        if (icon != null)
        {
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", icon);
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", icon);
        }
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.renderQueue = 3001;
        return mat;
    }
}
