using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Play-mode Game-view grid overlay. Toggle with G.
/// Draws via Graphics.DrawMesh (line mesh + unlit color) — not shader clip /
/// BuildingWallStrip techniques.
/// </summary>
[DefaultExecutionOrder(100)]
public class GridOverlay : MonoBehaviour
{
    [SerializeField] bool visible;
    [SerializeField] int halfExtentCells = 12;
    [SerializeField] Color lineColor = new Color(0.2f, 0.85f, 1f, 0.55f);
    [SerializeField] float yLift = 0.02f;

    Mesh lineMesh;
    Material lineMaterial;
    int builtHalfExtent = -1;
    float builtCellSize = -1f;
    Vector3 builtOrigin;

    public bool Visible
    {
        get => visible;
        set => visible = value;
    }

    void OnEnable()
    {
        EnsureMaterial();
    }

    void OnDisable()
    {
        if (lineMesh != null)
        {
            Destroy(lineMesh);
            lineMesh = null;
        }
        builtHalfExtent = -1;
    }

    void OnDestroy()
    {
        if (lineMaterial != null)
        {
            Destroy(lineMaterial);
            lineMaterial = null;
        }
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.gKey.wasPressedThisFrame)
            visible = !visible;
    }

    void LateUpdate()
    {
        if (!visible)
            return;

        GridSystem grid = GridSystem.Instance;
        if (grid == null || !grid.enabled)
            return;

        EnsureMaterial();
        EnsureMesh(grid);

        Vector3 center = ResolveDrawCenter(grid);
        // Snap mesh so lines stay on grid; translate to the corner of the lowest drawn cell.
        int cx = Mathf.FloorToInt((center.x - grid.originWorld.x) / grid.cellSize);
        int cz = Mathf.FloorToInt((center.z - grid.originWorld.z) / grid.cellSize);
        Vector3 drawOrigin = grid.originWorld
            + new Vector3((cx - halfExtentCells) * grid.cellSize, yLift, (cz - halfExtentCells) * grid.cellSize);

        Matrix4x4 matrix = Matrix4x4.TRS(drawOrigin, Quaternion.identity, Vector3.one);
        Graphics.DrawMesh(lineMesh, matrix, lineMaterial, 0);
    }

    Vector3 ResolveDrawCenter(GridSystem grid)
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Plane ground = new Plane(Vector3.up, grid.originWorld.y);
            if (ground.Raycast(ray, out float enter))
                return ray.GetPoint(enter);
        }

        PlayerController player = FindAnyObjectByType<PlayerController>();
        if (player != null)
            return player.transform.position;

        return grid.originWorld;
    }

    void EnsureMaterial()
    {
        if (lineMaterial != null)
            return;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        lineMaterial = new Material(shader);
        lineMaterial.name = "GridOverlay_Runtime";
        lineMaterial.color = lineColor;
        if (lineMaterial.HasProperty("_BaseColor"))
            lineMaterial.SetColor("_BaseColor", lineColor);
        lineMaterial.renderQueue = 3000;
        lineMaterial.enableInstancing = false;
    }

    void EnsureMesh(GridSystem grid)
    {
        if (lineMesh != null
            && builtHalfExtent == halfExtentCells
            && Mathf.Approximately(builtCellSize, grid.cellSize)
            && builtOrigin == grid.originWorld)
            return;

        if (lineMesh != null)
            Destroy(lineMesh);

        int size = halfExtentCells * 2;
        float cs = grid.cellSize;
        int lineCount = (size + 1) * 2;
        var verts = new Vector3[lineCount * 2];
        var indices = new int[lineCount * 2];

        int vi = 0;
        for (int i = 0; i <= size; i++)
        {
            float x = i * cs;
            verts[vi] = new Vector3(x, 0f, 0f);
            verts[vi + 1] = new Vector3(x, 0f, size * cs);
            indices[vi] = vi;
            indices[vi + 1] = vi + 1;
            vi += 2;
        }
        for (int i = 0; i <= size; i++)
        {
            float z = i * cs;
            verts[vi] = new Vector3(0f, 0f, z);
            verts[vi + 1] = new Vector3(size * cs, 0f, z);
            indices[vi] = vi;
            indices[vi + 1] = vi + 1;
            vi += 2;
        }

        lineMesh = new Mesh { name = "GridOverlayLines" };
        lineMesh.SetVertices(verts);
        lineMesh.SetIndices(indices, MeshTopology.Lines, 0);
        lineMesh.RecalculateBounds();

        builtHalfExtent = halfExtentCells;
        builtCellSize = grid.cellSize;
        builtOrigin = grid.originWorld;
    }
}
