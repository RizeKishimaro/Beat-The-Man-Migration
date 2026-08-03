using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds Item_Crate_Base.prefab from the FBX with Object physics defaults
/// (Rigidbody + PhysicsMaterial + PushableItem) and validates multi-instance use.
/// Auto-runs when Temp/build_crate_prefab.flag is present (agent self-check path).
/// </summary>
[InitializeOnLoad]
public static class ItemCratePrefabBuilder
{
    const string FbxPath = "Assets/Art/Items/Item_Crate_Base.fbx";
    const string PrefabPath = "Assets/Art/Items/Item_Crate_Base.prefab";
    const string PhysicsMatPath = "Assets/Art/Items/Item_Cardboard.physicMaterial";
    const string FlagPath = "Temp/build_crate_prefab.flag";
    const string VerifyPath = "Temp/agent_verify_crate_prefab.txt";
    const string FeelPath = "Temp/agent_verify_crate_physics.txt";

    const int BuilderRevision = 10; // single scene crate + carry hardening

    // Tuned after feel probe v6 (BOXY slide OK, tip 94° still high).
    const float Mass = 3.5f;
    const float LinearDamping = 2.0f;
    const float AngularDamping = 2.4f;
    const float FeelPushForce = 28f;
    const float FeelSpeedBoost = 0.75f;
    const float FeelMaxPushForce = 48f;

    static bool waitingForEditMode;

    static ItemCratePrefabBuilder()
    {
        EditorApplication.delayCall += TryAutoBuild;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [InitializeOnLoadMethod]
    static void Boot()
    {
        EditorApplication.delayCall += TryAutoBuild;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode && waitingForEditMode)
        {
            waitingForEditMode = false;
            EditorApplication.delayCall += TryAutoBuild;
        }
    }

