using System.Text;
using UnityEngine;

/// <summary>
/// Play-mode guardrail: detects NaN/Inf transforms and disables the component that is likely producing them.
/// This prevents Unity Editor GUI assertions like "IsFinite(d)" from spamming the Console and freezing iteration.
/// </summary>
[DefaultExecutionOrder(-32000)]
public sealed class FiniteTransformGuard : MonoBehaviour
{
    [Tooltip("If assigned, only checks transforms under this root (recommended: SMPL root or JointDebug).")]
    public Transform root;

    [Tooltip("If true, runs every frame. If false, runs once per second.")]
    public bool checkEveryFrame = true;

    [Tooltip("If true, disables the first offending component found on the same GameObject as the bad Transform.")]
    public bool disableOffendingComponents = true;

    [Tooltip("Max components disabled per run (keeps behavior predictable).")]
    public int maxDisablesPerRun = 2;

    private float _nextCheckTime = 0f;

    void Awake()
    {
        if (root == null) root = transform;
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        if (!checkEveryFrame)
        {
            if (Time.unscaledTime < _nextCheckTime) return;
            _nextCheckTime = Time.unscaledTime + 1f;
        }

        int disabled = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == null) continue;
            if (!Finite(t.position) || !Finite(t.localScale) || !Finite(t.rotation))
            {
                Debug.LogError($"[FiniteTransformGuard] Non-finite transform detected at '{Path(t)}' " +
                               $"pos={Fmt(t.position)} rot={Fmt(t.rotation)} scale={Fmt(t.localScale)}", t);

                if (disableOffendingComponents)
                    disabled += DisableLikelyOffenders(t.gameObject, maxDisablesPerRun - disabled);

                if (disabled >= maxDisablesPerRun)
                    break;
            }
        }
    }

    static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    static bool Finite(Quaternion q) => float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) && float.IsFinite(q.w);

    static string Fmt(Vector3 v) => $"({v.x:F4},{v.y:F4},{v.z:F4})";
    static string Fmt(Quaternion q) => $"({q.x:F4},{q.y:F4},{q.z:F4},{q.w:F4})";

    static string Path(Transform t)
    {
        var sb = new StringBuilder(128);
        while (t != null)
        {
            if (sb.Length == 0) sb.Insert(0, t.name);
            else sb.Insert(0, t.name + "/");
            t = t.parent;
        }
        return sb.ToString();
    }

    static int DisableLikelyOffenders(GameObject go, int budget)
    {
        if (go == null || budget <= 0) return 0;
        int disabled = 0;

        // Disable known producers first.
        disabled += DisableIfPresent(go, "AutoScaleSpheresToSmpl", ref budget);
        disabled += DisableIfPresent(go, "JointPlaybackStreamV2", ref budget);
        disabled += DisableIfPresent(go, "SpheresToBones_FKDriver", ref budget);
        disabled += DisableIfPresent(go, "FollowTransform", ref budget);
        disabled += DisableIfPresent(go, "SmplRootFollowPelvisSphere", ref budget);

        // Fallback: disable any remaining MonoBehaviours on this GO.
        if (budget > 0)
        {
            var mbs = go.GetComponents<MonoBehaviour>();
            foreach (var mb in mbs)
            {
                if (mb == null || !mb.enabled) continue;
                if (budget <= 0) break;
                mb.enabled = false;
                Debug.LogWarning($"[FiniteTransformGuard] Disabled component '{mb.GetType().Name}' on '{go.name}' to stop NaN/Inf propagation.", mb);
                budget--;
                disabled++;
            }
        }

        return disabled;
    }

    static int DisableIfPresent(GameObject go, string typeName, ref int budget)
    {
        if (budget <= 0) return 0;
        var comps = go.GetComponents<MonoBehaviour>();
        foreach (var c in comps)
        {
            if (c == null || !c.enabled) continue;
            if (c.GetType().Name != typeName) continue;
            c.enabled = false;
            Debug.LogWarning($"[FiniteTransformGuard] Disabled '{typeName}' on '{go.name}' (non-finite transform detected).", c);
            budget--;
            return 1;
        }
        return 0;
    }
}

