using UnityEngine;

/// <summary>
/// One-drop runtime bootstrap:
/// - Webcam background (UGUI)
/// - Hide SMPL body mesh (keep bones)
/// - Garment manager + UI selector
/// 
/// Designed to avoid touching the existing pose pipeline.
/// </summary>
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

    void Awake()
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

        if (showWebcamBackground)
        {
            if (GetComponent<WebcamBackgroundUGUI>() == null)
                gameObject.AddComponent<WebcamBackgroundUGUI>();
        }

        if (GetComponent<GarmentSelectorUIRuntime>() == null)
            gameObject.AddComponent<GarmentSelectorUIRuntime>();
    }

    void Start()
    {
        if (garmentManager != null && garmentManager.HasCatalog && autoSelectIndex >= 0)
            garmentManager.TrySetActive(autoSelectIndex);
    }
}

