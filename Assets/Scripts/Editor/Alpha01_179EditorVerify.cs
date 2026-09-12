using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 179 — after DefaultBuilding FBX reimport: retarget prefab BoxColliders
/// from mesh local bounds, confirm 6×6 envelope, and flag garage chassis vs 5.4
/// interior. Does not save Alpha01 or GridFoundation.
/// Flag: Temp/alpha01_179.flag
/// Report: Temp/alpha01_179/report.txt
/// </summary>
[InitializeOnLoad]
public static class Alpha01_179EditorVerify
{
    const string PrefabPath = "Assets/Prefabs/Buildings/DefaultBuilding.prefab";
    const string FbxPath = "Assets/Art/Buildings/DefaultBuilding.fbx";
    const string FlagPath = "Temp/alpha01_179.flag";
    const string ReportPath = "Temp/alpha01_179/report.txt";
    const float Ext = 6f;
    const float WallT = 0.3f;
    const float Inner = Ext - 2f * WallT;

    static Alpha01_179EditorVerify()
    {
        EditorApplication.delayCall += TryRun;
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (!File.Exists(Full(FlagPath)))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        TryRun();
    }

    static void TryRun()
    {
        string flag = Full(FlagPath);
        if (!File.Exists(flag))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        File.Delete(flag);
        try
        {
            string report = Run();
            Directory.CreateDirectory(Path.GetDirectoryName(Full(ReportPath)));
            File.WriteAllText(Full(ReportPath), report);
            Debug.Log("[Alpha01_179EditorVerify]\n" + report);
        }
        catch (System.Exception e)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Full(ReportPath)));
            File.WriteAllText(Full(ReportPath), "FAIL: " + e);
            Debug.LogError("[Alpha01_179EditorVerify] " + e);
        }
    }

    [MenuItem("Tools/Scenes/179 Apply DefaultBuilding 6x6 + Garage Flag")]
    public static void MenuRun()
    {
        File.WriteAllText(Full(FlagPath), "run\n");
    }

    static string Run()
    {
        var sb = new StringBuilder();
        int fails = 0;
        sb.AppendLine("=== Ticket 179 DefaultBuilding 6x6 ===");
        sb.AppendLine($"time={System.DateTime.UtcNow:o}");

        AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            fails += PatchPrefab(root, sb);

            BoxCollider interior = null;
            foreach (var box in root.GetComponentsInChildren<BoxCollider>(true))
            {
                if (box.name == "InteriorTrigger")
                {
                    interior = box;
                    break;
                }
            }
            if (interior == null)
            {
                sb.AppendLine("FAIL InteriorTrigger missing");
                fails++;
            }
            else
            {
                sb.AppendLine($"InteriorTrigger size={interior.size} center={interior.center}");
                bool ok =
                    Approx(interior.size.x, Inner) && Approx(interior.size.z, Inner)
                    && Approx(interior.center.x, Ext * 0.5f) && Approx(interior.center.z, Ext * 0.5f);
                sb.AppendLine((ok ? "PASS" : "FAIL") + " InteriorTrigger 5.4 at centre 3");
                if (!ok) fails++;
            }

            GridFootprint fp = root.GetComponent<GridFootprint>();
            if (fp == null)
            {
                sb.AppendLine("FAIL GridFootprint missing");
                fails++;
            }
            else
            {
                sb.AppendLine($"GridFootprint sizeInCells={fp.sizeInCells}");
                bool ok = fp.sizeInCells.x == 6 && fp.sizeInCells.y == 6;
                sb.AppendLine((ok ? "PASS" : "FAIL") + " GridFootprint 6x6");
                if (!ok) fails++;
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            sb.AppendLine("saved " + PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        try
        {
            instance.transform.position = new Vector3(0f, 5000f, 0f);
            Vector3 origin = instance.transform.position;
            Transform wallL = FindNamed(instance.transform, "Wall_Left");
            Transform wallR = FindNamed(instance.transform, "Wall_Right");
            Transform wallB = FindNamed(instance.transform, "Wall_Back");
            Transform frontL = FindNamed(instance.transform, "Wall_Front_Left");
            Transform frontR = FindNamed(instance.transform, "Wall_Front_Right");
            Bounds? env = Encapsulate(wallL, wallR, wallB, frontL, frontR,
                FindNamed(instance.transform, "Wall_Front_Lintel"));
            if (env == null)
            {
                sb.AppendLine("FAIL no wall envelope");
                fails++;
            }
            else
            {
                Vector3 min = env.Value.min - origin;
                Vector3 max = env.Value.max - origin;
                sb.AppendLine($"envelope xz=({min.x:F3},{max.x:F3}) ({min.z:F3},{max.z:F3}) y=({min.y:F3},{max.y:F3})");
                bool ok =
                    Approx(min.x, 0f) && Approx(max.x, Ext)
                    && Approx(min.z, 0f) && Approx(max.z, Ext)
                    && Approx(min.y, 0f) && Approx(max.y, 3f);
                sb.AppendLine((ok ? "PASS" : "FAIL") + " envelope 0–6 / Y 0–3");
                if (!ok) fails++;
            }

            Transform fl = FindNamed(instance.transform, "Wall_Front_Left");
            Transform fr = FindNamed(instance.transform, "Wall_Front_Right");
            Renderer flr = fl != null ? fl.GetComponent<Renderer>() : null;
            Renderer frr = fr != null ? fr.GetComponent<Renderer>() : null;
            if (flr != null && frr != null)
            {
                RoomSlotAnchor.RoomLocalAabb(instance.transform, flr.bounds, out Vector3 lmin, out Vector3 lmax);
                RoomSlotAnchor.RoomLocalAabb(instance.transform, frr.bounds, out Vector3 rmin, out Vector3 rmax);
                float hole = rmin.x - lmax.x;
                sb.AppendLine($"door hole={hole:F3}");
                bool ok = Mathf.Abs(hole - 1.5f) < 0.05f;
                sb.AppendLine((ok ? "PASS" : "FAIL") + " door 1.5 centred");
                if (!ok) fails++;
            }
            else
            {
                sb.AppendLine("FAIL door pieces missing");
                fails++;
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        FlagGarage(sb);

        bool pass = fails == 0;
        sb.AppendLine($"fails={fails}");
        sb.AppendLine("PASS=" + (pass ? "True" : "False"));
        return sb.ToString();
    }

    static int PatchPrefab(GameObject root, StringBuilder sb)
    {
        int fails = 0;
        GridFootprint fp = root.GetComponent<GridFootprint>();
        if (fp != null)
            fp.sizeInCells = new Vector2Int(6, 6);

        foreach (var box in root.GetComponentsInChildren<BoxCollider>(true))
        {
            if (box.name == "InteriorTrigger")
            {
                box.size = new Vector3(Inner, 3f, Inner);
                box.center = new Vector3(Ext * 0.5f, 1.5f, Ext * 0.5f);
                continue;
            }
            MeshFilter mf = box.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
                continue;
            Bounds b = mf.sharedMesh.bounds;
            box.center = b.center;
            box.size = b.size;
            sb.AppendLine($"collider {box.name} size={box.size} center={box.center}");
        }
        if (FindNamed(root.transform, "RoomFloor") == null)
        {
            sb.AppendLine("FAIL RoomFloor missing after FBX import");
            fails++;
        }
        return fails;
    }

    static void FlagGarage(StringBuilder sb)
    {
        sb.AppendLine("--- garage (132) vs 5.4 interior — flag only ---");
        GameObject carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(F1CarConvention.PrefabPath);
        if (carPrefab == null)
        {
            sb.AppendLine("FLAG garage: F1_Car prefab missing at " + F1CarConvention.PrefabPath);
            return;
        }
        GameObject car = Object.Instantiate(carPrefab);
        try
        {
            car.transform.position = Vector3.zero;
            car.transform.rotation = Quaternion.Euler(0f, F1CarConvention.Yaw, 0f);
            Bounds cb = F1CarConvention.RendererBounds(car);
            sb.AppendLine($"F1_Car scale={car.transform.localScale} size={cb.size} (ARCHITECTURE 2.88 x 2.15 x 3.82)");
            sb.AppendLine($"plinth 2x3, 1m off door wall; interior 5.4x5.4");
            bool fitsBox = cb.size.x <= Inner + 1e-3f && cb.size.z <= Inner + 1e-3f;
            sb.AppendLine(fitsBox
                ? "garage chassis AABB fits inside 5.4x5.4 (not forced; plinth unchanged)"
                : $"FLAG garage chassis does NOT fit 5.4 interior (car {cb.size.x:F2}x{cb.size.z:F2} vs 5.4x5.4) — not scaled");

            // Plinth placed 1m off door (-Z) and centred in X inside the 5.4 box.
            const float inset = 1f;
            const float plinthX = 2f;
            const float plinthZ = 3f;
            float roomMinX = WallT;
            float roomMinZ = WallT;
            float plinthMinX = roomMinX + (Inner - plinthX) * 0.5f;
            float plinthMinZ = roomMinZ + inset;
            Vector3 plinthCentre = new Vector3(plinthMinX + plinthX * 0.5f, 0f, plinthMinZ + plinthZ * 0.5f);
            Vector3 carMin = plinthCentre + (cb.min - cb.center);
            Vector3 carMax = plinthCentre + (cb.max - cb.center);
            float iMinX = WallT;
            float iMaxX = Ext - WallT;
            float iMinZ = WallT;
            float iMaxZ = Ext - WallT;
            bool placedFits =
                carMin.x >= iMinX - 0.02f && carMax.x <= iMaxX + 0.02f
                && carMin.z >= iMinZ - 0.02f && carMax.z <= iMaxZ + 0.02f;
            sb.AppendLine($"placed-on-plinth xz=({carMin.x:F2},{carMax.x:F2}) ({carMin.z:F2},{carMax.z:F2})");
            sb.AppendLine(placedFits
                ? "garage on 2x3 plinth still inside 5.4 interior"
                : "FLAG garage on 2x3 plinth pokes the 5.4 interior — not scaled");
        }
        finally
        {
            Object.DestroyImmediate(car);
        }
    }

    static Transform FindNamed(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamed(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    static Bounds? Encapsulate(params Transform[] ts)
    {
        Bounds? b = null;
        foreach (var t in ts)
        {
            if (t == null)
                continue;
            var r = t.GetComponent<Renderer>();
            if (r == null)
                continue;
            if (b == null) b = r.bounds;
            else
            {
                Bounds bb = b.Value;
                bb.Encapsulate(r.bounds);
                b = bb;
            }
        }
        return b;
    }

    static bool Approx(float a, float b) => Mathf.Abs(a - b) < 0.02f;

    static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
}
