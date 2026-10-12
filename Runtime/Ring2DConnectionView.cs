using UnityEngine;

namespace RotateRings
{
    /// <summary>父级生成的连接件子对象；杆和头分开显示，方便单独挂触发器或交互脚本。</summary>
    public sealed class Ring2DConnectionView : MonoBehaviour
    {
        public ProceduralRing2D Owner;
        public int ConnectionIndex;
        public SpriteRenderer Rod;
        public SpriteRenderer Head;
    }
}
