using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TerrainGPUPaint
{
    // --- 1. 枚举定义 ---
    public enum PaintMode
    {
        StrokePath = 0,    // 描边模式 (像修路一样)
        FillPolygon = 1    // 填充模式 (像填湖一样，把闭合区域压平)
    }

    // --- 2. 笔刷定义 (SplineBrush) ---
    [System.Serializable]
    public class SplineBrush : ITerrainBrush
    {
        public SplineContainer splineContainer;
        public PaintMode mode = PaintMode.StrokePath;

        [Min(0.1f)] public float width = 5f;
        [Range(0.1f, 5f)] public float resolutionScale = 1f;

        private ComputeBuffer segmentBuffer;
        private int segmentCount;
        private Vector4 globalBounds; // x,z, x,z

        public Bounds GetWorldBounds()
        {
            if (splineContainer == null) return new Bounds();
            Bounds b = splineContainer.Spline.GetBounds();
            Vector3 center = splineContainer.transform.TransformPoint(b.center);
            Vector3 size = Vector3.Scale(b.size, splineContainer.transform.lossyScale);
            // 将 Y 轴包围盒设得非常高，以适应地形高低差
            size.y = 10000f;
            return new Bounds(center, size + Vector3.one * width * 4);
        }

        public void BuildGPUData()
        {
            if (splineContainer == null) return;
            Release();

            List<GPU_Segment> segments = new List<GPU_Segment>();
            Transform tr = splineContainer.transform;
            float expand = width * 2f;

            // 初始化全局包围盒
            float minX = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxZ = float.MinValue;

            foreach (var spline in splineContainer.Splines)
            {
                float len = spline.GetLength();
                int steps = Mathf.CeilToInt(len * resolutionScale);
                if (steps < 2) steps = 2;

                Vector3 prevPos = tr.TransformPoint(spline.EvaluatePosition(0));

                for (int i = 1; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    Vector3 currPos = tr.TransformPoint(spline.EvaluatePosition(t));

                    // 更新全局包围盒
                    minX = Mathf.Min(minX, Mathf.Min(prevPos.x, currPos.x));
                    minZ = Mathf.Min(minZ, Mathf.Min(prevPos.z, currPos.z));
                    maxX = Mathf.Max(maxX, Mathf.Max(prevPos.x, currPos.x));
                    maxZ = Mathf.Max(maxZ, Mathf.Max(prevPos.z, currPos.z));

                    // 构建 Segment
                    Vector3 min = Vector3.Min(prevPos, currPos);
                    Vector3 max = Vector3.Max(prevPos, currPos);

                    segments.Add(new GPU_Segment
                    {
                        p0 = new float4(prevPos, 0),
                        p1 = new float4(currPos, 0),
                        // 忽略 Y 轴高度差异
                        min = new float4(min.x - expand, -100000, min.z - expand, 0),
                        max = new float4(max.x + expand, +100000, max.z + expand, 0)
                    });

                    prevPos = currPos;
                }
            }

            globalBounds = new Vector4(minX, minZ, maxX, maxZ);

            segmentCount = segments.Count;
            if (segmentCount > 0)
            {
                segmentBuffer = new ComputeBuffer(segmentCount, Marshal.SizeOf<GPU_Segment>());
                segmentBuffer.SetData(segments);
            }
        }

        public void Bind(ComputeShader cs, int kernel)
        {
            if (segmentBuffer != null)
            {
                cs.SetBuffer(kernel, "_Segments", segmentBuffer);
                cs.SetInt("_SegmentsCount", segmentCount);
                cs.SetVector("_GlobalBounds", globalBounds);
            }
            cs.SetFloat("_BrushWidth", width);
            cs.SetInt("_PaintMode", (int)mode);
        }

        public void Release()
        {
            ComputeRes.Release(ref segmentBuffer);
        }
    }

    // --- 3. 坡度修改器 (BankModifier) ---
    [System.Serializable]
    public class BankModifier : IBrushModifier
    {
        public float bankWidth = 10f;
        public AnimationCurve bankCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        private Texture2D _curveTex;

        public void Bind(ComputeShader cs, int kernel)
        {
            if (_curveTex == null)
            {
                _curveTex = new Texture2D(256, 1, TextureFormat.RFloat, false, true);
                _curveTex.wrapMode = TextureWrapMode.Clamp;
            }

            for (int i = 0; i < 256; i++)
            {
                float t = i / 255f;
                float v = bankCurve.Evaluate(t);
                _curveTex.SetPixel(i, 0, new Color(v, 0, 0, 0));
            }
            _curveTex.Apply();

            cs.SetFloat("_BankWidth", bankWidth);
            cs.SetTexture(kernel, "_BankCurve", _curveTex);
        }

        public void Release()
        {
            ComputeRes.Release(ref _curveTex);
        }
    }

    // --- 4. 高度修改器 (HeightModifier) ---
    [System.Serializable]
    public class HeightModifier : IBrushModifier
    {
        public float heightOffset = 0f;

        public void Bind(ComputeShader cs, int kernel)
        {
            cs.SetFloat("_HeightOffset", heightOffset);
        }

        public void Release() { }
    }
}