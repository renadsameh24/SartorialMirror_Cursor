using System.Collections;
using UnityEngine;

public class MediaPipe33_To_J17Mapper1 : MonoBehaviour
{
    [Header("MediaPipe landmark spheres (33) root")]
    public Transform mediaPipeRoot; // parent that contains 33 spheres (spawned at runtime)

    [Header("Your J_* spheres root (contains the 17 targets as children)")]
    public Transform jointRoot;     // parent that contains J_pelvis, J_spine, ... etc

    [Header("Tuning")]
    public bool mirrorX = false;                 // only if your MP rig is mirrored wrong
    [Range(0.0f, 1.0f)] public float lerp = 1f;  // 1 = snap, 0.2-0.5 smooth
    public Vector3 globalOffset = Vector3.zero;

    // MediaPipe Pose landmark indices
    const int L_SHOULDER = 11, R_SHOULDER = 12;
    const int L_ELBOW = 13, R_ELBOW = 14;
    const int L_WRIST = 15, R_WRIST = 16;
    const int L_HIP = 23, R_HIP = 24;
    const int L_KNEE = 25, R_KNEE = 26;
    const int L_ANKLE = 27, R_ANKLE = 28;

    const int NOSE = 0;
    const int L_EAR = 7, R_EAR = 8; // often steadier than nose

    private Transform[] mp = new Transform[33];

    // Targets (auto-found by name under jointRoot)
    private Transform J_pelvis, J_spine, J_neck, J_head;
    private Transform J_l_shoulder, J_l_elbow, J_l_wrist;
    private Transform J_r_shoulder, J_r_elbow, J_r_wrist;
    private Transform J_l_hip, J_l_knee, J_l_ankle;
    private Transform J_r_hip, J_r_knee, J_r_ankle;

    private bool targetsReady = false;

    IEnumerator Start()
    {
        // Wait until user assigns roots
        while (mediaPipeRoot == null || jointRoot == null)
            yield return null;

        // Wait until mediapipe spheres are spawned
        while (mediaPipeRoot.childCount < 33)
            yield return null;

        CacheMpChildren();
        CacheTargetsByName();
    }

    void CacheMpChildren()
    {
        // Assumes the 33 spheres are children of mediaPipeRoot in landmark index order.
        for (int i = 0; i < 33; i++)
            mp[i] = mediaPipeRoot.GetChild(i);
    }

    void CacheTargetsByName()
    {
        // These names MUST match your sphere GameObject names under jointRoot (case-sensitive).
        J_pelvis = FindChildRecursive(jointRoot, "J_pelvis");
        J_spine  = FindChildRecursive(jointRoot, "J_spine");
        J_neck   = FindChildRecursive(jointRoot, "J_neck");
        J_head   = FindChildRecursive(jointRoot, "J_head");

        J_l_shoulder = FindChildRecursive(jointRoot, "J_l_shoulder");
        J_l_elbow    = FindChildRecursive(jointRoot, "J_l_elbow");
        J_l_wrist    = FindChildRecursive(jointRoot, "J_l_wrist");

        J_r_shoulder = FindChildRecursive(jointRoot, "J_r_shoulder");
        J_r_elbow    = FindChildRecursive(jointRoot, "J_r_elbow");
        J_r_wrist    = FindChildRecursive(jointRoot, "J_r_wrist");

        J_l_hip   = FindChildRecursive(jointRoot, "J_l_hip");
        J_l_knee  = FindChildRecursive(jointRoot, "J_l_knee");
        J_l_ankle = FindChildRecursive(jointRoot, "J_l_ankle");

        J_r_hip   = FindChildRecursive(jointRoot, "J_r_hip");
        J_r_knee  = FindChildRecursive(jointRoot, "J_r_knee");
        J_r_ankle = FindChildRecursive(jointRoot, "J_r_ankle");

        targetsReady =
            J_pelvis && J_spine && J_neck && J_head &&
            J_l_shoulder && J_l_elbow && J_l_wrist &&
            J_r_shoulder && J_r_elbow && J_r_wrist &&
            J_l_hip && J_l_knee && J_l_ankle &&
            J_r_hip && J_r_knee && J_r_ankle;

        if (!targetsReady)
        {
            Debug.LogError(
                "[MediaPipe33_To_J17Mapper] Could not find all J_* spheres under jointRoot. " +
                "Make sure the names match exactly (case-sensitive)."
            );
        }
    }

    void LateUpdate()
    {
        if (!targetsReady) return;
        if (mediaPipeRoot == null || jointRoot == null) return;
        if (mediaPipeRoot.childCount < 33) return;

        // If play mode restarted and cached refs got lost, re-cache
        if (mp[0] == null) CacheMpChildren();

        Vector3 Ls = Pos(mp[L_SHOULDER]);
        Vector3 Rs = Pos(mp[R_SHOULDER]);
        Vector3 Le = Pos(mp[L_ELBOW]);
        Vector3 Re = Pos(mp[R_ELBOW]);
        Vector3 Lw = Pos(mp[L_WRIST]);
        Vector3 Rw = Pos(mp[R_WRIST]);

        Vector3 Lh = Pos(mp[L_HIP]);
        Vector3 Rh = Pos(mp[R_HIP]);
        Vector3 Lk = Pos(mp[L_KNEE]);
        Vector3 Rk = Pos(mp[R_KNEE]);
        Vector3 La = Pos(mp[L_ANKLE]);
        Vector3 Ra = Pos(mp[R_ANKLE]);

        Vector3 pelvis = (Lh + Rh) * 0.5f;
        Vector3 shoulderCenter = (Ls + Rs) * 0.5f;
        Vector3 spine = (pelvis + shoulderCenter) * 0.5f;
        Vector3 neck = shoulderCenter;

        // Head proxy: prefer ears midpoint; else nose; else neck
        Vector3 headBase = neck;
        if (mp[L_EAR] != null && mp[R_EAR] != null)
            headBase = (Pos(mp[L_EAR]) + Pos(mp[R_EAR])) * 0.5f;
        else if (mp[NOSE] != null)
            headBase = Pos(mp[NOSE]);

        Vector3 up = neck - pelvis;
        if (up.sqrMagnitude < 1e-6f) up = Vector3.up;

        Vector3 head = Vector3.Lerp(neck, headBase, 0.7f) + up.normalized * 0.10f;

        SetTarget(J_pelvis, pelvis);
        SetTarget(J_spine, spine);
        SetTarget(J_neck, neck);
        SetTarget(J_head, head);

        SetTarget(J_l_shoulder, Ls);
        SetTarget(J_l_elbow, Le);
        SetTarget(J_l_wrist, Lw);

        SetTarget(J_r_shoulder, Rs);
        SetTarget(J_r_elbow, Re);
        SetTarget(J_r_wrist, Rw);

        SetTarget(J_l_hip, Lh);
        SetTarget(J_l_knee, Lk);
        SetTarget(J_l_ankle, La);

        SetTarget(J_r_hip, Rh);
        SetTarget(J_r_knee, Rk);
        SetTarget(J_r_ankle, Ra);
    }

    Vector3 Pos(Transform t)
    {
        Vector3 p = t.position + globalOffset;
        if (mirrorX) p.x = -p.x;
        return p;
    }

    void SetTarget(Transform target, Vector3 p)
    {
        if (!target) return;

        if (lerp >= 0.999f)
            target.position = p;
        else
            target.position = Vector3.Lerp(target.position, p, Mathf.Clamp01(lerp));
    }

    static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindChildRecursive(root.GetChild(i), name);
            if (found != null) return found;
        }

        return null;
    }
}