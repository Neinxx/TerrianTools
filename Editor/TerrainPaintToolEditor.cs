using TerrainGPUPaint;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;

[CustomEditor(typeof(TerrainPaintTool))]
public class TerrainPaintToolEditor : Editor
{
    TerrainPaintTool tool;
    bool _isPreviewing = false;
    bool _isDirty = false;
    double _lastPaintTime;
    float[,] _backupHeights;

    protected void OnEnable()
    {
        tool = (TerrainPaintTool)target;
        Spline.Changed += OnSplineChanged;
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    protected void OnDisable()
    {
        Spline.Changed -= OnSplineChanged;
        Undo.undoRedoPerformed -= OnUndoRedo;
        if (_isPreviewing) CancelPreview();
    }

    void OnUndoRedo() { if (_isPreviewing) _isDirty = true; }
    void OnSplineChanged(Spline s, int i, SplineModification m) { if (_isPreviewing) _isDirty = true; }

    public override void OnInspectorGUI()
    {
        EditorGUILayout.Space();
        if (_isPreviewing)
            EditorGUILayout.HelpBox("PREVIEW MODE ACTIVE", MessageType.Warning);

        EditorGUI.BeginChangeCheck();

        // 绘制默认 Inspector
        base.OnInspectorGUI();

        if (EditorGUI.EndChangeCheck() && _isPreviewing)
            _isDirty = true;

        // 如果 Terrain 还是空的，显示一个手动查找按钮
        if (tool.terrain == null)
        {
            EditorGUILayout.HelpBox("No Terrain assigned. Ensure the Spline is within Terrain bounds.", MessageType.Error);
            if (GUILayout.Button("Auto Find Terrain"))
            {
                tool.AutoFindTerrain();
            }
        }

        EditorGUILayout.Space();
        DrawButtons();

        if (_isPreviewing && _isDirty)
        {
            if (EditorApplication.timeSinceStartup - _lastPaintTime > 0.05)
            {
                ExecutePaint();
                _lastPaintTime = EditorApplication.timeSinceStartup;
                _isDirty = false;
            }
        }
    }

    void DrawButtons()
    {
        EditorGUILayout.BeginHorizontal();

        if (!_isPreviewing)
        {
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Start Preview", GUILayout.Height(30)))
            {
                StartPreview();
            }
            GUI.backgroundColor = Color.white;
        }
        else
        {
            GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button("Apply", GUILayout.Height(30)))
            {
                ApplyPreview();
            }
            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("Cancel", GUILayout.Height(30)))
            {
                CancelPreview();
            }
            GUI.backgroundColor = Color.white;
        }

        EditorGUILayout.EndHorizontal();
    }

    void StartPreview()
    {
        // 1. 尝试最后一次自动查找
        if (tool.terrain == null) tool.AutoFindTerrain();

        if (tool.terrain == null || tool.paintShader == null || tool.spline == null)
        {
            Debug.LogError("Setup incomplete. Check Terrain, Shader, or Spline.");
            return;
        }

        var data = tool.terrain.terrainData;
        int res = data.heightmapResolution;
        _backupHeights = data.GetHeights(0, 0, res, res);

        _isPreviewing = true;
        _isDirty = true;
    }

    void CancelPreview()
    {
        if (!_isPreviewing || _backupHeights == null) return;
        tool.terrain.terrainData.SetHeights(0, 0, _backupHeights);
        _backupHeights = null;
        _isPreviewing = false;
        _isDirty = false;
    }

    void ApplyPreview()
    {
        if (!_isPreviewing) return;
        Undo.RegisterCompleteObjectUndo(tool.terrain.terrainData, "Apply Terrain Paint");
        _backupHeights = null;
        _isPreviewing = false;
        _isDirty = false;
        Debug.Log("Applied.");
    }

    void ExecutePaint()
    {
        // 再次检查防止空引用
        if (tool.terrain == null) return;

        var ctx = new TerrainPaintContext
        {
            terrain = tool.terrain,
            terrainData = tool.terrain.terrainData,
            resolution = tool.terrain.terrainData.heightmapResolution,
            terrainPos = tool.terrain.transform.position,
            terrainSize = tool.terrain.terrainData.size
        };

        ctx.sourceHeightRT = ComputeRes.CreateRenderTexture(ctx.resolution, ctx.resolution);
        ctx.targetHeightRT = ComputeRes.CreateRenderTexture(ctx.resolution, ctx.resolution);

        // 使用备份数据重置 Source，防止叠加
        SetTextureFromHeights(ctx.sourceHeightRT, _backupHeights);
        Graphics.Blit(ctx.sourceHeightRT, ctx.targetHeightRT);

        tool.GetPaintParams(out var brush, out var mods);

        var painter = new TerrainPainter(tool.paintShader);
        painter.Paint(ctx, brush, mods);

        ApplyResultToTerrain(ctx);

        ctx.Release();
        foreach (var m in mods) m.Release();
    }

    void SetTextureFromHeights(RenderTexture rt, float[,] heights)
    {
        int w = heights.GetLength(1);
        int h = heights.GetLength(0);

        Texture2D tex = new Texture2D(w, h, TextureFormat.RFloat, false);
        Color[] cols = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                cols[y * w + x] = new Color(heights[y, x], 0, 0);
            }
        }
        tex.SetPixels(cols);
        tex.Apply();

        Graphics.Blit(tex, rt);
        Object.DestroyImmediate(tex);
    }

    void ApplyResultToTerrain(TerrainPaintContext ctx)
    {
        RenderTexture.active = ctx.targetHeightRT;
        Texture2D tex = new Texture2D(ctx.resolution, ctx.resolution, TextureFormat.RFloat, false);
        tex.ReadPixels(new Rect(0, 0, ctx.resolution, ctx.resolution), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        Color[] cols = tex.GetPixels();
        float[,] result = new float[ctx.resolution, ctx.resolution];

        for (int y = 0; y < ctx.resolution; y++)
        {
            for (int x = 0; x < ctx.resolution; x++)
            {
                result[y, x] = cols[y * ctx.resolution + x].r;
            }
        }
        ctx.terrainData.SetHeights(0, 0, result);
        Object.DestroyImmediate(tex);
    }
}