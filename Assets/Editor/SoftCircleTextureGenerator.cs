using System.IO;
using UnityEditor;
using UnityEngine;

// One-off utility: generates a soft radial-gradient circle texture (white, fading to transparent
// at the edges) for use as a particle sprite. Run via Tools > Generate Soft Circle Particle
// Texture, then drag the resulting asset into a particle material's texture slot.
public static class SoftCircleTextureGenerator
{
    [MenuItem("Tools/Generate Soft Circle Particle Texture")]
    public static void Generate()
    {
        const int size = 64;
        var tex    = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var center = new Vector2(size / 2f, size / 2f);

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dist  = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / (size / 2f);
            float alpha = Mathf.Clamp01(1f - dist);
            alpha *= alpha; // soften the falloff so it reads as a glow, not a hard-edged disc
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
        }
        tex.Apply();

        const string dir  = "Assets/Textures";
        const string path = dir + "/SoftCircleParticle.png";
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, tex.EncodeToPNG());

        AssetDatabase.Refresh();

        // Reasonable import settings for a particle sprite: has alpha, no mip banding, no wrap artifacts.
        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
        {
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled       = false;
            importer.wrapMode            = TextureWrapMode.Clamp;
            importer.filterMode          = FilterMode.Bilinear;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        Debug.Log($"[SoftCircleTextureGenerator] Wrote {path}");
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
