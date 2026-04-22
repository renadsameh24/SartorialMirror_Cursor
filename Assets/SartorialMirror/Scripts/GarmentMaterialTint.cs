using UnityEngine;

public static class GarmentMaterialTint
{
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int Color = Shader.PropertyToID("_Color");

    public static void Apply(GameObject garmentRoot, UnityEngine.Color tint)
    {
        if (garmentRoot == null) return;

        // In Edit Mode, touching Renderer.material(s) instantiates/leaks materials into the scene.
        // In Play Mode, we want per-instance materials so tinting doesn't affect other instances.
        var renderers = garmentRoot.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (!r) continue;

            var mats = Application.isPlaying ? r.materials : r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (!m) continue;

                if (m.HasProperty(BaseColor))
                    m.SetColor(BaseColor, tint);
                if (m.HasProperty(Color))
                    m.SetColor(Color, tint);
            }
        }
    }
}

