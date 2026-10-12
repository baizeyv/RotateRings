using System;
using System.IO;
using System.Linq;
using RotateRings;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(ProceduralRing2D))]
public sealed class ProceduralRing2DEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script", "Connections", "Settings");
        DrawSettings();
        bool changed = serializedObject.ApplyModifiedProperties();
        var ring = (ProceduralRing2D)target;
        int oldCount = ring.Connections == null ? 0 : ring.Connections.Length;
        int count = Mathf.Clamp(EditorGUILayout.IntField("连接点数量", oldCount), 0, 16);
        if (GUILayout.Button("添加连接点")) count = Mathf.Min(16, oldCount + 1);
        if (count != oldCount)
        {
            Undo.RecordObject(ring, "调整连接点数量");
            var next = new Ring2DConnection[count];
            for (int i = 0; i < count; i++) next[i] = i < oldCount ? ring.Connections[i] : new Ring2DConnection();
            ring.Connections = next; changed = true; serializedObject.Update();
        }
        var connections = serializedObject.FindProperty("Connections");
        for (int i = 0; i < connections.arraySize; i++)
        {
            var entry = connections.GetArrayElementAtIndex(i);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("连接点 " + i, EditorStyles.boldLabel);
            foreach (string name in new[] { "Name", "Direction", "ToCenter" }) EditorGUILayout.PropertyField(entry.FindPropertyRelative(name));
            string position = ring.Settings.Shape == Ring2DShape.Circle || ring.Settings.Shape == Ring2DShape.CShape ? "AngleDegrees" : "PathPosition";
            EditorGUILayout.PropertyField(entry.FindPropertyRelative(position), new GUIContent(position == "AngleDegrees" ? "位置角度" : "路径位置 (0–1)"));
            bool automatic = entry.FindPropertyRelative("Direction").enumValueIndex == (int)Ring2DConnectionDirection.Inward && entry.FindPropertyRelative("ToCenter").boolValue;
            using (new EditorGUI.DisabledScope(automatic))
            {
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("Length"), new GUIContent("连接杆长度"));
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("DirectionOffsetDegrees"), new GUIContent("方向偏转角"));
            }
            foreach (string name in new[] { "RodWidth", "ShowHead", "HeadWidth", "HeadDepth" }) EditorGUILayout.PropertyField(entry.FindPropertyRelative(name));
            if (GUILayout.Button("删除此连接点")) { connections.DeleteArrayElementAtIndex(i); changed = true; EditorGUILayout.EndVertical(); break; }
            EditorGUILayout.EndVertical();
        }
        changed |= serializedObject.ApplyModifiedProperties();
        if (changed) { ring.Rebuild(true); EditorUtility.SetDirty(ring); }
        EditorGUILayout.HelpBox("A 是完整胶囊环身图，B 是连接矩形图，可换成拆好的两个独立 Sprite。连接杆与头各用 B，纹理方向相差 90°。向内 ToCenter 默认把头中心放在环中心，关闭后使用 Length。门字环开口向下。Seven 是顶部横边 + 右侧竖边的 7 字直角环，PathPosition 从左上端沿横边走到右下端。", MessageType.Info);
        if (ring.Settings.Shape == Ring2DShape.ZShape)
            EditorGUILayout.HelpBox("ZShape 为阶梯折线：上段向右 → 中段向下 → 下段向右。TopWidth 和 BottomWidth 独立控制两段宽度，Height 是上下横边间距；中间竖段固定在局部 X=0。PathPosition 沿实际路径长度分配。", MessageType.Info);
        if (ring.Settings.Shape == Ring2DShape.Gate || ring.Settings.Shape == Ring2DShape.Seven || ring.Settings.Shape == Ring2DShape.ZShape)
            EditorGUILayout.HelpBox("CornerRadius 控制转角中心线半径，显示和碰撞都使用圆角；须大于 Thickness/2。门字最大为 min(Width/2, Height)，7 字为 min(Width, Height)，Z 字为 min(TopWidth, BottomWidth, Height/2)。", MessageType.Info);
        EditorGUILayout.HelpBox("BlockRotation 使用动态 Rigidbody2D 和实体碰撞，冻结中心位置，通过 FixedUpdate / MoveRotation 旋转；LockRotation 冻结整个环。TriggerDetection 只检测，不阻挡；None 关闭自动碰撞体。生成子对象由父级管理，应在此调整配置。", MessageType.Info);
        if (GUILayout.Button("使用原图 A / B 示例 Sprite"))
        { Undo.RecordObject(ring, "指定 A/B 图"); Ring2DDemoEditor.AssignSprites(ring); ring.Rebuild(true); EditorUtility.SetDirty(ring); }
        if (GUILayout.Button("刷新本体 / 连接点 / 碰撞体")) ring.Rebuild(true);
        using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("已保存本体 Mesh", ring.SavedMesh, typeof(Mesh), false);
        if (ring.SavedMesh != null)
        {
            EditorGUILayout.HelpBox(ring.UsesSavedMesh ? "正在使用已保存 Mesh；启用和刷新时跳过本体网格生成。" : "形状、A 图或生成规则已改变，正在使用程序化网格；请重新另存 Mesh。", ring.UsesSavedMesh ? MessageType.Info : MessageType.Warning);
            if (GUILayout.Button("恢复程序化本体 Mesh"))
            { Undo.RecordObject(ring, "恢复程序化网格"); ring.UseSavedMesh(null); ring.Rebuild(true); EditorUtility.SetDirty(ring); }
        }
        using (new EditorGUI.DisabledScope(Application.isPlaying))
            if (GUILayout.Button("另存本体 Mesh 并使用（Assets/Mesh）")) SaveMesh(ring);
    }

    /// <summary>Z 形只显示独立的两段宽度，兼容旧 Width/2 值；一次编辑即固定两段的独立配置。</summary>
    private void DrawSettings()
    {
        var settings = serializedObject.FindProperty("Settings");
        settings.isExpanded = EditorGUILayout.Foldout(settings.isExpanded, "Settings", true);
        if (!settings.isExpanded) return;
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(settings.FindPropertyRelative("Shape"));
        bool z = settings.FindPropertyRelative("Shape").enumValueIndex == (int)Ring2DShape.ZShape;
        if (z)
        {
            var top = settings.FindPropertyRelative("TopWidth"); var bottom = settings.FindPropertyRelative("BottomWidth");
            float legacy = settings.FindPropertyRelative("Width").floatValue / 2;
            EditorGUI.BeginChangeCheck();
            float upper = EditorGUILayout.FloatField("上横段宽度 (TopWidth)", top.floatValue == 0 ? legacy : top.floatValue);
            float lower = EditorGUILayout.FloatField("下横段宽度 (BottomWidth)", bottom.floatValue == 0 ? legacy : bottom.floatValue);
            if (EditorGUI.EndChangeCheck()) { top.floatValue = upper; bottom.floatValue = lower; }
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("Height"));
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("CornerRadius"));
        }
        else foreach (string name in new[] { "Radius", "Width", "Height", "CornerRadius" }) EditorGUILayout.PropertyField(settings.FindPropertyRelative(name));
        foreach (string name in new[] { "Thickness", "StartDegrees", "SweepDegrees", "Segments", "CapSegments" }) EditorGUILayout.PropertyField(settings.FindPropertyRelative(name));
        EditorGUI.indentLevel--;
    }

    /// <summary>另存一份资源并自动绑定；只保存 A 本体，B 子元素和物理配置仍由父级管理。</summary>
    private static void SaveMesh(ProceduralRing2D ring)
    {
        try
        {
            if (!AssetDatabase.IsValidFolder("Assets/Mesh")) AssetDatabase.CreateFolder("Assets", "Mesh");
            string name = ring.gameObject.name;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (string.IsNullOrWhiteSpace(name)) name = "Ring2D";
            string proposed = AssetDatabase.GenerateUniqueAssetPath("Assets/Mesh/" + name + ".asset");
            string path = EditorUtility.SaveFilePanelInProject("另存二维圆环本体 Mesh", Path.GetFileNameWithoutExtension(proposed), "asset", "保存后自动使用该资源，跳过本体网格生成。", "Assets/Mesh");
            if (string.IsNullOrEmpty(path)) return;
            Mesh saved = SaveMeshAsset(ring, path); EditorGUIUtility.PingObject(saved);
            Debug.Log("二维圆环本体 Mesh 已保存并使用：" + AssetDatabase.GetAssetPath(saved), ring);
        }
        catch (Exception e) { Debug.LogError("保存二维圆环 Mesh 失败：" + e.Message, ring); }
    }

    /// <summary>可测试的保存流程：验证刷新成功、克隆网格、清除临时标记、写资源并登记当前参数。</summary>
    public static Mesh SaveMeshAsset(ProceduralRing2D ring, string path)
    {
        if (Application.isPlaying) throw new InvalidOperationException("请退出 Play 模式后保存 Mesh。");
        if (!path.StartsWith("Assets/", StringComparison.Ordinal) || Path.GetExtension(path) != ".asset") throw new ArgumentException("Mesh 须保存为 Assets 下的 .asset 资源。");
        if (!ring.Rebuild()) throw new InvalidOperationException("当前参数无法生成网格，未保存旧结果。");
        Mesh source = ring.GetComponent<MeshFilter>().sharedMesh;
        if (source == null || source.vertexCount == 0) throw new InvalidOperationException("当前没有可保存的本体 Mesh。");
        path = AssetDatabase.GenerateUniqueAssetPath(path);
        Mesh saved = UnityEngine.Object.Instantiate(source); saved.name = Path.GetFileNameWithoutExtension(path); saved.hideFlags = HideFlags.None;
        AssetDatabase.CreateAsset(saved, path); AssetDatabase.SaveAssets();
        Undo.RecordObject(ring, "使用保存的二维圆环 Mesh"); ring.UseSavedMesh(saved); ring.Rebuild(true);
        EditorUtility.SetDirty(ring);
        if (ring.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(ring.gameObject.scene);
        return saved;
    }

    [MenuItem("GameObject/2D Object/程序化圆环（A/B）", false, 20)]
    private static void Create(MenuCommand command)
    {
        var go = new GameObject("Ring 2D"); GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "创建二维圆环"); var ring = go.AddComponent<ProceduralRing2D>();
        Ring2DDemoEditor.AssignSprites(ring); ring.Rebuild(true); Selection.activeGameObject = go;
    }
}

