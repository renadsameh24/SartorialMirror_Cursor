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
        EnsureComponents();
    }

    void Awake()
    {
        EnsureComponents();
    }

    void EnsureComponents()
    {
        garmentManager = GetComponent<SmplGarmentManager>();
        if (garmentManager == null) garmentManager = gameObject.AddComponent<SmplGarmentManager>();

        garmentManager.smplRoot = smplRoot;
        garmentManager.smplRootName = smplRootName;
        garmentManager.catalog = garmentCatalog;

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