    static void TryAutoBuild()
    {
        string flag = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FlagPath));
        if (!File.Exists(flag))
            return;

        // Prefab save / DestroyImmediate need Edit Mode.
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.Log("[ItemCratePrefabBuilder] Exiting Play Mode before build...");
            waitingForEditMode = true;
            EditorApplication.isPlaying = false;
            return;
        }

        try
        {
            File.Delete(flag);
            string report = BuildAndValidate(placeSceneInstances: true);
            string verify = Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath));
            File.WriteAllText(verify, report);
            Debug.Log("[ItemCratePrefabBuilder]\n" + report);

            string feel = SimulateFeel();
            string feelVerify = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FeelPath));
            File.WriteAllText(feelVerify, feel);
            Debug.Log("[ItemCratePrefabBuilder.SimulateFeel]\n" + feel);

            string carryReport = VerifyOverheadCarry();
            File.WriteAllText(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp/agent_verify_overhead_carry.txt")),
                carryReport);
            Debug.Log("[ItemCratePrefabBuilder.VerifyOverheadCarry]\n" + carryReport);
        }
        catch (System.Exception e)
        {
            string verify = Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath));
            File.WriteAllText(verify, "FAIL: " + e);
            Debug.LogError("[ItemCratePrefabBuilder] " + e);
        }
    }

    [MenuItem("Tools/For Cursor/Build Item_Crate_Base Prefab")]
    public static void BuildMenu()
    {
        string report = BuildAndValidate(placeSceneInstances: true);
        string verify = Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath));
        File.WriteAllText(verify, report);
        Debug.Log("[ItemCratePrefabBuilder]\n" + report);
        if (!Application.isBatchMode)
            EditorUtility.DisplayDialog("Item_Crate_Base Prefab", report, "OK");
    }

    public static void BuildForBatch()
    {
        string report = BuildAndValidate(placeSceneInstances: false);
        string verify = Path.GetFullPath(Path.Combine(Application.dataPath, "..", VerifyPath));
        File.WriteAllText(verify, report);
        Debug.Log("[ItemCratePrefabBuilder]\n" + report);
        string feel = SimulateFeel();
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", FeelPath)), feel);
        if (report.Contains("FAIL") || !report.Contains("OK") || !feel.Contains("OK"))
            EditorApplication.Exit(1);
    }

    public static void SimulateFeelForBatch()
    {
        BuildForBatch();
    }

    public static string BuildAndValidate(bool placeSceneInstances)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("BuilderRevision=" + BuilderRevision);

        GameObject fbxRoot = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbxRoot == null)
            throw new System.Exception("FBX not found: " + FbxPath);

        PhysicsMaterial physMat = EnsurePhysicsMaterial();
        sb.AppendLine("physicsMat=" + PhysicsMatPath);

        GameObject sceneInstance = (GameObject)PrefabUtility.InstantiatePrefab(fbxRoot);
        sceneInstance.name = "Item_Crate_Base";
        sceneInstance.transform.position = new Vector3(0.9f, 0f, -1.2f);
        sceneInstance.transform.rotation = Quaternion.identity;
        sceneInstance.transform.localScale = Vector3.one;

        BoxCollider box = sceneInstance.GetComponent<BoxCollider>();
        if (box == null)
        {
            box = sceneInstance.AddComponent<BoxCollider>();
            box.size = new Vector3(0.7f, 0.7f, 0.7f);
            box.center = new Vector3(0f, 0.35f, 0f);
        }
        box.sharedMaterial = physMat;

        Rigidbody rb = sceneInstance.GetComponent<Rigidbody>();
        if (rb == null)
            rb = sceneInstance.AddComponent<Rigidbody>();
        rb.mass = Mass;
        rb.linearDamping = LinearDamping;
        rb.angularDamping = AngularDamping;
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        PushableItem pushable = sceneInstance.GetComponent<PushableItem>();
        if (pushable == null)
            pushable = sceneInstance.AddComponent<PushableItem>();
        pushable.mass = Mass;
        pushable.linearDamping = LinearDamping;
        pushable.angularDamping = AngularDamping;
        pushable.pushMultiplier = 1f;
        pushable.ApplyDefaults();

        if (sceneInstance.GetComponent<CarryableItem>() == null)
            sceneInstance.AddComponent<CarryableItem>();

        foreach (var rend in sceneInstance.GetComponentsInChildren<Renderer>(true))
        {
            if (rend.sharedMaterial == null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.color = new Color(0.55f, 0.45f, 0.25f, 1f);
                string matPath = "Assets/Art/Items/Item_Crate_Base_Mat.mat";
                AssetDatabase.CreateAsset(mat, matPath);
                rend.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            }
        }

        if (File.Exists(PrefabPath))
            AssetDatabase.DeleteAsset(PrefabPath);

        GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(sceneInstance, PrefabPath, out bool success);
        if (!success || prefabAsset == null)
            throw new System.Exception("SaveAsPrefabAsset failed: " + PrefabPath);

        sb.AppendLine("PREFAB_CREATED " + PrefabPath);

        var prefabFilters = prefabAsset.GetComponentsInChildren<MeshFilter>(true);
        sb.AppendLine($"prefab_meshFilters={prefabFilters.Length}");
        for (int i = 0; i < prefabFilters.Length; i++)
        {
            Mesh mesh = prefabFilters[i].sharedMesh;
            sb.AppendLine($"mesh[{i}]={(mesh != null ? mesh.name : "NULL")}");
            if (mesh == null)
                throw new System.Exception("Prefab MeshFilter has null mesh — broken FBX link");
        }

        Rigidbody prefabRb = prefabAsset.GetComponent<Rigidbody>();
        PushableItem prefabPush = prefabAsset.GetComponent<PushableItem>();
        CarryableItem prefabCarry = prefabAsset.GetComponent<CarryableItem>();
        BoxCollider prefabBox = prefabAsset.GetComponent<BoxCollider>();
        if (prefabRb == null || prefabPush == null || prefabBox == null || prefabCarry == null)
            throw new System.Exception("Prefab missing Rigidbody/PushableItem/CarryableItem/BoxCollider");
        if (prefabBox.sharedMaterial == null)
            throw new System.Exception("Prefab BoxCollider missing PhysicsMaterial");

        sb.AppendLine(
            $"physics mass={prefabRb.mass} linearDamping={prefabRb.linearDamping} " +
            $"angularDamping={prefabRb.angularDamping} gravity={prefabRb.useGravity} " +
            $"physMat={prefabBox.sharedMaterial.name} pushable=True carryable=True");

        Object.DestroyImmediate(sceneInstance);
        sb.AppendLine("CREATION_INSTANCE_DELETED");

        if (placeSceneInstances && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            // Wipe every Item_Crate_Base (including stale/broken PrefabInstances).
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (t != null && t.name == "Item_Crate_Base")
                    Object.DestroyImmediate(t.gameObject);
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            // Validate multi-instance capability without leaving extras in the scene.
            for (int i = 0; i < 2; i++)
            {
                GameObject probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                probe.name = "Item_Crate_Base_MultiProbe";
                bool ok = PrefabUtility.IsPartOfPrefabInstance(probe)
                          && probe.GetComponentInChildren<MeshFilter>()?.sharedMesh != null
                          && probe.GetComponent<Rigidbody>() != null
                          && probe.GetComponent<CarryableItem>() != null;
                sb.AppendLine($"MULTI_PROBE_{i} ok={ok}");
                if (!ok)
                    throw new System.Exception($"Multi-instance probe {i} failed");
                Object.DestroyImmediate(probe);
            }

            // Leave exactly ONE healthy scene instance for the test spike.
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.name = "Item_Crate_Base";
            inst.transform.position = new Vector3(0.9f, 0f, -1.2f);
            inst.transform.rotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;
            sb.AppendLine(
                $"SCENE_SINGLE pos={inst.transform.position} " +
                $"isPrefabInstance={PrefabUtility.IsPartOfPrefabInstance(inst)} " +
                $"carryable={inst.GetComponent<CarryableItem>() != null}");

            EditorSceneManagerMarkDirtySafe(sb);
        }
        else
        {
            sb.AppendLine("SKIP_SCENE_INSTANCES");
        }

        sb.AppendLine("OK");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return sb.ToString();
    }

    static void EditorSceneManagerMarkDirtySafe(System.Text.StringBuilder sb)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            sb.AppendLine("SKIP_MARK_DIRTY (play mode)");
            return;
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    static string SimulateFeel()
    {
        var sb = new System.Text.StringBuilder();
        SimulationMode previousMode = Physics.simulationMode;
        GameObject ground = null;
        GameObject crate = null;
        try
        {
            Physics.simulationMode = SimulationMode.Script;

            PhysicsMaterial physMat = EnsurePhysicsMaterial();
            ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "FeelProbe_Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(2f, 1f, 2f);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                throw new System.Exception("Prefab missing for feel probe: " + PrefabPath);

            crate = Object.Instantiate(prefab);
            crate.name = "FeelProbe_Crate";
            crate.hideFlags = HideFlags.HideAndDontSave;
            ground.hideFlags = HideFlags.HideAndDontSave;
            crate.transform.position = new Vector3(0f, 0.02f, 0f);

            Rigidbody rb = crate.GetComponent<Rigidbody>();
            if (rb == null)
                throw new System.Exception("Feel probe crate missing Rigidbody");

            PushableItem pushable = crate.GetComponent<PushableItem>();
            if (pushable != null)
                pushable.ApplyDefaults();

            Physics.SyncTransforms();
            for (int i = 0; i < 60; i++)
                Physics.Simulate(1f / 50f);

            Vector3 startPos = rb.position;
            Quaternion startRot = rb.rotation;
            float maxSpeed = 0f;
            float tipDegrees = 0f;
            const float pushForce = FeelPushForce;
            float speedFactor = 1f + FeelSpeedBoost;
            float force = Mathf.Min(pushForce * speedFactor, FeelMaxPushForce);

            for (int i = 0; i < 23; i++)
            {
                rb.AddForce(Vector3.right * force, ForceMode.Force);
                Physics.Simulate(1f / 50f);
                float planar = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z).magnitude;
                if (planar > maxSpeed) maxSpeed = planar;
                float tip = Quaternion.Angle(startRot, rb.rotation);
                if (tip > tipDegrees) tipDegrees = tip;
            }

            for (int i = 0; i < 40; i++)
            {
                Physics.Simulate(1f / 50f);
                float planar = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z).magnitude;
                if (planar > maxSpeed) maxSpeed = planar;
                float tip = Quaternion.Angle(startRot, rb.rotation);
                if (tip > tipDegrees) tipDegrees = tip;
            }

            float slide = new Vector3(rb.position.x - startPos.x, 0f, rb.position.z - startPos.z).magnitude;
            string feel;
            if (maxSpeed > 6.5f || slide > 3.5f)
                feel = "FLOATY — too much slide/speed; raise mass/damping or lower push";
            else if (maxSpeed < 0.6f && slide < 0.15f)
                feel = "STUCK — too heavy/friction; lower mass/damping or raise push";
            else if (tipDegrees > 50f && slide < 0.55f)
                feel = "TIPPY — tips more than slides; raise angularDamping or lower push";
            else if (maxSpeed >= 0.8f && maxSpeed <= 4.0f && slide >= 0.2f && slide <= 1.8f && tipDegrees <= 50f)
                feel = "BOXY — cardboard shove/slide with light tip tendency (target)";
            else
                feel = $"MIXED — speed={maxSpeed:F2} slide={slide:F2} tip={tipDegrees:F1} (review)";

            sb.AppendLine("BuilderRevision=" + BuilderRevision);
            sb.AppendLine("--- FEEL ---");
            sb.AppendLine($"mass={rb.mass} linearDamping={rb.linearDamping} angularDamping={rb.angularDamping}");
            sb.AppendLine($"pushForce={pushForce} speedBoost={FeelSpeedBoost} maxPushForce={FeelMaxPushForce} mode=Force");
            sb.AppendLine($"maxPlanarSpeed={maxSpeed:F3}");
            sb.AppendLine($"slideDistance={slide:F3}");
            sb.AppendLine($"maxTipDegrees={tipDegrees:F1}");
            sb.AppendLine($"yDelta={rb.position.y - startPos.y:F3}");
            sb.AppendLine($"physMat={(physMat != null ? physMat.name : "NULL")}");
            sb.AppendLine($"hasPushable={pushable != null}");
            sb.AppendLine("feel=" + feel);
            sb.AppendLine("OK");
            return sb.ToString();
        }
        finally
        {
            Physics.simulationMode = previousMode;
            if (crate != null) Object.DestroyImmediate(crate);
            if (ground != null) Object.DestroyImmediate(ground);
        }
    }

    static string VerifyOverheadCarry()
    {
        const float DoorHeight = 2.18f;
        const float SocketHeight = 1.45f;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("BuilderRevision=" + BuilderRevision);
        sb.AppendLine("--- OVERHEAD CARRY ---");

        var playerGo = new GameObject("Verify_Player");
        playerGo.AddComponent<CharacterController>();
        var carry = playerGo.AddComponent<PlayerOverheadCarry>();

        var crateGo = new GameObject("Verify_Crate");
        crateGo.transform.position = new Vector3(1f, 0f, 0f);
        var box = crateGo.AddComponent<BoxCollider>();
        box.size = new Vector3(0.7f, 0.7f, 0.7f);
        box.center = new Vector3(0f, 0.35f, 0f);
        crateGo.AddComponent<Rigidbody>();
        var carryable = crateGo.AddComponent<CarryableItem>();

        Transform socket = carry.CarrySocket;
        float socketY = socket != null ? socket.localPosition.y : -1f;
        sb.AppendLine($"socketLocalY={socketY:F3} (expect {SocketHeight:F2})");

        bool colliderBefore = box.enabled;
        carryable.PickUp(socket);
        bool colliderWhile = box.enabled;
        bool kinematicWhile = carryable.Body != null && carryable.Body.isKinematic;
        float carriedTop = SocketHeight + box.size.y;
        float worldTop = crateGo.transform.position.y + box.size.y;
        sb.AppendLine($"colliderBefore={colliderBefore} colliderWhileCarried={colliderWhile} kinematic={kinematicWhile}");
        sb.AppendLine($"carriedTopEstimate={carriedTop:F3} worldTopY={worldTop:F3} door={DoorHeight:F2} clearsDoor={carriedTop < DoorHeight && worldTop < DoorHeight}");
        sb.AppendLine($"pushSkipWhileCarried={carryable.IsCarried}");

        carryable.Drop(new Vector3(2f, 0f, 0f), Quaternion.identity);
        sb.AppendLine($"colliderAfterDrop={box.enabled} kinematicAfterDrop={carryable.Body.isKinematic} isCarried={carryable.IsCarried}");

        // Prefab has CarryableItem?
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        bool prefabCarry = prefab != null && prefab.GetComponent<CarryableItem>() != null;
        sb.AppendLine($"prefabHasCarryableItem={prefabCarry}");

        bool ok =
            Mathf.Abs(socketY - SocketHeight) < 0.001f
            && colliderBefore && !colliderWhile && kinematicWhile
            && carriedTop < DoorHeight && worldTop < DoorHeight
            && carryable.IsCarried == false
            && box.enabled
            && !carryable.Body.isKinematic
            && prefabCarry;

        sb.AppendLine(ok ? "OK" : "FAIL_CHECKS");
        Object.DestroyImmediate(playerGo);
        Object.DestroyImmediate(crateGo);
        return sb.ToString();
    }

    static PhysicsMaterial EnsurePhysicsMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PhysicsMatPath);
        if (existing != null)
        {
            existing.dynamicFriction = 0.55f;
            existing.staticFriction = 0.65f;
            existing.bounciness = 0.04f;
            existing.frictionCombine = PhysicsMaterialCombine.Average;
            existing.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        var mat = new PhysicsMaterial("Item_Cardboard")
        {
            dynamicFriction = 0.55f,
            staticFriction = 0.65f,
            bounciness = 0.04f,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };
        AssetDatabase.CreateAsset(mat, PhysicsMatPath);
        return AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PhysicsMatPath);
    }
}
