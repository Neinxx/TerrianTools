using UnityEngine;
using Unity.Mathematics;
using System.Runtime.InteropServices;

namespace TerrainGPUPaint
{
    // --- 资源管理辅助类 ---
    public static class ComputeRes
    {
        public static void Release(ref ComputeBuffer buffer) { if (buffer != null) { buffer.Release(); buffer = null; } }
        public static void Release(ref RenderTexture rt) { if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); rt = null; } }
        public static void Release(ref Texture2D tex) { if (tex != null) { Object.DestroyImmediate(tex); tex = null; } }

        public static RenderTexture CreateRenderTexture(int w, int h)
        {
            RenderTexture rt = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat);
            rt.enableRandomWrite = true;
            rt.filterMode = FilterMode.Bilinear;
            rt.Create();
            return rt;
        }

        public static void Dispatch(ComputeShader cs, int kernel, int w, int h)
        {
            uint x, y, z;
            cs.GetKernelThreadGroupSizes(kernel, out x, out y, out z);
            cs.Dispatch(kernel, Mathf.CeilToInt(w / (float)x), Mathf.CeilToInt(h / (float)y), 1);
        }
    }

    // --- GPU 数据结构 (对应 Shader) ---
    [StructLayout(LayoutKind.Sequential)]
    public struct GPU_Segment
    {
        public float4 p0;
        public float4 p1;
        public float4 min;
        public float4 max;
    }

    // --- 绘制上下文 ---
    public sealed class TerrainPaintContext
    {
        public Terrain terrain;
        public TerrainData terrainData;
        public int resolution;
        public Vector3 terrainPos;
        public Vector3 terrainSize;

        public RenderTexture sourceHeightRT;  // 原始 (或备份) 高度
        public RenderTexture targetHeightRT;  // 计算结果

        public void Release()
        {
            ComputeRes.Release(ref sourceHeightRT);
            ComputeRes.Release(ref targetHeightRT);
        }
    }

    // --- 接口定义 ---
    public interface ITerrainBrush
    {
        void BuildGPUData();
        void Bind(ComputeShader cs, int kernel);
        void Release();
        Bounds GetWorldBounds();
    }

    public interface IBrushModifier
    {
        void Bind(ComputeShader cs, int kernel);
        void Release();
    }
}