using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// One-drop runtime bootstrap:
/// - Webcam background (UGUI)
/// - Hide SMPL body mesh (keep bones)
/// - Garment manager + UI selector
/// 
/// Designed to avoid touching the existing pose pipeline.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class SartorialMirrorBootstrap : MonoBehaviour
{
    [Header("SMPL")]
    public Transform smplRoot;
    public string smplRootName = "SMPL_neutral_rig_GOLDEN";

    [Header("Garments")]
    public GarmentCatalog garmentCatalog;
    public int autoSelectIndex = 0;

    [Header("Presentation")]
    public bool showWebcamBackground = true;
    public bool hideBodyMesh = true;

    private SmplGarmentManager garmentManager;

    void OnEnable()
    {
        DisableDuplicateBootstraps();
        EnsureComponents();
    }

    void Awake()
    {
        DisableDuplicateBootstraps();
        EnsureComponents();
    }

    void DisableDuplicateBootstraps()
    {
        // Your screenshot shows multiple garments spawned, with one frozen.
        // The only way that happens in this project is: multiple bootstraps/managers are active in the same scene,
        // each spawning its own garment instance and driving different skeleton references.
        var all = FindObjectsOfType<SartorialMirrorBootstrap>(true);
        if (all == null || all.Length <= 1) return;

        SartorialMirrorBootstrap keep = null;
        foreach (var b in all)
        {
            if (b != null && b.isActiveAndEnabled)
            {
                keep = b;
                break;
            }
        }
        if (keep == null) keep = all[0];

        if (keep != this)
        {
            if (Application.isPlaying)
                Debug.LogWarning($"[SartorialMirrorBootstrap] Disabling duplicate bootstrap on '{gameObject.name}'. Keeping '{keep.gameObject.name}'.", this);
            enabled = false;
        }
    }

    void EnsureComponents()
    {
        garmentManager = GetComponent<SmplGarmentManager>();
        if (garmentManager == null) garmentManager = gameObject.AddComponent<SmplGarmentManager>();

        // Always install the finite guard in play mode. This prevents Unity GUI assertion spam if any upstream
        // joint source emits NaN/Inf and makes the project debuggable without manual scene edits.
        if (Application.isPlaying)
        {
            var guard = GetComponent<FiniteTransformGuard>();
            if (guard == null) guard = gameObject.AddComponent<FiniteTransformGuard>();
            guard.root = smplRoot != null ? smplRoot : null;
            guard.checkEveryFrame = true;
            guard.disableOffendingComponents = true;
        }

        garmentManager.smplRoot = smplRoot;
        garmentManager.smplRootName = smplRootName;
        garmentManager.catalog = garmentCatalog;

#if UNITY_EDITOR
        AutoLinkGarmentCatalogInEditor();
#endif

        if (hideBodyMesh)
        {
            var hider = GetComponent<SmplBodyMeshHider>();
            if (hider == null) hider = gameObject.AddComponent<SmplBodyMeshHider>();
            hider.smplRoot = smplRoot;
        }
        else
        {
            var hider = GetComponent<SmplBodyMeshHider>();
            if (hider != null && Application.isPlaying == false)
                hider.enabled = false;
        }

        if (showWebcamBackground)
        {
            if (GetComponent<WebcamBackgroundUGUI>() == null)
                gameObject.AddComponent<WebcamBackgroundUGUI>();
        }

        if (GetComponent<GarmentSelectorUIRuntime>() == null)
            gameObject.AddComponent<GarmentSelectorUIRuntime>();

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(gameObject);
#endif
    }

#if UNITY_EDITOR
    void AutoLinkGarmentCatalogInEditor()
    {
        if (Application.isPlaying) return;
        // Convenience only: auto-assign the catalog reference on this scene object (does NOT modify the catalog asset).
        if (garmentCatalog == null)
        {
            const string catalogPath = "Assets/SartorialMirror/GarmentCatalog.asset";
            garmentCatalog = AssetDatabase.LoadAssetAtPath<GarmentCatalog>(catalogPath);
            if (garmentCatalog != null && garmentManager != null)
                garmentManager.catalog = garmentCatalog;
        }

        // IMPORTANT:
        // Do not auto-overwrite GarmentCatalog entries from code.
        // GarmentCatalog asset references are GUID-based and are the source of truth for which prepared FBX
        // Unity will spawn. Any "helpful" auto-link here can silently point the catalog at an old FBX and
        // make runtime diagnostics look like the weight transfer/export failed.
        //
        // If you need to change which FBX is used, edit `Assets/SartorialMirror/GarmentCatalog.asset` directly.
        if (garmentCatalog == null) return;
    }
#endif

    void Start()
    {
        if (garmentManager == null) return;
        if (autoSelectIndex < 0) return;

        if (!garmentManager.HasCatalog)
        {
            Debug.LogWarning("SartorialMirrorBootstrap: Garment catalog is missing or empty. Assign `Assets/SartorialMirror/GarmentCatalog.asset` in the scene.", this);
            return;
        }

        if (!garmentManager.TrySetActive(autoSelectIndex))
            Debug.LogWarning($"SartorialMirrorBootstrap: autoSelectIndex {autoSelectIndex} failed to spawn garment. See prior warnings for the reason.", this);
    }
}

