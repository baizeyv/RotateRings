using System;
using System.IO;
using System.Linq;
using RotateRings;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;

[CustomEditor(typeof(ProceduralRing2D))]
public sealed class ProceduralRing2DEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script", "Connections", "Settings", "MovementAxis", "Clasps");
        DrawSettings();
        var shape = (Ring2DShape)serializedObject.FindProperty("Settings").FindPropertyRelative("Shape").enumValueIndex;
        if (shape == Ring2DShape.Straight || shape == Ring2DShape.Gate || shape == Ring2DShape.Seven || shape == Ring2DShape.ZShape)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Clasps"), new GUIContent("固定 C 卡扣"), true);
            if (GUILayout.Button(shape == Ring2DShape.Gate ? "添加左右两个竖向 C 卡扣" : "添加固定 C 卡扣"))
            {
                serializedObject.ApplyModifiedProperties();
                if(shape == Ring2DShape.Gate) CreateGateClasps((ProceduralRing2D)target);
                else CreateClasp((ProceduralRing2D)target);
                serializedObject.Update();
            }
            var current = (ProceduralRing2D)target;
            bool hasClasp = current.Clasps != null && current.Clasps.Any(c => c != null && c.isActiveAndEnabled);
            using (new EditorGUI.DisabledScope(hasClasp)) EditorGUILayout.PropertyField(serializedObject.FindProperty("MovementAxis"), new GUIContent("无卡扣时移动方向"));
            if (current.Clasps != null) foreach (var clasp in current.Clasps) if (clasp != null) DrawClasp(clasp);
            Vector2 axis;
            if (!current.TryMovementDirection(out axis)) EditorGUILayout.HelpBox("卡扣尺寸无效或通道不平行，物件无法移动。", MessageType.Error);
            else EditorGUILayout.LabelField("实际世界移动方向", axis.ToString("F3"));
            EditorGUILayout.HelpBox("C 卡扣为场景中独立固定对象，物件从中滑过。通道角度 0° 向右、90° 向上；多个卡扣必须平行。通道宽度须大于环身 Thickness，圆角或连接头碰到轨道时会停止。移除数组引用不会删除场景卡扣，它仍是几何障碍；不再使用时请删除或禁用卡扣对象。", MessageType.Info);
        }
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
            if (entry.FindPropertyRelative("ShowHead").boolValue)
            {
                var passage = entry.FindPropertyRelative("AllowRingBodyPassage");
                EditorGUILayout.PropertyField(passage, new GUIContent("允许圆环身沿连接头穿过"));
                if (passage.boolValue) EditorGUILayout.Slider(entry.FindPropertyRelative("PassageAngleDegrees"), 0, 90, new GUIContent("通道方向允许夹角"));
            }
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
        EditorGUILayout.HelpBox("圆/C 环拖动旋转；直线、门形、7/Z 形沿配置轴拖动移动。连接头可作为导向卡扣：圆/C 环身与头横轴的夹角在允许范围内时可滑过，连接杆仍阻挡。LockRotation 只锁旋转。None 和旧 TriggerDetection 不阻挡，也不产生物理触发事件。请使用 XY 平面和均匀缩放，实体阻挡部分初始不要穿插。", MessageType.Info);
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

    private static void DrawClasp(Ring2DClasp clasp)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(clasp.name, EditorStyles.boldLabel);
        Vector3 previous = clasp.transform.position;
        EditorGUI.BeginChangeCheck();
        Vector2 position = EditorGUILayout.Vector2Field("固定位置（世界）", previous);
        float angle = EditorGUILayout.FloatField("通道角度（世界）", Mathf.Atan2(clasp.Direction.y, clasp.Direction.x)*Mathf.Rad2Deg);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(clasp.transform,"调整固定卡扣");
            clasp.transform.position = new Vector3(position.x,position.y,previous.z);
            clasp.Refresh();
            clasp.transform.rotation = Quaternion.Euler(0,0,angle);
        }
        var data = new SerializedObject(clasp); data.Update();
        foreach(string name in new[]{"ClaspSprite","RenderMaterial","Color","Width","Length","ChannelWidth","SortingLayer","SortingOrder"}) EditorGUILayout.PropertyField(data.FindProperty(name));
        if(data.ApplyModifiedProperties()) {clasp.Refresh();EditorUtility.SetDirty(clasp);}
        if(!clasp.Valid)EditorGUILayout.HelpBox("Width 必须大于 ChannelWidth，所有尺寸须为正数。",MessageType.Error);
        if(GUILayout.Button("选中场景中的卡扣"))Selection.activeGameObject=clasp.gameObject;
        EditorGUILayout.EndVertical();
    }

    /// <summary>卡扣不作为移动环的子对象；初始放在第一段直边中点，通道对齐该处切线。</summary>
    public static Ring2DClasp CreateClasp(ProceduralRing2D ring)
    {
        if(!ring.DragsByMovement)throw new InvalidOperationException("只有直线、门形、7/Z 支持导向卡扣。");
        Sprite sprite=Ring2DDemoEditor.GetClaspSprite();
        var s=ring.Settings;double straight=s.Width, start=0;
        if(s.Shape==Ring2DShape.Gate)straight=s.Height-s.CornerRadius;
        else if(s.Shape==Ring2DShape.Seven)straight=s.Width-s.CornerRadius;
        else if(s.Shape==Ring2DShape.ZShape)straight=s.ZTopWidth-s.CornerRadius;
        if(straight<=0.0001 && (s.Shape==Ring2DShape.Seven || s.Shape==Ring2DShape.ZShape))
        {
            start=Math.PI*s.CornerRadius/2;
            straight=s.Height-(s.Shape==Ring2DShape.Seven?s.CornerRadius:2*s.CornerRadius);
        }
        double x,y,tx,ty,nx,ny;Ring2DGeometry.Frame(s,(start+Math.Max(0,straight)/2)/Ring2DGeometry.PathLength(s),out x,out y,out tx,out ty,out nx,out ny);
        var go=new GameObject(ring.name+" - 固定 C 卡扣");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,ring.gameObject.scene);
        go.transform.position=ring.transform.TransformPoint(new Vector3((float)x,(float)y,0));
        var tangent=ring.transform.TransformVector(new Vector3((float)tx,(float)ty,0));
        go.transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2(tangent.y,tangent.x)*Mathf.Rad2Deg-90);
        var clasp=go.AddComponent<Ring2DClasp>();clasp.ClaspSprite=sprite;clasp.RenderMaterial=ring.RenderMaterial;
        clasp.SortingLayer=ring.SortingLayer;clasp.SortingOrder=ring.SortingOrder+10;
        clasp.ChannelWidth=ring.Settings.Thickness+0.06f;clasp.Width=Mathf.Max(0.5f,clasp.ChannelWidth+0.2f);
        clasp.Length=Mathf.Min(0.65f,Mathf.Max(0.1f,(float)straight*0.8f));clasp.Refresh();
        Undo.RegisterCreatedObjectUndo(go,"添加固定 C 卡扣");Undo.RecordObject(ring,"绑定固定 C 卡扣");
        var list=ring.Clasps==null?new Ring2DClasp[0]:ring.Clasps;
        ring.Clasps=list.Concat(new[]{clasp}).ToArray();EditorUtility.SetDirty(ring);
        EditorSceneManager.MarkSceneDirty(ring.gameObject.scene);return clasp;
    }

    /// <summary>门形竖向移动的双卡扣预设：左右竖边各一个，通道均沿世界 Y 轴，作为一次撤销操作。</summary>
    public static Ring2DClasp[] CreateGateClasps(ProceduralRing2D ring)
    {
        if(ring.Settings.Shape!=Ring2DShape.Gate)throw new InvalidOperationException("双卡扣预设只适用于门形。");
        Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
        var left=CreateClasp(ring);var right=CreateClasp(ring);
        left.name=ring.name+" - 左侧固定 C 卡扣";right.name=ring.name+" - 右侧固定 C 卡扣";
        var s=ring.Settings;
        right.transform.position=ring.transform.TransformPoint(new Vector3(s.Width/2,-s.Height/2+(s.Height-s.CornerRadius)/2,0));
        left.transform.rotation=right.transform.rotation=Quaternion.Euler(0,0,90);
        Undo.CollapseUndoOperations(group);
        return new[]{left,right};
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
    public const string ScenePath = "Assets/RotateRings/Scenes/Ring2DDemo.unity";
    public const string ClaspScenePath = "Assets/RotateRings/Ring2D/Scenes/ClaspDemo.unity";
    private const string TexturePath = "Assets/RotateRings/Ring2D/Textures/ringextra_2d.png";
    [MenuItem("Tools/Rotate Rings/打开固定 C 卡扣示例")]
    public static void OpenClaspDemo()
    {
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        if(File.Exists(ClaspScenePath)){EditorSceneManager.OpenScene(ClaspScenePath);return;}
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var camera=new GameObject("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";
        camera.transform.position=new Vector3(7.5f,0,-20);camera.orthographic=true;camera.orthographicSize=5;
        camera.backgroundColor=new Color(0.97f,0.9f,0.85f);camera.clearFlags=CameraClearFlags.SolidColor;
        var kinds=new[]{Ring2DShape.Straight,Ring2DShape.Gate,Ring2DShape.Seven,Ring2DShape.ZShape};
        for(int i=0;i<kinds.Length;i++)
        {
            var ring=Add(kinds[i].ToString(),new Vector2(i*5,0),kinds[i],new Color(0.2f,0.65f,1));
            ring.Connections=new Ring2DConnection[0];ring.Rebuild();
            if(kinds[i]==Ring2DShape.Gate)ProceduralRing2DEditor.CreateGateClasps(ring);else ProceduralRing2DEditor.CreateClasp(ring);
        }
        EditorSceneManager.SaveScene(scene,ClaspScenePath);AssetDatabase.SaveAssets();
    }
    /// <summary>保留原 A/B 切片，追加 C 卡扣切片；以后可替换为独立的 C Sprite。</summary>
    public static Sprite GetClaspSprite()
    {
        var sprite=AssetDatabase.LoadAllAssetsAtPath(TexturePath).OfType<Sprite>().FirstOrDefault(s=>s.name=="RingClasp_C");
        if(sprite!=null)return sprite;
        var importer=AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        if(importer==null)throw new InvalidOperationException("找不到卡扣示例图："+TexturePath);
        var factories=new SpriteDataProviderFactories();factories.Init();
        var provider=factories.GetSpriteEditorDataProviderFromObject(importer);provider.InitSpriteEditorDataProvider();
        var slices=provider.GetSpriteRects().ToList();
        var added=new SpriteRect{name="RingClasp_C",rect=new Rect(784,13,88,102),alignment=SpriteAlignment.Custom,pivot=new Vector2(0.5f,0.5f),border=new Vector4(8,8,8,8),spriteID=GUID.Generate()};
        slices.RemoveAll(s=>s.name==added.name);slices.Add(added);provider.SetSpriteRects(slices.ToArray());
        var names=provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        var pairs=names.GetNameFileIdPairs().Where(p=>p.name!=added.name).ToList();pairs.Add(new SpriteNameFileIdPair(added.name,added.spriteID));
        names.SetNameFileIdPairs(pairs);provider.Apply();
        importer.SaveAndReimport();
        return AssetDatabase.LoadAllAssetsAtPath(TexturePath).OfType<Sprite>().First(s=>s.name=="RingClasp_C");
    }
    public static void AssignSprites(ProceduralRing2D ring)
    {
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

    [MenuItem("Tools/Rotate Rings/打开 A-B 二维圆环与几何阻挡示例")]
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
