using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

public class JointPlaybackStream : MonoBehaviour
{
    [Header("Input sequence JSON (frames)")]
    public TextAsset sequenceJson;

    [Header("Spheres location (drag these once)")]
    public Transform jointDebugRoot;              // drag JointDebug here
    public string jointSpheresRootName = "JointSpheresRoot";

    [Header("Playback")]
    public float fps = 30f;
    public bool loop = true;
    public bool playOnStart = true;

    [Header("DEBUG")]
    public bool debugLogs = true;
    public bool printKeySamplesOnFail = true;

    [Header("Coordinate Fixes (match JointDebugVisualizer)")]
    public float positionScale = 1.0f;
    public bool swapYZ = true;
    public bool invertX = false;
    public bool invertY = false;
    public bool invertZ = false;

    [Header("Pelvis anchoring (match JointDebugVisualizer)")]
    public Transform smplRoot;
    public string smplRootName = "SMPL_neutral_rig_GOLDEN";
    public string pelvisBoneName = "J00";
    public string jsonPelvisKey = "pelvis";

    [Header("Adjustments (match JointDebugVisualizer)")]
    public float rigScale = 0.85f;
    public float globalLiftY = 0.0f;
    public float armLiftY = 0.08f;

    private List<Dictionary<string, Vector3>> _frames = new();
    private int _frameIndex = 0;
    private float _accum = 0f;

    // sphere transforms by normalized joint key (lowercase)
    private readonly Dictionary<string, Transform> _sphereByJoint = new();

    void Start()
    {
        AutoFindSmplRoot();

        // Cache spheres at start
        CacheSpheresFromJointDebug();

        if (sequenceJson == null || string.IsNullOrWhiteSpace(sequenceJson.text))
        {
            Debug.LogWarning("[Playback] sequenceJson missing/empty.");
            return;
        }

        _frames = ParseFrames(sequenceJson.text);
        Debug.Log($"[Playback] Parsed frames = {_frames.Count}");

        if (_frames.Count == 0)
        {
            Debug.LogWarning("[Playback] No frames parsed from JSON.");
            return;
        }

        if (playOnStart)
        {
            _frameIndex = 0;
            ApplyFrame(_frames[_frameIndex]);
            if (debugLogs) Debug.Log("[Playback] Applied frame 0");
        }
    }

    void Update()
    {
        if (_frames == null || _frames.Count == 0) return;

        // If spheres got rebuilt, our cached transforms can become null.
        // So we refresh cache if any cached value is missing.
        EnsureSphereCacheValid();

        float frameTime = 1f / Mathf.Max(1f, fps);
        _accum += Time.deltaTime;

        while (_accum >= frameTime)
        {
            _accum -= frameTime;

            _frameIndex++;
            if (_frameIndex >= _frames.Count)
            {
                if (!loop) { _frameIndex = _frames.Count - 1; break; }
                _frameIndex = 0;
            }

            ApplyFrame(_frames[_frameIndex]);
        }
    }

    void EnsureSphereCacheValid()
    {
        if (_sphereByJoint.Count == 0)
        {
            CacheSpheresFromJointDebug();
            return;
        }

        // If any transform reference is null (destroyed), recache.
        foreach (var kv in _sphereByJoint)
        {
            if (kv.Value == null)
            {
                CacheSpheresFromJointDebug();
                return;
            }
        }
    }

    void CacheSpheresFromJointDebug()
    {
        _sphereByJoint.Clear();

        // Default: this component usually lives on the JointDebug GameObject; spheres are a child.
        Transform root = jointDebugRoot != null ? jointDebugRoot : transform;

        var spheresRoot = root.Find(jointSpheresRootName);
        if (spheresRoot == null)
        {
            Debug.LogWarning(
                $"[Playback] Could not find '{jointSpheresRootName}' under '{root.name}'. " +
                "Assign Joint Debug Root to the JointDebug object (or place this script on JointDebug).",
                this);
            return;
        }

        int count = 0;
        for (int i = 0; i < spheresRoot.childCount; i++)
        {
            var t = spheresRoot.GetChild(i);
            if (t.name.StartsWith("J_"))
            {
                string key = Norm(t.name.Substring(2)); // strip "J_"
                _sphereByJoint[key] = t;
                count++;
            }
        }

        Debug.Log($"[Playback] Cached spheres from JointDebug = {count}");
    }

