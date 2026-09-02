using System.Collections.Generic;
using UnityEngine;

namespace UI.PlacementMask
{
    //****************************************
    // 不可放置区域遮罩 · 描边几何。
    //
    // 两段式：
    //   A) 世界空间（3D，投屏前）：Douglas-Peucker 简化 → 定半径圆角。
    //      放在世界空间是为了让容差以「六边形外接圆半径 R」为单位有稳定几何含义，
    //      且推拉相机时轮廓形状不变（屏幕空间做的话，缩放会改变简化力度）。
    //   B) 屏幕空间（2D，投屏后）：生成缎带三角形（每段 5 列：外缘 a=0 / 外芯边 / 中线 / 内芯边 / 内缘 a=0，
    //      芯内三点 alpha 同为 tint.a）。芯宽由 coreRatio 控制，0 时退化为原来的 3 列纯羽化。
    //
    // 顶点色羽化是本工程既有手法（HexHighlightRenderer.AppendGlowBeamQuad），
    // 好处是不需要专用 shader 也不会出硬边「纸片」：顶点色 alpha 横向插值即得柔和边缘。
    // 引入实心芯是为了把「描边多宽」与「边缘多锐」解耦——纯羽化时两者被 halfWidth 绑死，
    // 只能在「细而锐」与「粗而糊」之间取值。
    //
    // 描边居中于路径，故不依赖闭环绕向（追踪出的环 CW/CCW 不定，这里刻意不做绕向归一化）。
    //****************************************
    public static class PlacementMaskOutline
    {
        /// <summary>
        /// 闭环 Douglas-Peucker 简化（世界空间 XZ 平面，Y 由端点线性插值带回）。
        ///
        /// 【为什么必须先简化，而不是直接平滑】
        /// Catmull-Rom 是**插值**样条——它穿过每一个输入点。六边形边界的锯齿本身就是顶点，
        /// 样条只会把锯齿磨圆，磨不掉，观感仍是「沿地块的波浪边」。要得到直线段，
        /// 必须先把锯齿当噪声**删掉**。
        ///
        /// 【容差怎么选（几何上有硬约束）】
        ///   直边上的锯齿振幅          = R - R*cos60° = 0.5R
        ///   孤立单格「角点相对邻角连线」凸起 = 0.5R  ← 与锯齿完全等价
        /// 两者垂距意义上不可区分，所以**没有任何容差能既抹平锯齿又保住孤立六边形的六角形状**。
        /// 抹掉锯齿 ⇒ 孤立单格必然塌成四边形（配上圆角后是个圆润小块）。这是本方案自觉接受的取舍。
        /// 实测窗口：eps &gt; 1.5(=0.5R) 才开始抹锯齿；eps ≥ 2.4(=0.8R) 会连「单格凹口」这类真实特征一起丢。
        /// 取 0.6R 居中。
        /// </summary>
        public static void SimplifyClosed(List<Vector3> loop, float epsilon, List<Vector3> outLoop)
        {
            outLoop.Clear();
            if (loop == null || loop.Count == 0) return;
            if (loop.Count < 4 || epsilon <= 0f) { outLoop.AddRange(loop); return; }

            // 闭环没有天然端点，直接跑 DP 会把「起点附近」永久钉死。
            // 取离 loop[0] 最远的点作第二锚点，拆成两条开链分别简化 —— 两个锚点都在
            // 轮廓极值上，不会落在锯齿中间，简化结果对起点选择不敏感。
            int far = 0;
            float best = -1f;
            for (int i = 1; i < loop.Count; i++)
            {
                float d = SqrXZ(loop[i], loop[0]);
                if (d > best) { best = d; far = i; }
            }
            if (far <= 0) { outLoop.AddRange(loop); return; }

            var chain = new List<Vector3>();
            for (int i = 0; i <= far; i++) chain.Add(loop[i]);
            DouglasPeucker(chain, 0, chain.Count - 1, epsilon, outLoop, false);

            chain.Clear();
            for (int i = far; i < loop.Count; i++) chain.Add(loop[i]);
            chain.Add(loop[0]);
            DouglasPeucker(chain, 0, chain.Count - 1, epsilon, outLoop, false);

            if (outLoop.Count < 3) { outLoop.Clear(); outLoop.AddRange(loop); }
        }

