using System.Globalization;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ticket 150/155 — CustomEditor for ObjectProperties. Surfaces Approve as a
/// banner+button, scale/pivot overrides with live scene preview, and blank
/// judgment fields (149 + 155). Approval is a status flag, not a lock.
/// </summary>
[CustomEditor(typeof(ObjectProperties))]
public class ObjectPropertiesEditor : Editor
{
    SerializedProperty objectName;
    SerializedProperty folder;
    SerializedProperty kind;
    SerializedProperty pickupable;
    SerializedProperty approved;
    SerializedProperty weight;
    SerializedProperty price;
    SerializedProperty category;
    SerializedProperty stackable;
    SerializedProperty sizeTier;
    SerializedProperty physicsGravity;
    SerializedProperty scaleMultiplier;
    SerializedProperty pivotOffset;
    SerializedProperty interaction;
    SerializedProperty placementMethod;
    SerializedProperty nodeCompat;
    SerializedProperty animationClips;
    SerializedProperty animatorController;
    SerializedProperty sfx;
    SerializedProperty uniqueProperties;

    static readonly Color PendingBg = new Color(0.85f, 0.55f, 0.12f, 1f);
    static readonly Color ApprovedBg = new Color(0.18f, 0.55f, 0.28f, 1f);

    static readonly ObjectNodeCompat[] NodeCompatFlags =
    {
        ObjectNodeCompat.Counter,
        ObjectNodeCompat.Keep,
        ObjectNodeCompat.Bin,
        ObjectNodeCompat.Storage,
        ObjectNodeCompat.Any
    };

    void OnEnable()
    {
        objectName = serializedObject.FindProperty("objectName");
        folder = serializedObject.FindProperty("folder");
        kind = serializedObject.FindProperty("kind");
        pickupable = serializedObject.FindProperty("pickupable");
        approved = serializedObject.FindProperty("approved");
        weight = serializedObject.FindProperty("weight");
        price = serializedObject.FindProperty("price");
        category = serializedObject.FindProperty("category");
        stackable = serializedObject.FindProperty("stackable");
        sizeTier = serializedObject.FindProperty("sizeTier");
        physicsGravity = serializedObject.FindProperty("physicsGravity");
        scaleMultiplier = serializedObject.FindProperty("scaleMultiplier");
        pivotOffset = serializedObject.FindProperty("pivotOffset");
        interaction = serializedObject.FindProperty("interaction");
        placementMethod = serializedObject.FindProperty("placementMethod");
        nodeCompat = serializedObject.FindProperty("nodeCompat");
        animationClips = serializedObject.FindProperty("animationClips");
        animatorController = serializedObject.FindProperty("animatorController");
        sfx = serializedObject.FindProperty("sfx");
        uniqueProperties = serializedObject.FindProperty("uniqueProperties");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawStatusBanner();
        EditorGUILayout.Space(8);

        DrawIdentity();
        EditorGUILayout.Space(6);

        bool transformChanged = DrawTransformOverrides();
        EditorGUILayout.Space(6);

        DrawJudgment();

        serializedObject.ApplyModifiedProperties();

        if (transformChanged)
        {
            var props = (ObjectProperties)target;
            if (ObjectPropertiesPreview.FindExistingRoot(props) != null)
                ObjectPropertiesPreview.ApplyTransforms(props);
            else
                ObjectPropertiesPreview.EnsurePreview(props);
            PendingReviewWindow.RequestRefresh();
        }
    }

