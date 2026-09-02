using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UI.PlacementMask
{
    //****************************************
    // 不可放置区域红色遮罩 · 单区域 UI 多边形（继承 Graphic，走 UGUI 顶点管线）。
    //
    // 上层（PlacementRangeMaskUI）负责把世界轮廓投影到 Canvas 本地坐标并三角化，
    // 通过 SetMesh(localVerts, triangles) 提交；本组件只负责在 OnPopulateMesh 出顶点。
    //
    // ⚠️ 本工程实测（Unity 2022.3）：AddComponent 建 Graphic 子类不会隐式补 CanvasRenderer，
    // 必须由创建方显式 AddComponent<CanvasRenderer>()，否则 Rebuild 首行即早退、永不出 mesh
    // （见 UITrailRenderer.cs:269-276 的踩坑记录）。
    //****************************************
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PlacementRangeMaskGraphic : Graphic
    {
        private readonly List<Vector2> _localVerts = new List<Vector2>();
        private readonly List<int> _triangles = new List<int>();
        private readonly List<Color32> _vertexColors = new List<Color32>();

        /// <summary>
        /// 提交一块三角化后的多边形（Canvas 本地坐标）。空 = 清空。
        /// vertexColors 非空时逐顶点取色（描边缎带的横向羽化依赖它）；
        /// 为 null 时全部用 Graphic.color（填充层用这条）。
        /// </summary>
        public void SetMesh(List<Vector2> localVerts, List<int> triangles, List<Color32> vertexColors = null)
        {
            _localVerts.Clear();
            _triangles.Clear();
            _vertexColors.Clear();
            if (localVerts != null && triangles != null && localVerts.Count >= 3 && triangles.Count >= 3)
            {
                _localVerts.AddRange(localVerts);
                _triangles.AddRange(triangles);
                // 数量不匹配时忽略顶点色，退回单色，避免越界取色。
                if (vertexColors != null && vertexColors.Count == localVerts.Count)
                    _vertexColors.AddRange(vertexColors);
            }
            SetVerticesDirty();
        }

        public void ClearMesh()
        {
            _localVerts.Clear();
            _triangles.Clear();
            _vertexColors.Clear();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_localVerts.Count < 3 || _triangles.Count < 3)
            {
                return;
            }

            bool perVertex = _vertexColors.Count == _localVerts.Count;
            Color32 flat = color;
            for (int i = 0; i < _localVerts.Count; i++)
            {
                UIVertex vert = UIVertex.simpleVert;
                vert.position = _localVerts[i];
                vert.color = perVertex ? _vertexColors[i] : flat;
                vert.uv0 = Vector2.zero;
                vh.AddVert(vert);
            }

            for (int i = 0; i + 2 < _triangles.Count; i += 3)
            {
                int a = _triangles[i];
                int b = _triangles[i + 1];
                int cc = _triangles[i + 2];
                if (a < 0 || b < 0 || cc < 0) continue;
                if (a >= _localVerts.Count || b >= _localVerts.Count || cc >= _localVerts.Count) continue;
                vh.AddTriangle(a, b, cc);
            }
        }
    }
}
