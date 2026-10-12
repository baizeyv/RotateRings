using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Sprites;

namespace RotateRings
{
    public enum Ring2DPhysicsMode { None, TriggerDetection, BlockRotation }
    /// <summary>世界空间二维圆环：A 图生成平面 Mesh，B 图生成独立的连接杆/头子元素。</summary>
    [ExecuteAlways, RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(SortingGroup))]
    public sealed class ProceduralRing2D : MonoBehaviour
    {
        public Ring2DSettings Settings = new Ring2DSettings();
        public Sprite BodySprite;
        public Sprite ConnectionSprite;
        public Material RenderMaterial;
        public Color Color = new Color(0.2f, 0.65f, 1);
        public string SortingLayer = "Default";
        public int SortingOrder;
        public bool AllowDrag = true;
        public Ring2DPhysicsMode PhysicsMode = Ring2DPhysicsMode.BlockRotation;
        public bool LockRotation;
        [Range(8, 128), Tooltip("碰撞圆周分段，与显示 Segments 独立；C 环按覆盖角缩减，门字环仅细分圆角。")]
        public int ColliderSegments = 32;
        public Ring2DConnection[] Connections = { new Ring2DConnection() };
        [SerializeField, HideInInspector] private List<Ring2DConnectionView> views = new List<Ring2DConnectionView>();
        [SerializeField, HideInInspector] private EdgeCollider2D generatedEdge;
        [SerializeField, HideInInspector] private Rigidbody2D generatedBody;
        [SerializeField, HideInInspector] private Mesh savedMesh;
        [SerializeField, HideInInspector] private string savedSettings;
        [SerializeField, HideInInspector] private Sprite savedBodySprite;
        [SerializeField, HideInInspector] private Vector4 savedUV;
        [SerializeField, HideInInspector] private float savedCapFraction;
        [SerializeField, HideInInspector] private int savedGeometryVersion;
        private const int MeshGeometryVersion = 2;
        private Mesh ownedMesh;
        private Material ownedMaterial;
        private Sprite ownedConnectionSprite, previousConnectionSprite;
        private bool dirty;
        private static ProceduralRing2D dragged;
        private static int pickFrame = -1;
        private float lastDragAngle;
        private float pendingRotation;

        public IReadOnlyList<Ring2DConnectionView> ConnectionViews { get { return views; } }
        public Mesh SavedMesh { get { return savedMesh; } }
        /// <summary>只在形状和 A 图 UV 仍匹配时复用资源，避免显示旧形状而碰撞体已变成新形状。</summary>
        public bool UsesSavedMesh
        {
            get { return savedMesh != null && savedGeometryVersion == MeshGeometryVersion && BodySprite != null && savedBodySprite == BodySprite &&
                savedSettings == JsonUtility.ToJson(Settings) && savedUV == DataUtility.GetOuterUV(BodySprite) &&
                Mathf.Approximately(savedCapFraction, (float)CapFraction()); }
        }

        /// <summary>保存按钮用当前参数登记 Mesh；传 null 恢复程序化网格。资源自身不会被修改或销毁。</summary>
        public void UseSavedMesh(Mesh mesh)
        {
            if (mesh != null && (BodySprite == null || mesh.vertexCount == 0)) throw new ArgumentException("保存的 Mesh 或 A 图无效。");
            savedMesh = mesh; savedSettings = mesh != null ? JsonUtility.ToJson(Settings) : null;
            savedBodySprite = mesh != null ? BodySprite : null;
            savedUV = mesh != null ? DataUtility.GetOuterUV(BodySprite) : Vector4.zero;
            savedCapFraction = mesh != null ? (float)CapFraction() : 0;
            savedGeometryVersion = mesh != null ? MeshGeometryVersion : 0;
            dirty = true;
        }

        private double CapFraction()
        { return BodySprite.border.x > 0 ? BodySprite.border.x / BodySprite.rect.width : BodySprite.rect.height / 2 / BodySprite.rect.width; }
        private void OnEnable() { dirty = true; }
        private void OnValidate() { dirty = true; }

        /// <summary>只在参数变化时更新网格/子对象。旋转只动父级 Transform，所有子元素一起跟随。</summary>
        private void Update()
        {
            if (dirty) { dirty = false; Rebuild(); }
            if (!Application.isPlaying || Camera.main == null) return;
            if (Input.GetMouseButtonDown(0) && pickFrame != Time.frameCount)
            {
                pickFrame = Time.frameCount; dragged = null;
                foreach (var ring in FindObjectsOfType<ProceduralRing2D>())
                    if (ring.AllowDrag && !ring.LockRotation && ring.HitPointer() && (dragged == null || ring.SortingOrder > dragged.SortingOrder ||
                        (ring.SortingOrder == dragged.SortingOrder && ring.GetInstanceID() > dragged.GetInstanceID()))) dragged = ring;
                if (dragged != null) dragged.lastDragAngle = dragged.PointerAngle();
            }
            if (dragged == this && Input.GetMouseButton(0))
            {
                float angle = PointerAngle(); RequestRotation(Mathf.DeltaAngle(lastDragAngle, angle)); lastDragAngle = angle;
            }
            if (Input.GetMouseButtonUp(0)) dragged = null;
        }

        /// <summary>实体模式只提交旋转请求，不直接修改 Transform；碰撞可能使实际角度小于请求。</summary>
        public void RequestRotation(float degrees)
        {
            if (LockRotation || float.IsNaN(degrees) || float.IsInfinity(degrees)) return;
            if (PhysicsMode == Ring2DPhysicsMode.BlockRotation) pendingRotation += Mathf.Clamp(degrees, -180, 180);
            else transform.Rotate(0, 0, degrees);
        }

        private void FixedUpdate() { ApplyPhysicsRotation(); }

        /// <summary>在固定物理步执行动态刚体旋转，中心被约束。丢弃受阻的累计量，反向拖动可立即响应。</summary>
        public void ApplyPhysicsRotation()
        {
            if (PhysicsMode != Ring2DPhysicsMode.BlockRotation || LockRotation) { pendingRotation = 0; return; }
            var body = GetComponent<Rigidbody2D>();
            if (body != null && body.simulated && Mathf.Abs(pendingRotation) > 0.0001f)
                body.MoveRotation(body.rotation + Mathf.Clamp(pendingRotation, -720 * Time.fixedDeltaTime, 720 * Time.fixedDeltaTime));
            pendingRotation = 0;
        }

        /// <summary>根据父级配置刷新；Editor 显式操作可启用 recordUndo，让生成子对象随配置一起撤销。</summary>
        public bool Rebuild(bool recordUndo = false)
        {
            try
            {
                Settings.Validate();
                if (BodySprite == null) throw new ArgumentException("请指定环身图 A。");
                if (Connections == null) Connections = new Ring2DConnection[0];
                if (Connections.Length > 16) throw new ArgumentException("连接点最多为 16 个。");
                if (Connections.Length > 0 && ConnectionSprite == null) throw new ArgumentException("请指定连接图 B。");
                // 先验证全部连接，避免中途失败只更新部分子对象。
                var poses = new Ring2DConnectionPose[Connections.Length];
                for (int i = 0; i < poses.Length; i++)
                {
                    if (Connections[i] == null) throw new ArgumentException("连接点参数不能为空。");
                    poses[i] = Ring2DGeometry.ConnectionPose(Settings, Connections[i]);
                }
#if UNITY_EDITOR
                if (recordUndo && !Application.isPlaying)
                {
                    // 先提交 Inspector 参数的旧值，避免随后记录层级快照覆盖尚未提交的参数撤销。
                    UnityEditor.Undo.FlushUndoRecordObjects();
                    UnityEditor.Undo.RegisterFullObjectHierarchyUndo(gameObject, "更新二维圆环");
                }
#endif
                if (UsesSavedMesh)
                {
                    // 直接引用持久化资源，跳过几何计算、顶点数组分配及 Mesh 上传。
                    GetComponent<MeshFilter>().sharedMesh = savedMesh;
                    ReleaseOwnedMesh();
                }
                else BuildBodyMesh();
                Material material = RenderMaterial;
                if (material == null)
                {
                    if (ownedMaterial == null) ownedMaterial = new Material(Shader.Find("RotateRings/Ring 2D")) { hideFlags = HideFlags.DontSave };
                    material = ownedMaterial;
                }
                var renderer = GetComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.sortingOrder = 2;
                var block = new MaterialPropertyBlock(); block.SetTexture("_MainTex", BodySprite.texture); block.SetColor("_Color", Color); renderer.SetPropertyBlock(block);
                var group = GetComponent<SortingGroup>(); group.sortingLayerName = SortingLayer; group.sortingOrder = SortingOrder;
                PrepareConnectionSprite();
                views.RemoveAll(view => view == null);
                while (views.Count > Connections.Length)
                {
                    var removed = views[views.Count - 1]; views.RemoveAt(views.Count - 1); DeleteObject(removed.gameObject, recordUndo);
                }
                while (views.Count < Connections.Length) views.Add(CreateView(views.Count, recordUndo));
                for (int i = 0; i < views.Count; i++)
                {
                    var view = views[i]; var c = Connections[i]; var pose = poses[i];
                    view.gameObject.SetActive(true); view.gameObject.name = i + " - " + c.Name; view.Owner = this; view.ConnectionIndex = i;
                    view.transform.localPosition = Vector3.zero; view.transform.localRotation = Quaternion.identity; view.transform.localScale = Vector3.one;
                    float angle = Mathf.Atan2((float)pose.DY, (float)pose.DX) * Mathf.Rad2Deg;
                    SetSprite(view.Rod, new Vector2((float)(pose.Start.X + pose.End.X) / 2, (float)(pose.Start.Y + pose.End.Y) / 2), angle,
                        new Vector2((float)pose.Length, c.RodWidth), pose.Length > 0.00001, material, 0);
                    // B 的横轴在杆上沿外伸方向，在头上转 -90° 沿切向，两者互相垂直。
                    SetSprite(view.Head, new Vector2((float)pose.Head.X, (float)pose.Head.Y), angle - 90,
                        new Vector2(c.HeadWidth, c.HeadDepth), c.ShowHead, material, 1);
                }
                UpdateTriggers(); dirty = false; return true;
            }
            catch (Exception exception) { Debug.LogError("二维圆环生成失败：" + exception.Message, this); return false; }
        }

        /// <summary>未烘焙或参数改变时生成临时本体网格，UV 对应当前 A Sprite 所在区域。</summary>
        private void BuildBodyMesh()
        {
            RingMeshData data = Ring2DGeometry.Build(Settings, CapFraction());
            if (ownedMesh == null) ownedMesh = new Mesh { name = "Ring 2D", hideFlags = HideFlags.DontSave };
            ownedMesh.Clear();
            var vertices = new Vector3[data.Vertices.Length]; var uv = new Vector2[vertices.Length]; var colors = new Color32[vertices.Length];
            Vector4 bounds = DataUtility.GetOuterUV(BodySprite);
            for (int i = 0; i < vertices.Length; i++)
            {
                RingVertex v = data.Vertices[i]; vertices[i] = new Vector3(v.X, v.Y, 0);
                uv[i] = new Vector2(Mathf.Lerp(bounds.x, bounds.z, v.U), Mathf.Lerp(bounds.y, bounds.w, v.V)); colors[i] = UnityEngine.Color.white;
            }
            ownedMesh.vertices = vertices; ownedMesh.uv = uv; ownedMesh.colors32 = colors;
            ownedMesh.triangles = data.Triangles; ownedMesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = ownedMesh;
        }

        /// <summary>只释放本组件创建的临时 Mesh，绝不销毁保存的共享资源。</summary>
        private void ReleaseOwnedMesh()
        {
            if (ownedMesh == null) return;
            if (GetComponent<MeshFilter>().sharedMesh == ownedMesh) GetComponent<MeshFilter>().sharedMesh = null;
            DeleteObject(ownedMesh, false); ownedMesh = null;
        }

        /// <summary>用户拆图后的 B 若未设置 Border，创建临时九宫格 Sprite；不修改原 Sprite 资源。</summary>
        private void PrepareConnectionSprite()
        {
            if (previousConnectionSprite == ConnectionSprite) return;
            if (ownedConnectionSprite != null) DeleteObject(ownedConnectionSprite, false);
            ownedConnectionSprite = null; previousConnectionSprite = ConnectionSprite;
            if (ConnectionSprite != null && ConnectionSprite.border == Vector4.zero)
            {
                float b = Mathf.Min(ConnectionSprite.rect.width, ConnectionSprite.rect.height) * 0.12f;
                ownedConnectionSprite = Sprite.Create(ConnectionSprite.texture, ConnectionSprite.rect, new Vector2(0.5f, 0.5f),
                    ConnectionSprite.pixelsPerUnit, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
                ownedConnectionSprite.hideFlags = HideFlags.DontSave;
            }
        }

        private Ring2DConnectionView CreateView(int index, bool undo)
        {
            var root = new GameObject("连接点 " + index); root.transform.SetParent(transform, false);
            var view = root.AddComponent<Ring2DConnectionView>();
            view.Rod = new GameObject("连接杆 B").AddComponent<SpriteRenderer>(); view.Rod.transform.SetParent(root.transform, false);
            view.Head = new GameObject("连接头 B").AddComponent<SpriteRenderer>(); view.Head.transform.SetParent(root.transform, false);
#if UNITY_EDITOR
            if (undo && !Application.isPlaying) UnityEditor.Undo.RegisterCreatedObjectUndo(root, "添加连接点子对象");
#endif
            return view;
        }

        private void SetSprite(SpriteRenderer renderer, Vector2 position, float angle, Vector2 size, bool visible, Material material, int order)
        {
            renderer.gameObject.SetActive(visible); renderer.gameObject.layer = gameObject.layer;
            renderer.sprite = ownedConnectionSprite != null ? ownedConnectionSprite : ConnectionSprite;
            renderer.drawMode = SpriteDrawMode.Sliced; renderer.color = Color; renderer.sharedMaterial = material; renderer.sortingOrder = order;
            renderer.transform.localPosition = new Vector3(position.x, position.y, 0); renderer.transform.localRotation = Quaternion.Euler(0, 0, angle);
            // 保持 B 的竖向比例，九宫格只拉伸中段；不同宽度的杆不会把边框放大到半个杆宽。
            float scale = renderer.sprite != null ? size.y / (renderer.sprite.rect.height / renderer.sprite.pixelsPerUnit) : 1;
            renderer.transform.localScale = new Vector3(scale, scale, 1); renderer.size = size / scale;
        }

        /// <summary>路径边保留内孔，杆/头各有 Box；实体模式为动态刚体，冻结中心但允许旋转。</summary>
        private void UpdateTriggers()
        {
            bool enabled = PhysicsMode != Ring2DPhysicsMode.None;
            bool trigger = PhysicsMode == Ring2DPhysicsMode.TriggerDetection;
            if (enabled && generatedEdge == null) generatedEdge = gameObject.AddComponent<EdgeCollider2D>();
            if (generatedEdge != null)
            {
                generatedEdge.enabled = enabled; generatedEdge.isTrigger = trigger; generatedEdge.edgeRadius = Settings.Thickness / 2;
                Ring2DPoint[] path = Ring2DGeometry.ColliderPath(Settings, ColliderSegments); var points = new Vector2[path.Length];
                for (int i = 0; i < points.Length; i++) points[i] = new Vector2((float)path[i].X, (float)path[i].Y);
                generatedEdge.points = points;
            }
            if (enabled && GetComponent<Rigidbody2D>() == null) generatedBody = gameObject.AddComponent<Rigidbody2D>();
            var body = GetComponent<Rigidbody2D>();
            if (body != null && enabled)
            {
                body.bodyType = trigger ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic; body.gravityScale = 0;
                body.constraints = RigidbodyConstraints2D.FreezePosition | (LockRotation ? RigidbodyConstraints2D.FreezeRotation : RigidbodyConstraints2D.None);
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous; body.interpolation = RigidbodyInterpolation2D.Interpolate;
                body.angularDrag = 1; body.simulated = true;
            }
            if (generatedBody != null) generatedBody.simulated = enabled;
            foreach (var view in views) foreach (var renderer in new[] { view.Rod, view.Head })
            {
                var box = renderer.GetComponent<BoxCollider2D>();
                if (enabled && box == null) box = renderer.gameObject.AddComponent<BoxCollider2D>();
                if (box != null) { box.isTrigger = trigger; box.size = renderer.size; box.offset = Vector2.zero; box.enabled = enabled && renderer.gameObject.activeSelf; }
            }
            // 连接头的 Box 会把自动质心拉向环边，而 Edge 本身没有面积。
            // FreezePosition 固定的是质心；必须显式设为环心，否则会绕连接头旋转并偏移本体。
            if (body != null && enabled)
            {
                body.centerOfMass = Vector2.zero;
                // Edge 没有面积，只有环身时自动转动惯量为零。用环/矩形近似保证角向碰撞响应。
                float span = Settings.Shape == Ring2DShape.ZShape ? Settings.ZTopWidth + Settings.ZBottomWidth : Settings.Width;
                float radiusSquared = Settings.Shape == Ring2DShape.Circle || Settings.Shape == Ring2DShape.CShape
                    ? Settings.Radius * Settings.Radius
                    : (span * span + (Settings.Shape == Ring2DShape.Straight ? Settings.Thickness * Settings.Thickness : Settings.Height * Settings.Height)) / 12;
                body.inertia = Mathf.Max(0.001f, body.mass * radiusSquared);
            }
        }

        private static void DeleteObject(UnityEngine.Object obj, bool undo)
        {
#if UNITY_EDITOR
            if (undo && !Application.isPlaying) { UnityEditor.Undo.DestroyObjectImmediate(obj); return; }
#endif
            if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }

        private Vector3 PointerPoint()
        {
            var plane = new Plane(transform.forward, transform.position); float distance;
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            return plane.Raycast(ray, out distance) ? transform.InverseTransformPoint(ray.GetPoint(distance)) : Vector3.zero;
        }
        private float PointerAngle()
        {
            var parent = transform.parent; Vector3 world = transform.TransformPoint(PointerPoint());
            Vector3 relative = parent != null ? parent.InverseTransformPoint(world) - transform.localPosition : world - transform.position;
            return Mathf.Atan2(relative.y, relative.x) * Mathf.Rad2Deg;
        }
        private bool HitPointer()
        {
            Vector3 point = PointerPoint();
            var path = Ring2DGeometry.Centerline(Settings);
            for (int i = 1; i < path.Length; i++)
            {
                var a = new Vector2((float)path[i - 1].X, (float)path[i - 1].Y); var b = new Vector2((float)path[i].X, (float)path[i].Y);
                Vector2 delta = b - a; float t = delta.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot((Vector2)point - a, delta) / delta.sqrMagnitude) : 0;
                if (Vector2.Distance(point, a + t * delta) <= Settings.Thickness / 2 + 0.025f) return true;
            }
            foreach (var view in views) foreach (var renderer in new[] { view.Rod, view.Head })
                if (renderer.gameObject.activeSelf)
                {
                    Vector3 local = renderer.transform.InverseTransformPoint(transform.TransformPoint(point));
                    if (Mathf.Abs(local.x) <= renderer.size.x / 2 && Mathf.Abs(local.y) <= renderer.size.y / 2) return true;
                }
            return false;
        }

        private void OnDisable()
        {
            if (dragged == this) dragged = null;
            pendingRotation = 0;
            if (generatedEdge != null) generatedEdge.enabled = false;
            if (generatedBody != null) generatedBody.simulated = false;
            ReleaseOwnedMesh();
            if (ownedMaterial != null) { DeleteObject(ownedMaterial, false); ownedMaterial = null; }
            if (ownedConnectionSprite != null) { DeleteObject(ownedConnectionSprite, false); ownedConnectionSprite = null; previousConnectionSprite = null; }
            foreach (var view in views) if (view != null) view.gameObject.SetActive(false);
        }
    }
}
