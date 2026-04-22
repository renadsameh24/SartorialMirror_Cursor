using UnityEditor;

namespace SartorialMirror.EditorTools
{
    /// <summary>
    /// Ensures the prepared garment FBX imports with Read/Write enabled.
    /// Required for runtime mesh vertex scaling when using Remap mode (bones are external to garment root).
    /// </summary>
    public sealed class GarmentImporterEnforcer : AssetPostprocessor
    {
        private const string PreparedGarmentPath = "Assets/garments_prepared/Flannel_SMPL_Skinned.fbx";

        void OnPreprocessModel()
        {
            if (assetPath != PreparedGarmentPath) return;

            if (assetImporter is not ModelImporter mi) return;

            // Needed so runtime can read/scale vertices safely.
            if (!mi.isReadable)
            {
                mi.isReadable = true;
            }

            // Keep consistent with repo expectations.
            mi.globalScale = 1f;
            mi.preserveHierarchy = true;
            mi.optimizeBones = false;
            mi.maxBonesPerVertex = 4;
        }
    }
}

