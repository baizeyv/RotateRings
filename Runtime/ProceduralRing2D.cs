using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Sprites;

namespace RotateRings
{
    public enum Ring2DMovementAxis { [InspectorName("左右")] Horizontal, [InspectorName("上下")] Vertical }
    // 保留枚举数值及字段名，以兼容已有场景的序列化设置。
    public enum Ring2DPhysicsMode
    {
        None,
        [InspectorName("无阻挡（旧 TriggerDetection）")] TriggerDetection,
        [InspectorName("几何阻挡（旋转 / 移动）")] BlockRotation
    }
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
        [Tooltip("直线、门形、7 形、Z 形的世界坐标移动轴。")]
        public Ring2DMovementAxis MovementAxis = Ring2DMovementAxis.Horizontal;
        [Tooltip("固定在场景中的 C 图卡扣。配置后由卡扣通道决定移动方向；多个卡扣须平行。")]
        public Ring2DClasp[] Clasps = new Ring2DClasp[0];
        [InspectorName("Collision Mode")] public Ring2DPhysicsMode PhysicsMode = Ring2DPhysicsMode.BlockRotation;
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
        private Vector3 lastDragPoint;
        private ProceduralRing2D lastLoggedObstacle;
        private ProceduralRing2D blockedByContactOwner, contactPartner;
        private float lastCollisionLogTime = float.NegativeInfinity;
        private static readonly List<ProceduralRing2D> activeRings = new List<ProceduralRing2D>();
        private readonly List<Ring2DCollisionShape> localShapes = new List<Ring2DCollisionShape>();
        private readonly List<Ring2DCollisionShape> worldShapes = new List<Ring2DCollisionShape>();
        private readonly List<ProceduralRing2D> obstacles = new List<ProceduralRing2D>();
        private readonly List<Ring2DCollisionShape> claspFrames = new List<Ring2DCollisionShape>();

        public IReadOnlyList<Ring2DConnectionView> ConnectionViews { get { return views; } }
        public Mesh SavedMesh { get { return savedMesh; } }
        public Vector3 LastCollisionPoint { get; private set; }
        public event Action<ProceduralRing2D, Vector3> CollisionBlocked;
        public bool DragsByMovement { get { return Settings.Shape == Ring2DShape.Straight || Settings.Shape == Ring2DShape.Gate ||
            Settings.Shape == Ring2DShape.Seven || Settings.Shape == Ring2DShape.ZShape; } }
        private bool SharesMovementAxis(ProceduralRing2D other)
        { Vector2 a, b; return DragsByMovement && other.DragsByMovement && TryMovementDirection(out a) && other.TryMovementDirection(out b) && Mathf.Abs(Vector2.Dot(a,b)) > 0.9999f; }

        /// <summary>使用固定卡扣的世界通道方向；未配置卡扣时保留旧移动轴，非平行卡扣拒绝移动。</summary>
        public bool TryMovementDirection(out Vector2 direction)
        {
            direction = MovementAxis == Ring2DMovementAxis.Horizontal ? Vector2.right : Vector2.up;
            bool found = false;
            if (Clasps != null) foreach (var clasp in Clasps)
            {
                if (clasp == null || !clasp.isActiveAndEnabled) continue;
                if (!clasp.Valid || clasp.Direction.sqrMagnitude < 0.99f) return false;
                if (!found) { direction = clasp.Direction; found = true; }
                else if (Mathf.Abs(Vector2.Dot(direction, clasp.Direction)) < 0.9999f) return false;
            }
            return true;
        }
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
        private void OnEnable() { activeRings.Add(this); DisablePhysics(); dirty = true; }
        private void OnValidate() { dirty = true; }

        /// <summary>只在参数变化时更新网格/子对象。旋转只动父级 Transform，所有子元素一起跟随。</summary>
        private void Update()
        {
            if (dirty) { dirty = false; Rebuild(); }
            if (!Application.isPlaying || Camera.main == null) return;
            if (Input.GetMouseButtonDown(0) && pickFrame != Time.frameCount)
            {
                pickFrame = Time.frameCount; dragged = null;
                foreach (var ring in activeRings)
                    if (ring.AllowDrag && (ring.DragsByMovement || !ring.LockRotation) && ring.HitPointer() && (dragged == null || ring.SortingOrder > dragged.SortingOrder ||
                        (ring.SortingOrder == dragged.SortingOrder && ring.GetInstanceID() > dragged.GetInstanceID()))) dragged = ring;
                if (dragged != null) { dragged.lastDragAngle = dragged.PointerAngle(); dragged.lastDragPoint = dragged.PointerWorldPoint(); }
            }
            if (dragged == this && Input.GetMouseButton(0))
            {
                if (DragsByMovement)
                {
                    Vector3 point = PointerWorldPoint(); RequestMovement(point - lastDragPoint); lastDragPoint = point;
                }
                else
                {
                    float angle = PointerAngle(); RequestRotation(Mathf.DeltaAngle(lastDragAngle, angle)); lastDragAngle = angle;
                }
            }
            if (Input.GetMouseButtonUp(0)) dragged = null;
        }

