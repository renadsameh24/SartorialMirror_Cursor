using UnityEngine;

public static class GarmentMaterialTint
{
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int Color = Shader.PropertyToID("_Color");

    public static void Apply(GameObject garmentRoot, UnityEngine.Color tint)
    {
        if (garmentRoot == null) return;

        // Use sharedMaterials to avoid instantiating per-renderer materials at runtime.
        // This means colorways affect the shared material reference for this instance only
        // as long as Unity has duplicated materials on import; if not, you can switch to
        // renderer.materials at the cost of allocations.
        var renderers = garmentRoot.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (!r) continue;

            var mats = r.materials; // instance materials (safe per garment instance)
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