    void DrawStatusBanner()
    {
        bool isApproved = approved.boolValue;
        Rect rect = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, isApproved ? ApprovedBg : PendingBg);

        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 14,
            normal = { textColor = Color.white }
        };
        GUI.Label(rect, isApproved ? "APPROVED" : "PENDING REVIEW", style);

        EditorGUILayout.Space(4);
        string btn = isApproved ? "Unapprove" : "Approve";
        GUI.backgroundColor = isApproved ? new Color(0.9f, 0.7f, 0.4f) : new Color(0.45f, 0.85f, 0.55f);
        if (GUILayout.Button(btn, GUILayout.Height(32)))
        {
            bool nowApproved = !isApproved;
            approved.boolValue = nowApproved;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);

            var props = (ObjectProperties)target;
            try
            {
                if (nowApproved)
                    ObjectApprovedPrefabBuilder.Build(props);
                else
                    ObjectApprovedPrefabBuilder.TearDown(props);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[ObjectPropertiesEditor] prefab " + (nowApproved ? "build" : "teardown")
                    + " failed: " + e.Message);
                // Roll back approve flag if build failed.
                if (nowApproved)
                {
                    approved.boolValue = false;
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(target);
                }
            }

            PendingReviewWindow.RequestRefresh();
            if (PendingReviewWindow.HasOpenInstance)
                PendingReviewWindow.RepaintOpen();
        }
        GUI.backgroundColor = Color.white;
    }

    void DrawIdentity()
    {
        EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.PropertyField(objectName);
            EditorGUILayout.PropertyField(folder);
            EditorGUILayout.PropertyField(kind);
            EditorGUILayout.PropertyField(pickupable);
        }
    }

    bool DrawTransformOverrides()
    {
        EditorGUILayout.LabelField("Transform overrides", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        float scale = Mathf.Max(0.01f, scaleMultiplier.floatValue);
        scale = EditorGUILayout.FloatField(
            new GUIContent("Scale multiplier (1 = 1m)", "Uniform wrapper localScale. FBX / importer untouched."),
            scale);
        scaleMultiplier.floatValue = Mathf.Max(0.01f, scale);
        EditorGUILayout.PropertyField(
            pivotOffset,
            new GUIContent("Pivot Offset", "Mesh-child localPosition under the scaled wrapper."));
        bool changed = EditorGUI.EndChangeCheck();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Apply / Refresh Preview", GUILayout.Height(24)))
        {
            serializedObject.ApplyModifiedProperties();
            ObjectPropertiesPreview.EnsurePreview((ObjectProperties)target);
            changed = true;
        }
        if (GUILayout.Button("Clear Preview", GUILayout.Height(24)))
            ObjectPropertiesPreview.ClearAllPreviews();
        EditorGUILayout.EndHorizontal();

        return changed;
    }

    void DrawJudgment()
    {
        EditorGUILayout.LabelField("Judgment (blank = unset)", EditorStyles.boldLabel);
        weight.floatValue = DrawOptionalFloat("Weight", weight.floatValue);
        price.floatValue = DrawOptionalFloat("Price", price.floatValue);

        string cat = category.stringValue ?? "";
        string newCat = EditorGUILayout.TextField("Category", cat);
        category.stringValue = newCat ?? "";

        stackable.enumValueIndex = DrawOptionalEnum("Stackable", stackable.enumValueIndex, stackable.enumDisplayNames);
        sizeTier.enumValueIndex = DrawOptionalEnum("Size Tier", sizeTier.enumValueIndex, sizeTier.enumDisplayNames);
        physicsGravity.enumValueIndex = DrawOptionalEnum("Physics Gravity", physicsGravity.enumValueIndex, physicsGravity.enumDisplayNames);

        EditorGUILayout.Space(4);
        interaction.enumValueIndex = DrawOptionalEnum(
            "Interaction", interaction.enumValueIndex, interaction.enumDisplayNames);
        placementMethod.enumValueIndex = DrawOptionalEnum(
            "Placement Method", placementMethod.enumValueIndex, placementMethod.enumDisplayNames);

        DrawNodeCompat();

        EditorGUILayout.Space(2);
        EditorGUILayout.PropertyField(animationClips, new GUIContent("Animation Clips"), true);
        EditorGUILayout.PropertyField(animatorController, new GUIContent("Animator Controller"));

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("SFX set (blank = unset)", EditorStyles.miniBoldLabel);
        if (sfx != null)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(sfx.FindPropertyRelative("pickup"));
            EditorGUILayout.PropertyField(sfx.FindPropertyRelative("drop"));
            EditorGUILayout.PropertyField(sfx.FindPropertyRelative("use"));
            EditorGUILayout.PropertyField(sfx.FindPropertyRelative("open"));
            EditorGUILayout.PropertyField(sfx.FindPropertyRelative("custom"));
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.PropertyField(uniqueProperties, new GUIContent("Unique Properties"), true);
    }

    void DrawNodeCompat()
    {
        EditorGUILayout.LabelField(
            new GUIContent("Node Compat", "Bitmask for Counter/Keep/Bin/Storage. Blank = unset."),
            EditorStyles.label);

        int mask = nodeCompat.intValue;
        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < NodeCompatFlags.Length; i++)
        {
            ObjectNodeCompat flag = NodeCompatFlags[i];
            bool on = (mask & (int)flag) != 0;
            bool next = GUILayout.Toggle(on, flag.ToString(), EditorStyles.miniButton);
            if (next != on)
            {
                if (next)
                    mask |= (int)flag;
                else
                    mask &= ~(int)flag;
            }
        }
        EditorGUILayout.EndHorizontal();

        if (mask == 0)
            EditorGUILayout.LabelField(" ", "— (unset)", EditorStyles.miniLabel);

        nodeCompat.intValue = mask;
    }

    static float DrawOptionalFloat(string label, float value)
    {
        string text = Mathf.Abs(value) < 0.00001f
            ? ""
            : value.ToString(CultureInfo.InvariantCulture);
        string newText = EditorGUILayout.TextField(label, text);
        if (string.IsNullOrWhiteSpace(newText))
            return 0f;
        if (float.TryParse(newText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            return parsed;
        return value;
    }

    static int DrawOptionalEnum(string label, int index, string[] names)
    {
        string[] display = new string[names.Length];
        for (int i = 0; i < names.Length; i++)
            display[i] = i == 0 ? "—" : names[i];
        return EditorGUILayout.Popup(label, Mathf.Clamp(index, 0, display.Length - 1), display);
    }
}
