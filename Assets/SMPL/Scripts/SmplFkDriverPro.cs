using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(900)] // run after playback updates spheres
public class SmplFkDriverPro : MonoBehaviour
{
    [Header("INPUT (Spheres)")]
    public Transform jointDebugRoot;                // Drag your JointDebug gameobject here
    public string jointSpheresRootName = "JointSpheresRoot";

    [Header("RIG ROOT (moves + rotates)")]
    public Transform characterRoot;                 // Usually your CharacterRoot (parent of the skinned mesh)
    public Transform rigRoot;                       // Usually the armature root under the model (optional)

    [Header("BONES (assign from your SMPL armature)")]
    public Transform B_Pelvis;                      // J00
    public Transform B_Spine;                       // J03 (or whatever your spine bone is)
    public Transform B_Neck;                        // J12/J15 depending on your rig
    public Transform B_Head;                        // Head joint/bone if you have it

    public Transform B_LShoulder;                   // J16
    public Transform B_LElbow;                      // J18
    public Transform B_LWrist;                      // J20 (or wrist/hand)

    public Transform B_RShoulder;                   // J17
    public Transform B_RElbow;                      // J19
    public Transform B_RWrist;                      // J21 (or wrist/hand)

    public Transform B_LHip;                        // J01
    public Transform B_LKnee;                       // J04
    public Transform B_LAnkle;                      // J07 (or ankle/foot)

    public Transform B_RHip;                        // J02
    public Transform B_RKnee;                       // J05
    public Transform B_RAnkle;                      // J08 (or ankle/foot)

    [Header("JOINT KEYS (sphere names without 'J_')")]
    public string J_Pelvis = "pelvis";
    public string J_Spine  = "spine";
    public string J_Neck   = "neck";
    public string J_Head   = "head";

    public string J_LShoulder = "l_shoulder";
    public string J_LElbow    = "l_elbow";
    public string J_LWrist    = "l_wrist";

    public string J_RShoulder = "r_shoulder";
    public string J_RElbow    = "r_elbow";
    public string J_RWrist    = "r_wrist";

    public string J_LHip   = "l_hip";
    public string J_LKnee  = "l_knee";
    public string J_LAnkle = "l_ankle";

    public string J_RHip   = "r_hip";
    public string J_RKnee  = "r_knee";
    public string J_RAnkle = "r_ankle";

    [Header("ROOT / TORSO SETTINGS")]
    public bool applyRootPosition = true;
    public bool applyRootRotation = true;

    [Tooltip("How much torso rotation lives in pelvis vs spine/neck (should sum ~1).")]
    [Range(0f, 1f)] public float pelvisTwist = 0.25f;
    [Range(0f, 1f)] public float spineTwist  = 0.55f;
    [Range(0f, 1f)] public float neckTwist   = 0.20f;

    [Header("STABILITY (anti-flip planes)")]
    [Tooltip("How strongly to stabilize elbow plane using shoulder-elbow-wrist plane normal.")]
    [Range(0f, 1f)] public float elbowPlaneStability = 0.6f;

    [Tooltip("How strongly to stabilize knee plane using hip-knee-ankle plane normal.")]
    [Range(0f, 1f)] public float kneePlaneStability = 0.6f;

    [Header("SMOOTHING")]
    [Tooltip("0 = no smoothing, 0.2~0.5 recommended")]
    [Range(0f, 1f)] public float rotSmoothing = 0.25f;

    [Tooltip("0 = no smoothing, 0.2~0.5 recommended")]
    [Range(0f, 1f)] public float posSmoothing = 0.25f;

    [Header("DEBUG")]
    public bool logMissingOnce = true;

    // --- internal caches ---
    Transform _spheresRoot;
    Dictionary<string, Transform> _sphere = new Dictionary<string, Transform>();

    struct Rest
    {
        public Quaternion worldRestRot;
        public Vector3 worldRestDir;    // from bone to its child at rest
        public Vector3 worldRestUp;     // a stable "up" reference for that bone (approx)
    }

    Rest R_Pelvis, R_Spine, R_Neck;
    Rest R_LShoulder, R_LElbow, R_RShoulder, R_RElbow;
    Rest R_LHip, R_LKnee, R_RHip, R_RKnee;

    bool _restCached = false;
    HashSet<string> _missingLogged = new HashSet<string>();

    void Start()
    {
        CacheSphereMap();
        CacheRestPose();
    }

    void OnEnable()
    {
        CacheSphereMap();
        CacheRestPose();
    }

    void LateUpdate()
    {
        if (!characterRoot) return;

        if (_spheresRoot == null || _sphere.Count == 0)
            CacheSphereMap();

        if (!_restCached)
            CacheRestPose();

        // --- Read joints from spheres ---
        if (!TryJ(J_Pelvis, out var pPelvis)) return;

        TryJ(J_Spine, out var pSpine);
        TryJ(J_Neck,  out var pNeck);
        TryJ(J_Head,  out var pHead);

        TryJ(J_LHip, out var pLHip);
        TryJ(J_RHip, out var pRHip);

        // 1) ROOT POSITION
        if (applyRootPosition)
        {
            var targetPos = pPelvis;
            characterRoot.position = Vector3.Lerp(characterRoot.position, targetPos, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(1e-4f, posSmoothing)));
        }