        /// <summary>直接执行几何受限旋转；遇到障碍立即丢弃剩余请求。</summary>
        public void RequestRotation(float degrees)
        {
            if (LockRotation || float.IsNaN(degrees) || float.IsInfinity(degrees)) return;
            MoveGeometry(Mathf.Clamp(degrees, -180, 180), Vector3.zero);
        }

        /// <summary>世界空间位移，移动形状沿配置轴投影，并检查完整移动路径。</summary>
        public void RequestMovement(Vector2 displacement)
        {
            if (float.IsNaN(displacement.x) || float.IsInfinity(displacement.x) ||
                float.IsNaN(displacement.y) || float.IsInfinity(displacement.y)) return;
            if (DragsByMovement)
            {
                Vector2 direction;
                if (!TryMovementDirection(out direction)) return;
                displacement = direction * Vector2.Dot(displacement, direction);
            }
            MoveGeometry(0, displacement);
        }

        private void MoveGeometry(float degrees, Vector3 displacement)
        {
            if (dirty) Rebuild();
            if (blockedByContactOwner != null && SharesMovementAxis(blockedByContactOwner)) blockedByContactOwner = null;
            Quaternion rotation = transform.localRotation;
            Vector3 position = transform.position;
            if (PhysicsMode != Ring2DPhysicsMode.BlockRotation)
            { transform.localRotation = rotation * Quaternion.Euler(0, 0, degrees); transform.position = position + displacement; return; }
            if (localShapes.Count == 0) return;
            obstacles.Clear();
            claspFrames.Clear();
            foreach (var clasp in Ring2DClasp.Active)
                if (clasp != null && clasp.Valid) foreach (var frame in clasp.WorldFrames()) claspFrames.Add(frame);
            foreach (var ring in activeRings)
            {
                if (ring == this || ring.PhysicsMode != Ring2DPhysicsMode.BlockRotation) continue;
                if (ring.dirty) ring.Rebuild();
                ring.UpdateWorldShapes(ring.transform.localToWorldMatrix);
                obstacles.Add(ring);
            }
            UpdateWorldShapes(transform.localToWorldMatrix);
            double radius = 0;
            foreach (var shape in worldShapes) foreach (var point in shape.Points)
                radius = Math.Max(radius, Math.Sqrt((point.X-position.x)*(point.X-position.x) +
                    (point.Y-position.y)*(point.Y-position.y)) + shape.Radius);
            double travel = Math.Abs(degrees) * Mathf.Deg2Rad * radius + displacement.magnitude;
            if (travel <= 1e-12) return;
            Matrix4x4 parent = transform.parent != null ? transform.parent.localToWorldMatrix : Matrix4x4.identity;
            Vector3 localPosition = transform.localPosition;
            Vector3 localDisplacement = parent.inverse.MultiplyVector(displacement);
            ProceduralRing2D hitRing = null;
            Vector3 hitPosition = position;
            float hitAngle = transform.eulerAngles.z;
            int hitShape = -1, hitOtherShape = -1;
            Vector3 hitPoint = position;
            double latchedClearance = double.PositiveInfinity;
            int latchedShape = -1, latchedOtherShape = -1;
            bool blocked;
            bool heldByContactOwner = false;
            Func<double, double> clearanceAt = t =>
            {
                var matrix = parent * Matrix4x4.TRS(localPosition + localDisplacement * (float)t,
                    rotation * Quaternion.Euler(0, 0, degrees * (float)t), transform.localScale);
                UpdateWorldShapes(matrix);
                double clearance = double.PositiveInfinity;
                ProceduralRing2D closestRing = null;
                int closestShape = -1, closestOtherShape = -1;
                latchedClearance = double.PositiveInfinity;
                foreach (var ring in obstacles) for (int i = 0; i < worldShapes.Count; i++) for (int j = 0; j < ring.worldShapes.Count; j++)
                {
                    var a = worldShapes[i]; var b = ring.worldShapes[j];
                    // 完整圆环的环身旋转不改变轮廓；只有连接件参与主动旋转的阻挡。
                    // 若本对象是被锁住的 B，仍需检查全部轮廓来判断与 A 的接触是否解除。
                    if (blockedByContactOwner == null && displacement == Vector3.zero && degrees != 0 && a.RotationInvariant) continue;
                    double boundsDistance = Ring2DCollision.BoundsDistance(a, b);
                    if (boundsDistance > Math.Max(0, clearance) &&
                        (ring != blockedByContactOwner || boundsDistance > Math.Max(0, latchedClearance))) continue;
                    double distance = Ring2DCollision.Clearance(a, b);
                    if (ring == blockedByContactOwner && distance < latchedClearance)
                    { latchedClearance = distance; latchedShape = i; latchedOtherShape = j; }
                    if (distance < clearance) { clearance = distance; closestRing = ring; closestShape = i; closestOtherShape = j; }
                }
                // 固定卡扣轨道也是几何障碍；不能套用 B 连接头的圆环通行豁免。
                foreach (var a in worldShapes) foreach (var b in claspFrames)
                {
                    if (displacement == Vector3.zero && degrees != 0 && a.RotationInvariant) continue;
                    if (Ring2DCollision.BoundsDistance(a,b) > Math.Max(0,clearance)) continue;
                    double distance = Ring2DCollision.Clearance(a,b);
                    if (distance < clearance) { clearance = distance; closestRing = null; }
                }
                if (clearance <= Ring2DCollision.ContactDistance && closestRing != null)
                {
                    hitRing = closestRing; hitShape = closestShape; hitOtherShape = closestOtherShape;
                    hitPosition = position+displacement*(float)t;
                    hitAngle = (transform.parent != null ? transform.parent.rotation : Quaternion.identity).eulerAngles.z +
                        (rotation*Quaternion.Euler(0, 0, degrees*(float)t)).eulerAngles.z;
                    var point = Ring2DCollision.ContactPoint(worldShapes[hitShape], hitRing.worldShapes[hitOtherShape]);
                    hitPoint = new Vector3((float)point.X, (float)point.Y, (position.z+hitRing.transform.position.z)/2);
                }
                return clearance;
            };
            double fraction;
            if (blockedByContactOwner != null)
            {
                clearanceAt(0);
                if (latchedClearance <= Ring2DCollision.ContactDistance)
                {
                    heldByContactOwner = true;
                    fraction = Ring2DCollision.Sweep(travel, clearanceAt, out blocked, true); hitRing = blockedByContactOwner;
                    hitShape = latchedShape; hitOtherShape = latchedOtherShape;
                    hitPosition = position; hitAngle = transform.eulerAngles.z;
                    var point = Ring2DCollision.ContactPoint(worldShapes[hitShape], hitRing.worldShapes[hitOtherShape]);
                    hitPoint = new Vector3((float)point.X, (float)point.Y, (position.z+hitRing.transform.position.z)/2);
                }
                else { blockedByContactOwner = null; fraction = Ring2DCollision.Sweep(travel, clearanceAt, out blocked); }
            }
            else fraction = Ring2DCollision.Sweep(travel, clearanceAt, out blocked);
            transform.localRotation = rotation * Quaternion.Euler(0, 0, degrees * (float)fraction);
            transform.position = position + displacement * (float)fraction;
            if (blocked && hitRing != null)
            {
                // A 主动碰到 B 时由 A 持有接触；B 被锁期间的拖动不能反过来锁住 A。
                if (SharesMovementAxis(hitRing) && hitRing.blockedByContactOwner == this) hitRing.blockedByContactOwner = null;
                if (!heldByContactOwner && !SharesMovementAxis(hitRing) &&
                    (hitRing.blockedByContactOwner == null || hitRing.blockedByContactOwner == this))
                    hitRing.blockedByContactOwner = this;
                LastCollisionPoint = hitPoint; hitRing.LastCollisionPoint = hitPoint;
                if (contactPartner != hitRing)
                {
                    contactPartner = hitRing; hitRing.contactPartner = this;
                    CollisionBlocked?.Invoke(hitRing, hitPoint); hitRing.CollisionBlocked?.Invoke(this, hitPoint);
                }
            }
            else if (fraction > 0)
            {
                if (contactPartner != null && contactPartner.contactPartner == this) contactPartner.contactPartner = null;
                contactPartner = null;
            }
            if (blocked && hitRing != null && (lastLoggedObstacle != hitRing || Time.unscaledTime-lastCollisionLogTime >= 1))
            {
                Debug.Log($"几何碰撞：{name} [轮廓 {hitShape}] ↔ {hitRing.name} [轮廓 {hitOtherShape}]；" +
                    $"接触发起者 {(heldByContactOwner ? hitRing.name : name)}；" +
                    $"碰撞点（世界坐标）{hitPoint:F5}；" +
                    $"受阻时位置 {hitPosition:F5}，Z 角 {hitAngle:F4}°；对方位置 {hitRing.transform.position:F5}，Z 角 {hitRing.transform.eulerAngles.z:F4}°；" +
                    $"停止位置 {transform.position:F5}，Z 角 {transform.eulerAngles.z:F4}°", this);
                lastLoggedObstacle = hitRing; lastCollisionLogTime = Time.unscaledTime;
            }
            UpdateWorldShapes(transform.localToWorldMatrix);
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
                BuildCollisionShapes(poses); DisablePhysics(); dirty = false; return true;
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

        /// <summary>缓存环身胶囊和连接件矩形；仅在形状参数改变后重建。</summary>
        private void BuildCollisionShapes(Ring2DConnectionPose[] poses)
        {
            localShapes.Clear(); worldShapes.Clear();
            var path = Ring2DGeometry.ColliderPath(Settings, ColliderSegments);
            for (int i = 1; i < path.Length; i++)
            {
                var shape = new Ring2DCollisionShape(2) { Radius = Settings.Thickness / 2, IsRingBody = !DragsByMovement,
                    RotationInvariant = Settings.Shape == Ring2DShape.Circle };
                shape.Points[0] = path[i-1]; shape.Points[1] = path[i]; localShapes.Add(shape);
            }
            for (int i = 0; i < poses.Length; i++)
            {
                var pose = poses[i]; var connection = Connections[i];
                if (pose.Length > 0.00001) AddCollisionBox(new Ring2DPoint((pose.Start.X+pose.End.X)/2,
                    (pose.Start.Y+pose.End.Y)/2), pose.DX, pose.DY, pose.Length, connection.RodWidth);
                if (connection.ShowHead)
                {
                    AddCollisionBox(pose.Head, pose.DY, -pose.DX, connection.HeadWidth, connection.HeadDepth);
                    localShapes[localShapes.Count-1].HeadPassageRadians = connection.AllowRingBodyPassage ? connection.PassageAngleDegrees*Math.PI/180 : 0;
                }
            }
            foreach (var shape in localShapes) worldShapes.Add(new Ring2DCollisionShape(shape.Points.Length));
        }

        private void AddCollisionBox(Ring2DPoint center, double dx, double dy, double width, double height)
        {
            var shape = new Ring2DCollisionShape(4);
            for (int i = 0; i < 4; i++)
            {
                double x = (i == 0 || i == 3 ? -1 : 1) * width / 2;
                double y = (i < 2 ? -1 : 1) * height / 2;
                shape.Points[i] = new Ring2DPoint(center.X + dx*x - dy*y, center.Y + dy*x + dx*y);
            }
            localShapes.Add(shape);
        }

        private void UpdateWorldShapes(Matrix4x4 matrix)
        {
            double scale = Math.Max(matrix.MultiplyVector(Vector3.right).magnitude, matrix.MultiplyVector(Vector3.up).magnitude);
            Vector3 center = matrix.MultiplyPoint3x4(Vector3.zero);
            for (int i = 0; i < localShapes.Count; i++)
            {
                var local = localShapes[i]; var world = worldShapes[i]; world.Radius = local.Radius * scale;
                world.IsRingBody = local.IsRingBody; world.HeadPassageRadians = local.HeadPassageRadians;
                world.RotationInvariant = local.RotationInvariant;
                world.CircleCenter = new Ring2DPoint(center.x, center.y);
                for (int j = 0; j < local.Points.Length; j++)
                {
                    var point = local.RotationInvariant
                        ? new Vector3(center.x+(float)(local.Points[j].X*scale), center.y+(float)(local.Points[j].Y*scale), center.z)
                        : matrix.MultiplyPoint3x4(new Vector3((float)local.Points[j].X, (float)local.Points[j].Y, 0));
                    world.Points[j] = new Ring2DPoint(point.x, point.y);
                }
                world.UpdateBounds();
            }
        }

        /// <summary>旧场景的自动碰撞体保留为禁用状态，不再创建或模拟刚体。</summary>
        private void DisablePhysics()
        {
            foreach (var body in GetComponents<Rigidbody2D>()) body.simulated = false;
            foreach (var collider in GetComponents<Collider2D>()) collider.enabled = false;
            foreach (var view in views)
            {
                if (view == null) continue;
                foreach (var collider in view.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
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
        { return transform.InverseTransformPoint(PointerWorldPoint()); }

        private Vector3 PointerWorldPoint()
        {
            var plane = new Plane(transform.forward, transform.position); float distance;
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            return plane.Raycast(ray, out distance) ? ray.GetPoint(distance) : transform.position;
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
            blockedByContactOwner = null;
            if (contactPartner != null && contactPartner.contactPartner == this) contactPartner.contactPartner = null;
            contactPartner = null;
            activeRings.Remove(this);
            if (generatedEdge != null) generatedEdge.enabled = false;
            if (generatedBody != null) generatedBody.simulated = false;
            ReleaseOwnedMesh();
            if (ownedMaterial != null) { DeleteObject(ownedMaterial, false); ownedMaterial = null; }
            if (ownedConnectionSprite != null) { DeleteObject(ownedConnectionSprite, false); ownedConnectionSprite = null; previousConnectionSprite = null; }
            foreach (var view in views) if (view != null) view.gameObject.SetActive(false);
        }
    }
}
