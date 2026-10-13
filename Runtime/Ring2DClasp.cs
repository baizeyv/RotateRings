using System;
using System.Collections.Generic;
using UnityEngine;

namespace RotateRings
{
    /// <summary>固定 C 图卡扣；自身 SpriteRenderer 显示，局部 X 为通道，两侧轨道参与几何阻挡。</summary>
    [ExecuteAlways, RequireComponent(typeof(SpriteRenderer))]
    public sealed class Ring2DClasp : MonoBehaviour
    {
        public Sprite ClaspSprite;
        public Material RenderMaterial;
        public Color Color = UnityEngine.Color.white;
        public float Width = 0.5f, Length = 0.65f, ChannelWidth = 0.28f;
        public string SortingLayer = "Default";
        public int SortingOrder = 10;
        public static readonly List<Ring2DClasp> Active = new List<Ring2DClasp>();
        private readonly Ring2DCollisionShape[] frames = { new Ring2DCollisionShape(4), new Ring2DCollisionShape(4) };
        private bool dirty;
        [SerializeField, HideInInspector] private SpriteRenderer image;
        [SerializeField, HideInInspector] private bool rootImageLayout;
        public bool Valid { get { return Finite(Width) && Finite(Length) && Finite(ChannelWidth) && Width > ChannelWidth && ChannelWidth > 0 && Length > 0; } }
        public Vector2 Direction { get { return ((Vector2)(rootImageLayout ? transform.right : transform.up)).normalized; } }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); dirty = true; }
        private void OnDisable() { Active.Remove(this); }
        private void OnValidate() { dirty = true; }
        private void Update() { if (dirty) { dirty = false; Refresh(); } }

        /// <summary>将旧「父级通道 + 旋转子图」迁移成自身显示；旋转对象 90° 并切换通道轴，保持世界方向。</summary>
        public void Refresh()
        {
            if (!rootImageLayout)
            {
                transform.localRotation *= Quaternion.Euler(0, 0, 90);
                rootImageLayout = true;
            }
            var renderer = GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = gameObject.AddComponent<SpriteRenderer>();
            var child = image != null && image.transform != transform ? image.transform : transform.Find("C 卡扣图");
            if (child != null)
            {
                child.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(child.gameObject); else DestroyImmediate(child.gameObject);
            }
            image = renderer; renderer.enabled = true; renderer.sprite = ClaspSprite;
            renderer.color = Color; renderer.sortingLayerName = SortingLayer; renderer.sortingOrder = SortingOrder;
            if (RenderMaterial != null) renderer.sharedMaterial = RenderMaterial;
            renderer.drawMode = SpriteDrawMode.Sliced;
            // 交换尺寸，旋转后世界占用范围仍是横向 Width、通道方向 Length。
            if (Valid) renderer.size = new Vector2(Length, Width);
            dirty = false;
        }

        /// <summary>两侧轨道，中间通道留空，允许环身滑过；粗连接头与圆角仍会碰到轨道。</summary>
        public IReadOnlyList<Ring2DCollisionShape> WorldFrames()
        {
            double rail = (Width - ChannelWidth) / 2;
            for (int side = 0; side < 2; side++)
            {
                double center = (side == 0 ? -1 : 1) * (ChannelWidth + rail) / 2;
                for (int i = 0; i < 4; i++)
                {
                    float cross = (float)(center + (i == 0 || i == 3 ? -1 : 1) * rail / 2);
                    float along = (i < 2 ? -1 : 1) * Length / 2;
                    var world = transform.TransformPoint(rootImageLayout ? new Vector3(along,-cross,0) : new Vector3(cross,along,0));
                    frames[side].Points[i] = new Ring2DPoint(world.x, world.y);
                }
                frames[side].UpdateBounds();
            }
            return frames;
        }

        private void OnDrawGizmosSelected()
        {
            if(!Valid)return;
            Gizmos.color=UnityEngine.Color.cyan;
            foreach(var frame in WorldFrames())for(int i=0;i<4;i++)
            {
                var a=frame.Points[i];var b=frame.Points[(i+1)%4];
                Gizmos.DrawLine(new Vector3((float)a.X,(float)a.Y,transform.position.z),new Vector3((float)b.X,(float)b.Y,transform.position.z));
            }
            Gizmos.color=UnityEngine.Color.green;
            Gizmos.DrawLine(transform.position-(Vector3)Direction*Length,transform.position+(Vector3)Direction*Length);
        }
    }
}
