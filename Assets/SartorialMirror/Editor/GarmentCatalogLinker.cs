#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace SartorialMirror.Editor
{
    /// <summary>
    /// Points <see cref="GarmentCatalog"/> entry 0 at the Blender-prepared SMPL-skinned FBX.
    /// Menu: SartorialMirror / Link prepared Flannel FBX to GarmentCatalog
    /// Batch: Unity -batchmode -quit -projectPath ... -executeMethod SartorialMirror.Editor.GarmentCatalogLinker.Link
    /// </summary>
    public static class GarmentCatalogLinker
    {
        private const string CatalogPath = "Assets/SartorialMirror/GarmentCatalog.asset";
        private const string PreparedFbx = "Assets/garments_prepared/Flannel_OriginalRig_Drive.fbx";

        [MenuItem("SartorialMirror/Link prepared Flannel FBX to GarmentCatalog")]
        public static void Link()
        {
            bool ok = TryLink();
            if (Application.isBatchMode)
                EditorApplication.Exit(ok ? 0 : 1);
            else if (!ok)
                EditorUtility.DisplayDialog("GarmentCatalog", "Link failed — see Console.", "OK");
        }

        static bool TryLink()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<GarmentCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError("[GarmentCatalogLinker] Missing catalog at " + CatalogPath);
                return false;
            }

            if (catalog.garments == null || catalog.garments.Count == 0)
            {
                Debug.LogError("[GarmentCatalogLinker] Catalog has no garment entries.");
                return false;
            }

            var modelRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PreparedFbx);
            if (modelRoot == null)
            {
                Debug.LogError("[GarmentCatalogLinker] Missing model at " + PreparedFbx + ". Reimport project or run Blender export into this path.");
                return false;
            }

            catalog.garments[0].displayName = "Flannel Shirt (SMPL skinned)";
            catalog.garments[0].garmentPrefab = modelRoot;

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[GarmentCatalogLinker] Linked catalog entry 0 to " + PreparedFbx, catalog);
            return true;
        }
    }
}
#endif