        // 2) ROOT ROTATION (turn-in-place / yaw)
        // Build a body orientation from hips + spine:
        Quaternion bodyRot = characterRoot.rotation;
        if (applyRootRotation && pSpine != Vector3.zero && pLHip != Vector3.zero && pRHip != Vector3.zero)
        {
            Vector3 up = (pSpine - pPelvis).normalized;
            Vector3 right = (pRHip - pLHip).normalized;
            Vector3 forward = Vector3.Cross(right, up).normalized;

            if (forward.sqrMagnitude > 1e-6f && up.sqrMagnitude > 1e-6f)
            {
                bodyRot = Quaternion.LookRotation(forward, up);
                characterRoot.rotation = SmoothSlerp(characterRoot.rotation, bodyRot, rotSmoothing);
            }
        }

        // 3) TORSO DISTRIBUTION
        // Compute a torso rotation relative to current root:
        // We aim pelvis->spine direction and hip axis, then distribute across pelvis/spine/neck.
        if (B_Pelvis && B_Spine && pSpine != Vector3.zero && pLHip != Vector3.zero && pRHip != Vector3.zero)
        {
            Quaternion torsoRot = bodyRot; // world torso orientation
            ApplyDistributedTorso(torsoRot);
        }

        // 4) LIMBS: rotate bone chains to match joint directions
        // Arms
        SolveChain(B_LShoulder, B_LElbow, B_LWrist, pGet(J_LShoulder), pGet(J_LElbow), pGet(J_LWrist), ref R_LShoulder, ref R_LElbow, elbowPlaneStability);
        SolveChain(B_RShoulder, B_RElbow, B_RWrist, pGet(J_RShoulder), pGet(J_RElbow), pGet(J_RWrist), ref R_RShoulder, ref R_RElbow, elbowPlaneStability);

