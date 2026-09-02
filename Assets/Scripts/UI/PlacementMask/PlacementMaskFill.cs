using System.Collections.Generic;
using UnityEngine;

namespace UI.PlacementMask
{
    //****************************************
    // 不可放置区域遮罩 · 填充几何（扫描线 + 偶奇规则，屏幕空间 2D）。
    //
    // 【为什么不再逐格扇形填充】
    // 旧填充用 PlacementMaskTopology 的原始六边形角点逐格扇形三角化，而描边走的是
    // 「Douglas-Peucker 简化 → 定半径圆角」之后的平滑路径 —— 两者是**两套不同的几何**：
    //   凹口处 DP 会把路径切到填充之外 → 描边内侧露出没被填充的空白；
    //   凸角处圆角把角切掉      → 填充反而溢出到描边之外。
    // 偏移量最大可达大半个格，远超描边半宽，靠调粗线盖不住。
    // 改为「填充与描边共用同一条处理后的闭环」，两层几何同源，任何参数下都严丝合缝。
    //
    // 【为什么是扫描线，不是耳切】
    // 闭环里既有外环也有洞环（洞 = 被不可放置格包住的可放置孤岛），洞必须真的挖空。
    // 课本做法是「嵌套判定 → 桥接 → 耳切」，实测在这个工程里非常脆：桥接会把洞缝成
    // 「弱简单多边形」（同一位置出现重复顶点、零宽通道），耳切在缝合口附近会走到
    // 整圈顶点全为凹的中间状态，一个耳都找不到 → 剩余顶点连同面积被静默丢弃、
    // 已切出的三角互相重叠，填充面积反而比外环还大（多洞随机用例稳定 9/4000 命中）。
    //
    // 扫描线把问题整个绕开：
    //   1) 取所有顶点的 y 排序去重，相邻两个 y 之间是一条「带」。带内没有任何顶点，
    //      故每条边要么整段穿过这条带、要么完全不碰 —— 穿带边的集合在带内恒定。
    //   2) 带中线上求各穿带边的 x，排序后**按偶奇规则两两配对**：第 0-1 个 x 之间是实心，
    //      1-2 之间是空的，2-3 之间又是实心……洞就这么自动挖掉了。
    //   3) 每对 x 在带上下沿各取一次 → 一个梯形 → 2 个三角。
    //
    // 于是这些全都不需要了：嵌套深度判定、绕向归一化、桥接可见性、耳切、凹凸判定。
    // 偶奇规则天然处理：任意绕向、任意嵌套层数（洞里的岛、岛里的洞）、互不相交的多个区域。
    // 输出必然无重叠（带与带不交、带内各段不交），故半透明填充可直接用普通 alpha 混合。
    //
    // 代价是三角数变多（每带每段 2 个，薄梯形），但这是每次重建才跑一遍的 UI 遮罩，
    // 量级在千级三角，可以接受；换来的是「不会再有静默丢面积」这一条硬保证。
    //
    // 全部工作缓冲都是实例字段：提起态下相机每动就重建一次，不能每帧产生 GC。
    //****************************************
    public sealed class PlacementMaskFill
    {
        // 带高低于此值视为退化，跳过（避免除零与零面积三角）
        private const float MinBandHeight = 1e-5f;
        // 梯形上下沿都窄于此值视为退化，跳过
        private const float MinSpanWidth = 1e-5f;

        private readonly List<float> _ys = new List<float>();
        // 穿过当前带的边，三元组并行存放：带中线处的 x（用于排序配对）+ 上下沿的 x
        private readonly List<float> _xMid = new List<float>();
        private readonly List<float> _xLow = new List<float>();
        private readonly List<float> _xHigh = new List<float>();
        private readonly List<int> _order = new List<int>();

        private readonly System.Comparison<int> _cmpByX;

        public PlacementMaskFill()
        {
            // 委托在构造期一次性分配，避免每帧排序时产生闭包垃圾。
            _cmpByX = (a, b) => _xMid[a].CompareTo(_xMid[b]);
        }

        /// <summary>
        /// 把一组闭环（含外环与洞环，绕向不限）三角化成一张无重叠 mesh。
        /// 输入即描边用的同一批处理后闭环，故填充边界与描边中线逐点重合。
        /// 内外由**偶奇规则**判定：一条射线穿过奇数条边即在区域内。
        /// </summary>
        public void Triangulate(List<List<Vector2>> loops, List<Vector2> outVerts, List<int> outTris)
        {
            outVerts.Clear();
            outTris.Clear();
            if (loops == null || loops.Count == 0) return;

            CollectBandBoundaries(loops);
            if (_ys.Count < 2) return;

            for (int b = 0; b + 1 < _ys.Count; b++)
            {
                float yLow = _ys[b];
                float yHigh = _ys[b + 1];
                if (yHigh - yLow < MinBandHeight) continue;

                CollectCrossings(loops, yLow, yHigh);
                EmitSpans(yLow, yHigh, outVerts, outTris);
            }
        }