    void ApplyFrame(Dictionary<string, Vector3> jointsRaw)
    {
        if (jointsRaw == null || jointsRaw.Count == 0) return;

        // Convert coords + normalize keys
        var joints = new Dictionary<string, Vector3>(jointsRaw.Count);
        foreach (var kv in jointsRaw)
        {
            string key = Norm(kv.Key);
            joints[key] = FixCoords(kv.Value);
        }

        // Offset: snap pelvis to rig pelvis + global lift
        Vector3 offset = new Vector3(0f, globalLiftY, 0f);

        string pelvisKey = Norm(jsonPelvisKey);

        if (smplRoot != null && joints.ContainsKey(pelvisKey))
        {
            Transform pelvisBone = FindDeepChild(smplRoot, pelvisBoneName);
            if (pelvisBone != null)
                offset += pelvisBone.position - joints[pelvisKey];
        }

        bool hasPelvis = joints.ContainsKey(pelvisKey);
        Vector3 pelvisPos = hasPelvis ? (joints[pelvisKey] + offset) : Vector3.zero;

        int updated = 0;

        foreach (var kv in joints)
        {
            if (!_sphereByJoint.TryGetValue(kv.Key, out var sphereT) || sphereT == null)
                continue;

            Vector3 worldPos = kv.Value + offset;

            if (IsArmJoint(kv.Key))
                worldPos += new Vector3(0f, armLiftY, 0f);

            if (hasPelvis)
                sphereT.position = pelvisPos + (worldPos - pelvisPos) * rigScale;
            else
                sphereT.position = worldPos;

            updated++;
        }

        // If updated 0, recache and retry once
        if (updated == 0)
        {
            CacheSpheresFromJointDebug();

            updated = 0;
            foreach (var kv in joints)
            {
                if (!_sphereByJoint.TryGetValue(kv.Key, out var sphereT) || sphereT == null)
                    continue;

                Vector3 worldPos = kv.Value + offset;
                if (IsArmJoint(kv.Key))
                    worldPos += new Vector3(0f, armLiftY, 0f);

                if (hasPelvis)
                    sphereT.position = pelvisPos + (worldPos - pelvisPos) * rigScale;
                else
                    sphereT.position = worldPos;

                updated++;
            }

            if (updated == 0)
            {
                Debug.LogWarning("[Playback] Still updated 0 spheres after recache. Keys mismatch.");
                if (printKeySamplesOnFail) PrintKeySamples(joints);
            }
            else if (debugLogs)
            {
                Debug.Log($"[Playback] Updated {updated} spheres after recache.");
            }
        }
    }

    void PrintKeySamples(Dictionary<string, Vector3> joints)
    {
        int shown = 0;
        foreach (var k in _sphereByJoint.Keys)
        {
            Debug.Log("[Playback] Cached sphere key: " + k);
            shown++;
            if (shown >= 8) break;
        }

        shown = 0;
        foreach (var k in joints.Keys)
        {
            Debug.Log("[Playback] Frame key: " + k);
            shown++;
            if (shown >= 8) break;
        }
    }

    bool IsArmJoint(string jointName)
        => jointName.Contains("shoulder") || jointName.Contains("elbow") || jointName.Contains("wrist");

    string Norm(string s) => (s ?? "").Trim().ToLowerInvariant();

    void AutoFindSmplRoot()
    {
        if (smplRoot != null) return;
        var go = GameObject.Find(smplRootName);
        if (go != null) smplRoot = go.transform;
    }

    Vector3 FixCoords(Vector3 v)
    {
        v *= positionScale;
        if (swapYZ) v = new Vector3(v.x, v.z, v.y);
        if (invertX) v.x *= -1f;
        if (invertY) v.y *= -1f;
        if (invertZ) v.z *= -1f;
        return v;
    }

    Transform FindDeepChild(Transform parent, string name)
    {
        if (parent == null) return null;
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var r = FindDeepChild(child, name);
            if (r != null) return r;
        }
        return null;
    }

    // Parser: finds all "joints": { ... } blocks (your sample format)
    List<Dictionary<string, Vector3>> ParseFrames(string json)
    {
        var frames = new List<Dictionary<string, Vector3>>();
        int idx = 0;

        while (true)
        {
            int k = json.IndexOf("\"joints\"", idx);
            if (k < 0) break;

            int open = json.IndexOf('{', k);
            if (open < 0) break;

            int depth = 0;
            int close = -1;
            for (int i = open; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0) { close = i; break; }
                }
            }
            if (close < 0) break;

            string jointsBlock = json.Substring(open + 1, close - open - 1);
            var jointMap = ParseJointMap(jointsBlock);
            if (jointMap.Count > 0) frames.Add(jointMap);

            idx = close + 1;
        }

        return frames;
    }

    Dictionary<string, Vector3> ParseJointMap(string jointsBlock)
    {
        var map = new Dictionary<string, Vector3>();

        var re = new Regex(
            "\"(?<name>[^\"]+)\"\\s*:\\s*\\[\\s*(?<x>[-+0-9.eE]+)\\s*,\\s*(?<y>[-+0-9.eE]+)\\s*,\\s*(?<z>[-+0-9.eE]+)\\s*\\]",
            RegexOptions.Compiled
        );

        var matches = re.Matches(jointsBlock);
        foreach (Match m in matches)
        {
            string name = m.Groups["name"].Value;

            if (!float.TryParse(m.Groups["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) continue;
            if (!float.TryParse(m.Groups["y"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) continue;
            if (!float.TryParse(m.Groups["z"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;

            map[name] = new Vector3(x, y, z);
        }

        return map;
    }
}
