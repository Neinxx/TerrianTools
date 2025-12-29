using System.Collections.Generic;
using TerrainGPUPaint;
using UnityEngine;
using UnityEngine.Splines;

// --- 绘制执行器 (无需修改，保持原样) ---
namespace TerrainGPUPaint
{
    public class TerrainPainter
    {
        private ComputeShader _shader;
        public TerrainPainter(ComputeShader shader) => _shader = shader;

        public void Paint(TerrainPaintContext ctx, ITerrainBrush brush, List<IBrushModifier> modifiers)
        {
            if (ctx == null || brush == null || _shader == null) return;

            int kernel = _shader.FindKernel("CSMain");

            // 安全检查：如果 Shader 没编译好，Kernel 会无效
            if (kernel < 0)
            {
                Debug.LogError("ComputeShader kernel 'CSMain' not found. Check shader compilation errors.");
                return;
            }

            _shader.SetVector("_TerrainSize", ctx.terrainSize);
            _shader.SetVector("_TerrainPos", ctx.terrainPos);
            _shader.SetInt("_Resolution", ctx.resolution);

            _shader.SetTexture(kernel, "_SourceHeight", ctx.sourceHeightRT);
            _shader.SetTexture(kernel, "_ResultHeight", ctx.targetHeightRT);

            brush.BuildGPUData();
            brush.Bind(_shader, kernel);

            foreach (var m in modifiers) m.Bind(_shader, kernel);

            ComputeRes.Dispatch(_shader, kernel, ctx.resolution, ctx.resolution);
            brush.Release();
        }
    }
}

// --- 主工具脚本 (已更新自动查找逻辑) ---
public class TerrainPaintTool : MonoBehaviour
{
    [Header("Settings")]
    public ComputeShader paintShader;

    [Header("Auto-Reference")]
    public Terrain terrain; // 自动获取

    [Header("Brush")]
    public SplineContainer spline;
    public SplineBrush brushSettings = new SplineBrush();

    [Header("Modifiers")]
    public HeightModifier heightMod = new HeightModifier();
    public BankModifier bankMod = new BankModifier();

    private void OnValidate()
    {
        if (spline == null) spline = GetComponent<SplineContainer>();
        // 尝试自动查找地形
        if (terrain == null) AutoFindTerrain();

        brushSettings.splineContainer = spline;
    }

    /// <summary>
    /// 自动查找 Spline 所在的地形
    /// </summary>
    public void AutoFindTerrain()
    {
        if (spline == null) return;

        // 获取 Spline 的世界坐标中心（近似）
        Vector3 checkPos = spline.transform.position;
        // 如果有 knot，用第一个 knot 的位置更准
        if (spline.Splines.Count > 0 && spline.Splines[0].Count > 0)
        {
            checkPos = spline.transform.TransformPoint(spline.Splines[0][0].Position);
        }

        // 遍历所有激活的地形
        if (Terrain.activeTerrains != null)
        {
            foreach (var t in Terrain.activeTerrains)
            {
                Vector3 tPos = t.transform.position;
                Vector3 tSize = t.terrainData.size;

                // 简单的 2D 包围盒检测 (忽略 Y 轴，因为样条线可能悬空)
                if (checkPos.x >= tPos.x && checkPos.x <= tPos.x + tSize.x &&
                    checkPos.z >= tPos.z && checkPos.z <= tPos.z + tSize.z)
                {
                    terrain = t;
                    // Debug.Log($"[TerrainPaintTool] Auto-assigned terrain: {t.name}");
                    return;
                }
            }
        }
    }

    public void GetPaintParams(out ITerrainBrush outBrush, out List<IBrushModifier> outMods)
    {
        brushSettings.splineContainer = spline;
        outBrush = brushSettings;
        outMods = new List<IBrushModifier> { heightMod, bankMod };
    }
}