public static class Ring2DDemoEditor
{
    public const string ScenePath = "Assets/RotateRings/Ring2D/Scenes/Ring2DDemo.unity";
    private const string TexturePath = "Assets/RotateRings/Ring2D/Textures/ringextra_2d.png";
    public static void AssignSprites(ProceduralRing2D ring)
    {
        if (!File.Exists(TexturePath)) { File.Copy("Assets/RotateRings/Textures/ringextra.png", TexturePath); AssetDatabase.ImportAsset(TexturePath); }
        var all = AssetDatabase.LoadAllAssetsAtPath(TexturePath).OfType<Sprite>().ToArray();
        if (all.Length == 0)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None; importer.wrapMode = TextureWrapMode.Clamp; importer.spritePixelsPerUnit = 100;
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings); settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings);
#pragma warning disable 0618
            importer.spritesheet = new[] {
                new SpriteMetaData { name="RingBody_A", rect=new Rect(32,31,448,65), alignment=9, pivot=new Vector2(0.5f,0.5f), border=new Vector4(32.5f,0,32.5f,0) },
                new SpriteMetaData { name="RingConnector_B", rect=new Rect(576,31,128,65), alignment=9, pivot=new Vector2(0.5f,0.5f), border=new Vector4(8,8,8,8) }
            };
