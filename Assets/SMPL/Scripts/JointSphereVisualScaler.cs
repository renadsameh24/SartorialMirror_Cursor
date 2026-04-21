using UnityEngine;

public class JointSphereVisualScaler : MonoBehaviour
{
    [Header("Root that contains the joint transforms (J_pelvis, J_spine, etc.)")]
    public Transform jointSpheresRoot;

    [Header("Visual sphere size in meters (world-ish). Try 0.03 to 0.06)")]
    public float visualDiameter = 0.05f;

    [Header("Run once on Play")]
    public bool applyOnStart = true;

    void Start()
    {
        if (applyOnStart) Apply();
    }

    [ContextMenu("Apply")]
    public void Apply()
    {
        if (!jointSpheresRoot)
        {
            Debug.LogError("JointSphereVisualScaler: jointSpheresRoot is not set.");
            return;
        }

        foreach (Transform joint in jointSpheresRoot)
        {
            // Keep joint transform clean (important!)
            joint.localScale = Vector3.one;

            // Find or create Viz child
            Transform viz = joint.Find("Viz");
            if (!viz)
            {
                GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                g.name = "Viz";
                // remove collider, purely visual
                var col = g.GetComponent<Collider>();
                if (col) Destroy(col);

                viz = g.transform;
                viz.SetParent(joint, false);
                viz.localPosition = Vector3.zero;
                viz.localRotation = Quaternion.identity;
            }

            // Scale only the visual
            viz.localScale = Vector3.one * visualDiameter;
        }
    }
}

