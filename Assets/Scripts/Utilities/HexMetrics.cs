using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//****************************************
//创建人：易生
//功能说明：用于六边形地格的数学测量
//    1、噪声扰动
//****************************************

public class HexMetrics
{
    // 噪声图
    public static Texture2D noiseSource;
    // 噪声采样频率，值越大相邻地块的视觉高程差异越明显。
    public static float noiseScale = 0.003f;
    // 平面扰动强度（教程中从5调整为4）
    public const float cellPerturbStrength = 5f;
    // 竖直（高程）扰动强度，有界。由 MapGenerator 从 elevationStep * verticalPerturbRatio 注入，
    // 保证每格 Y 抖动夹在 ±yPerturbStrength 内，水下不捅穿海面、陆地不沉水。
    public static float yPerturbStrength = 1.2f;
    public static Vector4 SampleNoise(Vector3 position)
    {
        // 用世界坐标X*缩放系数 作为UV.x，Z*缩放系数 作为UV.y
        // GetPixelBilinear：双线性滤波采样（使噪声过渡更平滑）
        return noiseSource.GetPixelBilinear(
            position.x * noiseScale,
            position.z * noiseScale
        );
    }

    // 扰动方法：输入原始顶点位置，返回扰动后位置（仅 XZ，Y 由 PerturbY2 单独管理）
    public static Vector3 Perturb(Vector3 position)
    {
        // 1. 采样噪声：获取当前顶点位置对应的四通道噪声数据（Vector4）
        Vector4 noiseSample = HexMetrics.SampleNoise(position);

        //扰动上限
        float upperLimit = 0.2f;
        // 2. 仅扰动 XZ，不动 Y——Y 高程统一由 PerturbY2 的有界连续公式控制，
        //    避免大振幅取模产生锯齿状坑洼感。
        position.x += ((noiseSample.x * 2f - 1f) * HexMetrics.cellPerturbStrength) % upperLimit; // R通道→X轴
        position.z += ((noiseSample.z * 2f - 1f) * HexMetrics.cellPerturbStrength) % upperLimit; // B通道→Z轴

        // 3. 返回扰动后的顶点位置
        return position;
    }

    // 扰动方法：输入原始顶点位置，返回扰动后位置
    public static Vector3 PerturbXZ(Vector3 position)
    {
        // 1. 采样噪声：获取当前顶点位置对应的四通道噪声数据（Vector4）
        Vector4 noiseSample = HexMetrics.SampleNoise(position);

        //扰动上限
        float upperLimit = 0.2f;
        //倍率
        //float magnification = 7;
        // 2. 通道映射+范围调整：生成X、Z轴扰动（Y轴初期禁用，保持单元格平整）
        position.x += ((noiseSample.x * 2f - 1f) * HexMetrics.cellPerturbStrength) % upperLimit; // R通道→X轴
        //position.y += ((noiseSample.y * 2f - 1f) * HexMetrics.cellPerturbStrength * magnification) % upperLimit; // G通道→Y轴（注释，暂不扰动）
        position.z += ((noiseSample.z * 2f - 1f) * HexMetrics.cellPerturbStrength) % upperLimit; // B通道→Z轴

        // 3. 返回扰动后的顶点位置（包含噪声信息的Vector3）
        //print("扰动强度：" + new Vector3(((noiseSample.x * 2f - 1f) * HexMetrics.cellPerturbStrength) % 0.25f, ((noiseSample.y * 2f - 1f) * HexMetrics.cellPerturbStrength * 7) % 0.25f, ((noiseSample.z * 2f - 1f) * HexMetrics.cellPerturbStrength) % 0.25f));
        return position;
    }

    // 高程扰动方法：输入原始顶点位置，返回高程（y）扰动后的位置
    public static Vector3 PerturbY(Vector3 position)
    {
        // 1. 采样噪声：获取当前顶点位置对应的四通道噪声数据（Vector4）
        Vector4 noiseSample = HexMetrics.SampleNoise(position);

        // 2. 通道映射+范围调整
        position.y += (noiseSample.y * 2f - 1f) * HexMetrics.cellPerturbStrength; // G通道→Y轴（注释，暂不扰动）

        // 3. 返回扰动后的顶点位置（包含噪声信息的Vector3）
        return position;
    }

    // 高程扰动方法2：输入原始顶点位置，返回高程（y）
    public static Vector3 PerturbY2(Vector3 position)
    {
        // 1. 采样噪声：获取当前顶点位置对应的四通道噪声数据（Vector4）
        Vector4 noiseSample = HexMetrics.SampleNoise(position);

        // 调用方会把该值加到已包含基础高度的顶点上，因此这里只返回偏移量。
        // 若返回 position.y + offset，会把 Height * elevationStep 重复叠加一次。
        float yOffset = (noiseSample.y * 2f - 1f) * HexMetrics.yPerturbStrength;

        return new Vector3(0f, yOffset, 0f);
    }
}
