using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// Ticket 168 — world-space prompt at the centre of the 6×6 slot the player is in,
/// only when that slot is unoccupied. E / gamepad South (A) opens the stub menu.
/// Dashed border: Figma 8px corner radius, cyan dashes. Centre: SlotBuildIcon.
/// </summary>
[DefaultExecutionOrder(40)]
public class RoomSlotPlusPrompt : MonoBehaviour
{
    public const string PlusName = "RoomSlotPlus";
    const float PlusHeight = 1.15f;
    const float BobAmp = 0.08f;
    const float BobHz = 0.35f;
    const float BorderY = 0.04f;
    const float FigmaPanelPx = 381f;
    const float CornerRadiusPx = 8f;
    const float StrokePx = 5f;
    const float IconPx = 39f;
    const float DashPx = 13f;
    const float GapPx = 13f;
    static readonly Color DashColor = new Color(0f, 0.75f, 1f, 1f);
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
        float bob = Mathf.Sin(Time.time * (Mathf.PI * 2f * BobHz)) * BobAmp;
        plusRoot.position = new Vector3(center.x, PlusHeight + bob, center.z);
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

        float s = slotSize / FigmaPanelPx;
        float radius = CornerRadiusPx * s;
        float stroke = StrokePx * s;
        float dash = DashPx * s;
        float gap = GapPx * s;

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
        float halfW = stroke * 0.5f;
        float straight = size - 2f * radius;
        float arcLen = 0.5f * Mathf.PI * radius;
        float period = Mathf.Max(0.05f, dash + gap);
        float phase = Mathf.Repeat(-(straight + arcLen * 0.5f - dash * 0.5f), period);

        var path = new List<Vector3>(128);
        for (int side = 0; side < 4; side++)
        {
            var one = new List<Vector3>(32);
            AppendOneSide(one, size, radius);
            RotatePath90(one, size, side);
            int i0 = path.Count == 0 ? 0 : 1;
            for (int i = i0; i < one.Count; i++)
                path.Add(one[i]);
        }
        DashPath(verts, indices, path, dash, gap, halfW, phase, true);
    }

    static void AppendOneSide(List<Vector3> path, float size, float r)
    {
        const int Arc = 12;
        AddLine(path, new Vector3(r, 0f, 0f), new Vector3(size - r, 0f, 0f));
        AddArc(path, new Vector3(size - r, 0f, r), r, -Mathf.PI * 0.5f, 0f, Arc);
    }

    static void RotatePath90(List<Vector3> path, float size, int times)
    {
        float cx = size * 0.5f;
        float cz = size * 0.5f;
        for (int n = 0; n < times; n++)
        {
            for (int i = 0; i < path.Count; i++)
            {
                float x = path[i].x - cx;
                float z = path[i].z - cz;
                path[i] = new Vector3(cx - z, 0f, cz + x);
            }
        }
    }

    static void DashPath(
        List<Vector3> verts,
        List<int> indices,
        List<Vector3> path,
        float dash,
        float gap,
        float halfW,
        float phase,
        bool closed)
    {
        float period = Mathf.Max(0.05f, dash + gap);
        float pathLen = 0f;
        for (int i = 0; i < path.Count - 1; i++)
            pathLen += Vector3.Distance(path[i], path[i + 1]);
        if (closed && path.Count > 1)
            pathLen += Vector3.Distance(path[path.Count - 1], path[0]);
        if (pathLen < 0.01f)
            return;

        float step = Mathf.Max(halfW * 0.45f, 0.02f);
        float t = Mathf.Repeat(phase, period);
        while (t < pathLen)
        {
            float t1 = t + dash;
            if (t1 <= pathLen)
            {
                DrawDashOpen(verts, indices, path, pathLen, t, t1, halfW, step, closed);
            }
            else
            {
                DrawDashOpen(verts, indices, path, pathLen, t, pathLen, halfW, step, closed);
                if (closed)
                    DrawDashOpen(verts, indices, path, pathLen, 0f, t1 - pathLen, halfW, step, closed);
            }
            t += period;
        }
    }

    static void DrawDashOpen(
        List<Vector3> verts,
        List<int> indices,
        List<Vector3> path,
        float pathLen,
        float t0,
        float t1,
        float halfW,
        float step,
        bool closed)
    {
        if (t1 - t0 < 0.0001f)
            return;
        SamplePathClosed(path, pathLen, t0, closed, out Vector3 prev, out Vector3 tanPrev);
        AddDisc(verts, indices, prev, halfW, 10);
        float s = t0;
        while (s < t1 - 0.0001f)
        {
            float s2 = Mathf.Min(s + step, t1);
            SamplePathClosed(path, pathLen, s2, closed, out Vector3 cur, out Vector3 tanCur);
            Vector3 pa = Vector3.Cross(Vector3.up, tanPrev).normalized;
            Vector3 pb = Vector3.Cross(Vector3.up, tanCur).normalized;
            if (pa.sqrMagnitude > 0.01f && pb.sqrMagnitude > 0.01f)
                AddDashQuad(verts, indices, prev, cur, pa, pb, halfW);
            AddDisc(verts, indices, cur, halfW, 10);
            prev = cur;
            tanPrev = tanCur;
            s = s2;
        }
    }

    static void SamplePathClosed(
        List<Vector3> path,
        float pathLen,
        float dist,
        bool closed,
        out Vector3 pos,
        out Vector3 tan)
    {
        if (closed && pathLen > 0.01f)
            dist = Mathf.Repeat(dist, pathLen);
        float remain = dist;
        int last = path.Count - 1;
        int segCount = closed ? path.Count : last;
        for (int i = 0; i < segCount; i++)
        {
            Vector3 a = path[i];
            Vector3 b = path[(i + 1) % path.Count];
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f)
                continue;
            if (remain <= len)
            {
                pos = a + d * (remain / len);
                tan = d / len;
                return;
            }
            remain -= len;
        }
        pos = path[last];
        tan = (path[last] - path[last - 1]).normalized;
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

    static void AddDisc(
        List<Vector3> verts,
        List<int> indices,
        Vector3 center,
        float radius,
        int seg)
    {
        int c = verts.Count;
        verts.Add(center);
        for (int i = 0; i < seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            verts.Add(center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }
        for (int i = 0; i < seg; i++)
        {
            int i1 = c + 1 + i;
            int i2 = c + 1 + ((i + 1) % seg);
            indices.Add(c);
            indices.Add(i1);
            indices.Add(i2);
            indices.Add(c);
            indices.Add(i2);
            indices.Add(i1);
        }
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
        Color c = DashColor;
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