        /// <summary>
        /// 所有顶点的 y，排序去重 —— 相邻两个 y 之间即一条「带」。
        /// 带内保证没有顶点，故穿带边的集合在整条带上恒定，这正是配对能成立的前提。
        /// </summary>
        private void CollectBandBoundaries(List<List<Vector2>> loops)
        {
            _ys.Clear();
            for (int i = 0; i < loops.Count; i++)
            {
                List<Vector2> loop = loops[i];
                if (loop == null || loop.Count < 3) continue;
                for (int k = 0; k < loop.Count; k++) _ys.Add(loop[k].y);
            }
            if (_ys.Count == 0) return;

            _ys.Sort();
            int w = 1;
            for (int r = 1; r < _ys.Count; r++)
                if (_ys[r] - _ys[w - 1] > MinBandHeight) _ys[w++] = _ys[r];
            _ys.RemoveRange(w, _ys.Count - w);
        }

        /// <summary>
        /// 求所有穿过 [yLow, yHigh] 这条带的边，记下它们在带中线与上下沿处的 x。
        /// 半开区间 [minY, maxY) 判定：顶点恰在带边界上时只算一次，避免同一条边被数两遍
        /// 而破坏偶奇的奇偶性。水平边不参与（它对内外交替没有贡献）。
        /// </summary>
        private void CollectCrossings(List<List<Vector2>> loops, float yLow, float yHigh)
        {
            _xMid.Clear();
            _xLow.Clear();
            _xHigh.Clear();

            float yMid = (yLow + yHigh) * 0.5f;

            for (int i = 0; i < loops.Count; i++)
            {
                List<Vector2> loop = loops[i];
                if (loop == null || loop.Count < 3) continue;

                int n = loop.Count;
                for (int k = 0, j = n - 1; k < n; j = k++)
                {
                    Vector2 a = loop[j], b = loop[k];
                    float dy = b.y - a.y;
                    if (dy > -MinBandHeight && dy < MinBandHeight) continue;   // 水平边

                    float minY = a.y < b.y ? a.y : b.y;
                    float maxY = a.y < b.y ? b.y : a.y;
                    if (yMid < minY || yMid >= maxY) continue;

                    float inv = 1f / dy;
                    _xMid.Add(a.x + (yMid - a.y) * (b.x - a.x) * inv);
                    _xLow.Add(a.x + (yLow - a.y) * (b.x - a.x) * inv);
                    _xHigh.Add(a.x + (yHigh - a.y) * (b.x - a.x) * inv);
                }
            }
        }

        /// <summary>
        /// 按 x 排序后两两配对（偶奇规则）：第 0-1 段实心、1-2 段空、2-3 段实心……
        /// 每段在带上下沿各取一次 → 梯形 → 2 个三角。
        /// 相邻带共用同一个 y 且 x 由同一条边线性插值得出，故带与带之间严丝合缝、不会露缝。
        /// </summary>
        private void EmitSpans(float yLow, float yHigh, List<Vector2> outVerts, List<int> outTris)
        {
            int m = _xMid.Count;
            if (m < 2) return;

            _order.Clear();
            for (int i = 0; i < m; i++) _order.Add(i);
            _order.Sort(_cmpByX);

            for (int s = 0; s + 1 < m; s += 2)
            {
                int li = _order[s];
                int ri = _order[s + 1];

                float xl0 = _xLow[li], xr0 = _xLow[ri];
                float xl1 = _xHigh[li], xr1 = _xHigh[ri];

                // 上下沿都退化成一个点 → 零面积，跳过（单点接触的尖角）
                if (xr0 - xl0 < MinSpanWidth && xr1 - xl1 < MinSpanWidth) continue;

                int v = outVerts.Count;
                outVerts.Add(new Vector2(xl0, yLow));
                outVerts.Add(new Vector2(xr0, yLow));
                outVerts.Add(new Vector2(xr1, yHigh));
                outVerts.Add(new Vector2(xl1, yHigh));

                outTris.Add(v); outTris.Add(v + 1); outTris.Add(v + 2);
                outTris.Add(v); outTris.Add(v + 2); outTris.Add(v + 3);
            }
        }

        /// <summary>有向面积（正 = CCW）。供测试与诊断使用。</summary>
        public static float SignedArea(List<Vector2> poly)
        {
            float s = 0f;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
                s += (poly[j].x * poly[i].y) - (poly[i].x * poly[j].y);
            return s * 0.5f;
        }
    }
}