        /// <summary>
        /// 递归 DP。keepLast=false 表示不写入末端点（闭环拼接时由下一条链的首点补上，避免重复）。
        /// 垂距只算 XZ 平面：Y 是地形高度，不参与「轮廓是否弯折」的判断。
        /// </summary>
        private static void DouglasPeucker(
            List<Vector3> pts, int first, int last, float epsilon, List<Vector3> outPts, bool keepLast)
        {
            float dmax = 0f;
            int index = first;
            for (int i = first + 1; i < last; i++)
            {
                float d = PerpDistXZ(pts[i], pts[first], pts[last]);
                if (d > dmax) { dmax = d; index = i; }
            }

            if (dmax > epsilon && index > first)
            {
                DouglasPeucker(pts, first, index, epsilon, outPts, false);
                DouglasPeucker(pts, index, last, epsilon, outPts, keepLast);
            }
            else
            {
                outPts.Add(pts[first]);
                if (keepLast) outPts.Add(pts[last]);
            }
        }

        private static float PerpDistXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6f) return Mathf.Sqrt(SqrXZ(p, a));
            return Mathf.Abs(dz * p.x - dx * p.z + b.x * a.z - b.z * a.x) / len;
        }

        private static float SqrXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>
        /// 定半径圆角：每个顶点替换为一段二次贝塞尔（切点 → 顶点 → 切点）。
        ///
        /// 切点距离 t = radius / tan(θ/2)，并**夹取到相邻边半长**——这保证圆角永不外溢出原多边形，
        /// 即使 radius 远大于边长或遇到极锐角（已用穷举验证 bbox 恒不超出原轮廓）。
        /// 夹取的副作用是锐角处圆角自动变小，正是想要的行为。
        /// </summary>
        public static void RoundCorners(List<Vector3> poly, float radius, int segments, List<Vector3> outPoly)
        {
            outPoly.Clear();
            if (poly == null || poly.Count == 0) return;
            if (poly.Count < 3 || radius <= 0f || segments < 1) { outPoly.AddRange(poly); return; }

            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = poly[i];
                Vector3 a = poly[(i - 1 + n) % n];
                Vector3 b = poly[(i + 1) % n];

                Vector3 v1 = a - p, v2 = b - p;
                float l1 = v1.magnitude, l2 = v2.magnitude;
                if (l1 < 1e-5f || l2 < 1e-5f) { outPoly.Add(p); continue; }

                Vector3 u1 = v1 / l1, u2 = v2 / l2;
                float cosang = Mathf.Clamp(Vector3.Dot(u1, u2), -1f, 1f);
                float ang = Mathf.Acos(cosang);
                // 近共线（无需圆角）与近折返（角平分线无意义）都直接保留原点
                if (ang < 1e-3f || Mathf.Abs(ang - Mathf.PI) < 1e-3f) { outPoly.Add(p); continue; }

                float t = radius / Mathf.Tan(ang * 0.5f);
                t = Mathf.Min(t, l1 * 0.5f, l2 * 0.5f);

                Vector3 t1 = p + u1 * t;
                Vector3 t2 = p + u2 * t;
                for (int s = 0; s <= segments; s++)
                {
                    float f = (float)s / segments;
                    float omf = 1f - f;
                    outPoly.Add(omf * omf * t1 + 2f * omf * f * p + f * f * t2);
                }
            }
        }

        /// <summary>
        /// 闭环相邻点去重（含首尾重合）。圆角段之间在「切点被夹到边中点」时会产出重合点，
        /// 缎带的角平分线在重合点处退化 → 尖刺，故投屏后必须去一遍。
        /// </summary>
        public static void DedupClosed(List<Vector2> loop, float mergeEpsilon, List<Vector2> outLoop)
        {
            outLoop.Clear();
            if (loop == null || loop.Count == 0) return;

            float eps2 = mergeEpsilon * mergeEpsilon;
            for (int i = 0; i < loop.Count; i++)
            {
                if (outLoop.Count == 0 || (loop[i] - outLoop[outLoop.Count - 1]).sqrMagnitude > eps2)
                    outLoop.Add(loop[i]);
            }
            if (outLoop.Count > 1 && (outLoop[0] - outLoop[outLoop.Count - 1]).sqrMagnitude <= eps2)
                outLoop.RemoveAt(outLoop.Count - 1);
        }

        /// <summary>
        /// 闭环 Catmull-Rom 重采样。相邻重复点先去掉，避免样条退化出尖刺。
        /// subdivisions=1 等价于原样返回（只做去重）。
        ///
        /// ⚠️ 当前描边路径**不再走这里**：Catmull-Rom 是插值样条，穿过每个输入点，
        /// 只能把锯齿磨圆、磨不掉（见 SimplifyClosed 注释）。保留它是因为它在
        /// 「已经是直线+圆角的折线」上仍是可用的柔化手段，且有用例覆盖。
        /// </summary>
        public static void SmoothClosed(List<Vector2> loop, int subdivisions, float mergeEpsilon, List<Vector2> outLoop)
        {
            outLoop.Clear();
            if (loop == null || loop.Count == 0) return;

            var pts = new List<Vector2>(loop.Count);
            DedupClosed(loop, mergeEpsilon, pts);

            if (pts.Count < 3 || subdivisions <= 1)
            {
                outLoop.AddRange(pts);
                return;
            }

            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = pts[(i - 1 + n) % n];
                Vector2 p1 = pts[i];
                Vector2 p2 = pts[(i + 1) % n];
                Vector2 p3 = pts[(i + 2) % n];

                for (int s = 0; s < subdivisions; s++)
                {
                    float t = (float)s / subdivisions;
                    outLoop.Add(CatmullRom(p0, p1, p2, p3, t));
                }
            }
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>
        /// 沿闭合折线生成缎带（可带实心芯）。每个路径点出 5 个顶点
        /// （外缘 a=0 / 外芯边 a=1 / 中线 a=1 / 内芯边 a=1 / 内缘 a=0），相邻点之间出 8 个三角形。
        ///
        /// <paramref name="coreRatio"/> = 实心芯占半宽的比例，把「宽度」与「锐度」解耦：
        ///   0   → 芯宽为 0，退化成原来的三列纯羽化缎带（缘 0 → 中线 1，全程渐变）
        ///   0.5 → 中间一半不透明、两侧各 25% 渐变（清晰且不锯齿，推荐）
        ///   1   → 全实心硬边，屏幕空间无 MSAA 时会有锯齿
        /// 芯内三点 alpha 同为 tint.a，故芯是纯色平台；只有芯边到外缘那一截做插值。
        ///
        /// 法线取「前后两段方向的角平分线」，并按转角补偿长度（miter），
        /// 避免拐角处描边变细；补偿上限 miterLimit 防止锐角处针状外溢。
        /// </summary>
        public static void BuildRibbon(
            List<Vector2> path, float halfWidth, Color tint,
            List<Vector2> outVerts, List<Color32> outColors, List<int> outTris,
            float coreRatio = 0f, float miterLimit = 3f)
        {
            int n = path != null ? path.Count : 0;
            if (n < 3 || halfWidth <= 0f) return;

            float core = Mathf.Clamp01(coreRatio);
            int baseIdx = outVerts.Count;
            var edgeColor = (Color32)new Color(tint.r, tint.g, tint.b, 0f);
            var midColor = (Color32)tint;

            for (int i = 0; i < n; i++)
            {
                Vector2 prev = path[(i - 1 + n) % n];
                Vector2 cur = path[i];
                Vector2 next = path[(i + 1) % n];

                Vector2 dIn = (cur - prev).normalized;
                Vector2 dOut = (next - cur).normalized;
                Vector2 bisector = (dIn + dOut);

                Vector2 normal;
                float scale = 1f;
                if (bisector.sqrMagnitude < 1e-8f)
                {
                    // 180° 折返：退化为单段法线
                    normal = new Vector2(-dOut.y, dOut.x);
                }
                else
                {
                    bisector.Normalize();
                    normal = new Vector2(-bisector.y, bisector.x);
                    // miter 长度补偿：cos(半转角) = |dot(bisector, dOut)|
                    float cosHalf = Mathf.Abs(Vector2.Dot(bisector, dOut));
                    scale = cosHalf > 1e-4f ? Mathf.Min(1f / cosHalf, miterLimit) : miterLimit;
                }

                Vector2 offset = normal * (halfWidth * scale);
                Vector2 coreOffset = offset * core;   // core=0 时芯边与中线重合，等价于原三列缎带
                outVerts.Add(cur - offset);     outColors.Add(edgeColor);
                outVerts.Add(cur - coreOffset); outColors.Add(midColor);
                outVerts.Add(cur);              outColors.Add(midColor);
                outVerts.Add(cur + coreOffset); outColors.Add(midColor);
                outVerts.Add(cur + offset);     outColors.Add(edgeColor);
            }

            for (int i = 0; i < n; i++)
            {
                int a = baseIdx + i * 5;
                int b = baseIdx + ((i + 1) % n) * 5;
                // 4 条纵带：外羽化 / 外半芯 / 内半芯 / 内羽化
                for (int k = 0; k < 4; k++)
                {
                    outTris.Add(a + k);     outTris.Add(a + k + 1); outTris.Add(b + k + 1);
                    outTris.Add(a + k);     outTris.Add(b + k + 1); outTris.Add(b + k);
                }
            }
        }
    }
}
