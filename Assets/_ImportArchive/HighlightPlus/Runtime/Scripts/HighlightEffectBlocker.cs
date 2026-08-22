using UnityEngine;
using UnityEngine.Rendering;

namespace HighlightPlus {

    [ExecuteInEditMode]
    [DefaultExecutionOrder(-100)]
    public class HighlightEffectBlocker : MonoBehaviour {

        Renderer thisRenderer;
        public bool blockOutlineAndGlow;
        public bool blockOverlay;

        Material blockerOutlineAndGlowMat;
        Material blockerOverlayMat;
        Material blockerAllMat;
        CommandBuffer cmd;

        void OnEnable () {
            thisRenderer = GetComponentInChildren<Renderer>();
            if (blockerOutlineAndGlowMat == null) {
                blockerOutlineAndGlowMat = Resources.Load<Material>("HighlightPlus/HighlightBlockerOutlineAndGlow");
            }
            if (blockerOverlayMat == null) {
                blockerOverlayMat = Resources.Load<Material>("HighlightPlus/HighlightBlockerOverlay");
            }
            if (blockerAllMat == null) {
                blockerAllMat = Resources.Load<Material>("HighlightPlus/HighlightUIMask");
            }
            if (cmd == null) {
                cmd = new CommandBuffer();
            }
        }


        void OnRenderObject () {
            int stencilID = 0;
            if (blockOutlineAndGlow) {
                stencilID = 2;
            }
            if (blockOverlay) {
                stencilID += 4;
            }
            if (stencilID == 0 || thisRenderer == null) return;

            cmd.Clear();
            if (stencilID == 2) {
                cmd.DrawRenderer(thisRenderer, blockerOutlineAndGlowMat);
            }
            else if (stencilID == 4) {
                cmd.DrawRenderer(thisRenderer, blockerOverlayMat);
            }
            else if (stencilID == 6) {
                cmd.DrawRenderer(thisRenderer, blockerAllMat);
            }
            Graphics.ExecuteCommandBuffer(cmd);
        }

    }
}
