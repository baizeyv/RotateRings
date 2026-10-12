using UnityEngine;

namespace RotateRings
{
    public sealed class Ring2DDemoLabels : MonoBehaviour
    {
        public ProceduralRing2D Outer;
        private void OnGUI()
        {
            GUI.Label(new Rect(16,12,Screen.width-32,28),"Drag Circle/C to rotate; Straight/Gate/7/Z to move along their configured axis.");
            GUI.Label(new Rect(16,42,Screen.width-32,28),"Unlock the blue C-ring: its body slides through the orange head channel; the rod still blocks.");
            if (Outer!=null && GUI.Button(new Rect(16,76,240,34),Outer.LockRotation ? "Unlock outer C-ring" : "Lock outer C-ring"))
            { Outer.LockRotation=!Outer.LockRotation; Outer.Rebuild(); }
            if (Camera.main==null) return;
            foreach(var ring in FindObjectsByType<ProceduralRing2D>(FindObjectsSortMode.None))
            {
                if(ring==Outer) continue;
                float below=ring.Settings.Shape==Ring2DShape.Seven || ring.Settings.Shape==Ring2DShape.ZShape ? ring.Settings.Height/2+0.4f : 1.65f;
                var p=Camera.main.WorldToScreenPoint(ring.transform.position+Vector3.down*below);
                GUI.Label(new Rect(p.x-150,Screen.height-p.y,300,28),ring.name);
            }
        }
    }
}