#pragma warning restore 0618
            importer.SaveAndReimport(); all = AssetDatabase.LoadAllAssetsAtPath(TexturePath).OfType<Sprite>().ToArray();
        }
        ring.BodySprite = all.First(s => s.name == "RingBody_A"); ring.ConnectionSprite = all.First(s => s.name == "RingConnector_B");
        const string materialPath = "Assets/RotateRings/Ring2D/Materials/Ring2D.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) { material = new Material(Shader.Find("RotateRings/Ring 2D")); AssetDatabase.CreateAsset(material, materialPath); }
        ring.RenderMaterial = material;
    }

    [MenuItem("Tools/Rotate Rings/打开 A-B 二维圆环与物理示例")]
    public static void Open()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath); else Build();
    }
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0,0,-20); camera.orthographic = true; camera.orthographicSize = 5.2f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.97f,0.90f,0.85f); camera.gameObject.AddComponent<AudioListener>();
        var circle = Add("Circle - inward to center", new Vector2(-4,2.35f), Ring2DShape.Circle, new Color(0.12f,0.75f,0.38f));
        var straight = Add("Straight - two end heads", new Vector2(0,2.35f), Ring2DShape.Straight, new Color(0.22f,0.65f,1));
        straight.Connections = new[] { new Ring2DConnection { PathPosition=0,Direction=Ring2DConnectionDirection.Outward,Length=0.4f }, new Ring2DConnection { PathPosition=1,Direction=Ring2DConnectionDirection.Outward,Length=0.4f } }; straight.Rebuild();
        var arc = Add("C Ring - outward head", new Vector2(4,2.35f), Ring2DShape.CShape, new Color(0.68f,0.20f,0.95f));
        arc.Connections = new[] { new Ring2DConnection { AngleDegrees=90,Direction=Ring2DConnectionDirection.Outward,Length=0.42f } }; arc.Rebuild();
        var gate = Add("Gate - inward center", new Vector2(-4,-1.65f), Ring2DShape.Gate, new Color(1,0.25f,0.18f));
        gate.Connections[0].PathPosition = 0.5f; gate.Rebuild();
        var multi = Add("Circle - long / inward / outward", new Vector2(0,-1.65f), Ring2DShape.Circle, new Color(0.05f,0.65f,0.68f));
        multi.Connections = new[] { new Ring2DConnection {AngleDegrees=90}, new Ring2DConnection {AngleDegrees=0,Direction=Ring2DConnectionDirection.Outward,Length=0.7f},
            new Ring2DConnection {AngleDegrees=210,ToCenter=false,Length=0.28f,ShowHead=false} }; multi.Rebuild();
        var inner = Add("Physics Inner", new Vector2(4,-1.65f), Ring2DShape.Circle, new Color(1,0.5f,0.07f));
        inner.Settings.Radius=0.72f; inner.Settings.Thickness=0.12f;
        inner.Connections=new[] {new Ring2DConnection {AngleDegrees=90,Direction=Ring2DConnectionDirection.Outward,Length=0.16f,RodWidth=0.09f,HeadWidth=0.28f,HeadDepth=0.16f}}; inner.Rebuild();
        var outer = Add("Physics Outer", new Vector2(4,-1.65f), Ring2DShape.CShape, new Color(0.20f,0.50f,0.95f));
        outer.Settings.Thickness=0.12f; outer.Settings.StartDegrees=125; outer.Settings.SweepDegrees=290;
        outer.Connections=new Ring2DConnection[0]; outer.LockRotation=true; outer.SortingOrder=1; outer.Rebuild();
        var seven = Add("Seven - right angle", new Vector2(0,-3.8f), Ring2DShape.Seven, new Color(0.95f,0.64f,0.05f));
        seven.Settings.Width=1.8f; seven.Settings.Height=1.3f; seven.Settings.Thickness=0.18f;
        seven.Connections[0].PathPosition=seven.Settings.Width/2/(seven.Settings.Width+seven.Settings.Height); seven.Rebuild();
        var z = Add("Z - right angle step", new Vector2(4,-3.8f), Ring2DShape.ZShape, new Color(0.95f,0.35f,0.64f));
        z.Settings.TopWidth=0.7f; z.Settings.BottomWidth=1.1f; z.Settings.Height=1.3f; z.Settings.Thickness=0.18f;
        z.Connections[0].PathPosition=(float)(z.Settings.TopWidth/2/Ring2DGeometry.PathLength(z.Settings)); z.Rebuild();
        var hud = new GameObject("2D Demo Instructions").AddComponent<Ring2DDemoLabels>(); hud.Outer=outer;
        EditorSceneManager.SaveScene(scene,ScenePath); AssetDatabase.SaveAssets();
    }
    private static ProceduralRing2D Add(string name,Vector2 point,Ring2DShape shape,Color color)
    {
        var ring=new GameObject(name).AddComponent<ProceduralRing2D>(); ring.transform.position=new Vector3(point.x,point.y,0);
        ring.Settings.Shape=shape; ring.Color=color; AssignSprites(ring); ring.Rebuild(); return ring;
    }
}