        // Legs
        SolveChain(B_LHip, B_LKnee, B_LAnkle, pGet(J_LHip), pGet(J_LKnee), pGet(J_LAnkle), ref R_LHip, ref R_LKnee, kneePlaneStability);
        SolveChain(B_RHip, B_RKnee, B_RAnkle, pGet(J_RHip), pGet(J_RKnee), pGet(J_RAnkle), ref R_RHip, ref R_RKnee, kneePlaneStability);
    }

    // ---------------------- Core Solvers ----------------------

    void ApplyDistributedTorso(Quaternion torsoWorld)
    {
        // Distribute torsoWorld across pelvis/spine/neck by blending from each bone's rest.
        // We don't slam absolute rotations; we rotate each bone so its "rest dir" matches live dir targets.
        // Here we simply use torsoWorld as the target orientation and blend it.

        if (B_Pelvis)
        {
            Quaternion target = Quaternion.Slerp(R_Pelvis.worldRestRot, torsoWorld, pelvisTwist);
            B_Pelvis.rotation = SmoothSlerp(B_Pelvis.rotation, target, rotSmoothing);
        }
        if (B_Spine)
        {
            Quaternion target = Quaternion.Slerp(R_Spine.worldRestRot, torsoWorld, spineTwist);
            B_Spine.rotation = SmoothSlerp(B_Spine.rotation, target, rotSmoothing);
        }
        if (B_Neck)
        {
            Quaternion target = Quaternion.Slerp(R_Neck.worldRestRot, torsoWorld, neckTwist);
            B_Neck.rotation = SmoothSlerp(B_Neck.rotation, target, rotSmoothing);
        }
        // Optional head: keep head mostly upright
        if (B_Head)
        {
            // Keep head close to neck; small follow to avoid creepy tilts
            B_Head.rotation = SmoothSlerp(B_Head.rotation, B_Neck ? B_Neck.rotation : torsoWorld, rotSmoothing * 0.6f);
        }
    }

    void SolveChain(
        Transform boneA, Transform boneB, Transform boneC,
        Vector3 jA, Vector3 jB, Vector3 jC,
        ref Rest restA, ref Rest restB,
        float planeStability)
    {
        if (!boneA || !boneB || !boneC) return;
        if (jA == Vector3.zero || jB == Vector3.zero || jC == Vector3.zero) return;

        // Desired directions:
        Vector3 dirA = (jB - jA).normalized; // A -> B
        Vector3 dirB = (jC - jB).normalized; // B -> C

        if (dirA.sqrMagnitude < 1e-6f || dirB.sqrMagnitude < 1e-6f) return;

        // Plane normal (for stabilizing roll / twist)
        Vector3 planeN = Vector3.Cross((jB - jA), (jC - jB));
        if (planeN.sqrMagnitude < 1e-8f) planeN = restA.worldRestUp;
        planeN.Normalize();

        // A bone rotation: aim restDir to dirA, and stabilize its "up" using the plane normal
        Quaternion aimA = FromToWithUp(restA.worldRestDir, dirA, restA.worldRestUp, planeN, planeStability) * restA.worldRestRot;
        boneA.rotation = SmoothSlerp(boneA.rotation, aimA, rotSmoothing);

        // B bone rotation: aim restDir to dirB, stabilize using same plane normal
        Quaternion aimB = FromToWithUp(restB.worldRestDir, dirB, restB.worldRestUp, planeN, planeStability) * restB.worldRestRot;
        boneB.rotation = SmoothSlerp(boneB.rotation, aimB, rotSmoothing);

        // boneC (wrist/ankle) is typically end-effector; keep as-is or lightly follow parent
        boneC.rotation = SmoothSlerp(boneC.rotation, boneB.rotation, rotSmoothing * 0.35f);
    }

    Quaternion FromToWithUp(Vector3 restDir, Vector3 newDir, Vector3 restUp, Vector3 newUp, float upBlend)
    {
        // primary: rotate restDir -> newDir
        Quaternion primary = Quaternion.FromToRotation(restDir, newDir);

        // secondary: stabilize roll by aligning "up" directions after primary
        Vector3 upAfter = (primary * restUp).normalized;

        Quaternion roll = Quaternion.FromToRotation(upAfter, newUp);

        // blend roll influence so we don't over-constrain
        Quaternion secondary = Quaternion.Slerp(Quaternion.identity, roll, Mathf.Clamp01(upBlend));
        return secondary * primary;
    }

    Quaternion SmoothSlerp(Quaternion a, Quaternion b, float smoothing)
    {
        if (smoothing <= 0.0001f) return b;
        float t = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(1e-4f, smoothing));
        return Quaternion.Slerp(a, b, t);
    }

    // ---------------------- Sphere input helpers ----------------------

    Vector3 pGet(string key)
    {
        if (TryJ(key, out var p)) return p;
        return Vector3.zero;
    }

    bool TryJ(string key, out Vector3 pos)
    {
        pos = Vector3.zero;
        if (_sphere.TryGetValue(Norm(key), out var t) && t != null)
        {
            pos = t.position;
            return true;
        }

        if (logMissingOnce && !_missingLogged.Contains(key))
        {
            Debug.LogWarning($"[SmplFkDriverPro] Missing sphere for key: {key} (expected object name J_{key})");
            _missingLogged.Add(key);
        }
        return false;
    }

    void CacheSphereMap()
    {
        _sphere.Clear();

        if (!jointDebugRoot)
        {
            Debug.LogWarning("[SmplFkDriverPro] jointDebugRoot not assigned.");
            return;
        }

        _spheresRoot = jointDebugRoot.Find(jointSpheresRootName);
        if (!_spheresRoot)
        {
            Debug.LogWarning("[SmplFkDriverPro] Could not find JointSpheresRoot under jointDebugRoot.");
            return;
        }

        for (int i = 0; i < _spheresRoot.childCount; i++)
        {
            var t = _spheresRoot.GetChild(i);
            if (!t.name.StartsWith("J_")) continue;

            string k = Norm(t.name.Substring(2));
            _sphere[k] = t;
        }

        Debug.Log($"[SmplFkDriverPro] Cached {_sphere.Count} spheres.");
    }

    string Norm(string s) => (s ?? "").Trim().ToLowerInvariant();

    // ---------------------- Rest pose caching ----------------------

    void CacheRestPose()
    {
        if (!B_Pelvis || !B_Spine)
        {
            _restCached = false;
            return;
        }

        R_Pelvis = CacheRest(B_Pelvis, B_Spine);
        R_Spine  = CacheRest(B_Spine,  B_Neck ? B_Neck : B_Head);
        R_Neck   = CacheRest(B_Neck ? B_Neck : B_Spine, B_Head);

        R_LShoulder = CacheRest(B_LShoulder, B_LElbow);
        R_LElbow    = CacheRest(B_LElbow,    B_LWrist);

        R_RShoulder = CacheRest(B_RShoulder, B_RElbow);
        R_RElbow    = CacheRest(B_RElbow,    B_RWrist);

        R_LHip   = CacheRest(B_LHip,   B_LKnee);
        R_LKnee  = CacheRest(B_LKnee,  B_LAnkle);

        R_RHip   = CacheRest(B_RHip,   B_RKnee);
        R_RKnee  = CacheRest(B_RKnee,  B_RAnkle);

        _restCached = true;
        Debug.Log("[SmplFkDriverPro] Rest pose cached.");
    }

    Rest CacheRest(Transform bone, Transform child)
    {
        Rest r = new Rest();
        if (!bone || !child)
        {
            r.worldRestRot = bone ? bone.rotation : Quaternion.identity;
            r.worldRestDir = Vector3.forward;
            r.worldRestUp  = Vector3.up;
            return r;
        }

        r.worldRestRot = bone.rotation;

        Vector3 dir = (child.position - bone.position);
        if (dir.sqrMagnitude < 1e-8f) dir = bone.forward;
        r.worldRestDir = dir.normalized;

        // A stable "up": try bone.up, but ensure not parallel to dir
        Vector3 up = bone.up;
        if (Mathf.Abs(Vector3.Dot(up.normalized, r.worldRestDir)) > 0.95f)
            up = bone.right;

        r.worldRestUp = up.normalized;
        return r;
    }
}
