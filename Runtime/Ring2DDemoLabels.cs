using UnityEngine;

namespace RotateRings
{
    public sealed class Ring2DDemoLabels : MonoBehaviour
    {
        public ProceduralRing2D Outer;
        private void OnGUI()
        {
            GUI.Label(new Rect(16,12,Screen.width-32,28),"2D A/B Rings - drag rings or heads - real Physics2D blocks rotation");
            GUI.Label(new Rect(16,42,Screen.width-32,28),"Lower right: the orange head hits the blue C-ring. Unlock the outer ring to reposition its gap.");
            if (Outer!=null && GUI.Button(new Rect(16,76,240,34),Outer.LockRotation ? "Unlock outer C-ring" : "Lock outer C-ring"))
            { Outer.LockRotation=!Outer.LockRotation; Outer.Rebuild(); }
            if (Camera.main==null) return;
            foreach(var ring in FindObjectsOfType<ProceduralRing2D>())
            {
                if(ring==Outer) continue;
                float below=ring.Settings.Shape==Ring2DShape.Seven || ring.Settings.Shape==Ring2DShape.ZShape ? ring.Settings.Height/2+0.4f : 1.65f;
                var p=Camera.main.WorldToScreenPoint(ring.transform.position+Vector3.down*below);
                GUI.Label(new Rect(p.x-150,Screen.height-p.y,300,28),ring.name);
            }
        }
    }
}
