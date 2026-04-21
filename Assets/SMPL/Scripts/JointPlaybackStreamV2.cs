using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

public class JointPlaybackStreamV2 : MonoBehaviour
{
    public enum Mode
    {
        SequencePlayback, // frames[] inside one JSON
        SingleFrameLive   // one "frame" JSON (joints only), reloaded repeatedly (Jetson simulation)
    }

    [Header("Mode")]
    public Mode mode = Mode.SequencePlayback;

    // ---------------------------
    // A) Sequence Playback (frames)
    // ---------------------------
    [Header("A) Input sequence JSON (frames)")]
    public TextAsset sequenceJson;

    [Header("Playback")]
    public float fps = 30f;
    public bool loop = true;
    public bool playOnStart = true;

    // ---------------------------
    // B) Single Frame Live (Jetson simulation)
    // ---------------------------
    [Header("B) Single-frame live JSON (Jetson simulation)")]
    [Tooltip("Optional: a TextAsset for a single-frame JSON. If file path is set, file path wins.")]
    public TextAsset liveFrameJson;

    [Tooltip("If set, script will read this file every updateTickSeconds. Use this to simulate Jetson writing frames.")]
    public string liveFrameFilePath = ""; // e.g. Application.dataPath + "/Data/live_frame.json"

    [Tooltip("How often to reload the live frame JSON/file.")]
    public float updateTickSeconds = 1f / 30f;

    [Tooltip("If true, will keep last good frame when live JSON fails to parse.")]
    public bool keepLastOnParseFail = true;

    // ---------------------------
    // Spheres + Coordinate system
    // ---------------------------
    [Header("Spheres location (drag JointDebug here)")]
    public Transform jointDebugRoot;              // drag JointDebug here
    public string jointSpheresRootName = "JointSpheresRoot";

    [Header("Smoothing")]
    [Range(0f, 1f)] public float positionLerp = 0.35f; // 1 = no smoothing, 0.2~0.4 smooth

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

    // ---------------------------
    // Internal state
    // ---------------------------
    private List<Dictionary<string, Vector3>> _frames = new();
    private int _frameIndex = 0;
    private float _accum = 0f;

    private float _liveAccum = 0f;
    private Dictionary<string, Vector3> _lastLiveFrame = null;

    private readonly Dictionary<string, Transform> _sphereByJoint = new();

    void Start()
    {
        AutoFindSmplRoot();
        CacheSpheresFromJointDebug();

        if (mode == Mode.SequencePlayback)
        {
            if (sequenceJson == null || string.IsNullOrWhiteSpace(sequenceJson.text))
            {
                Debug.LogWarning("[PlaybackV2] sequenceJson missing/empty.");
                return;
            }

            _frames = ParseFrames(sequenceJson.text);
            Debug.Log($"[PlaybackV2] Parsed frames = {_frames.Count}");

            if (_frames.Count == 0)
            {
                Debug.LogWarning("[PlaybackV2] No frames parsed from JSON.");
                return;
            }

            if (playOnStart)
            {
                _frameIndex = 0;
                ApplyFrame(_frames[_frameIndex], snapInstant: true); // snap first frame
                if (debugLogs) Debug.Log("[PlaybackV2] Applied frame 0 (snap)");
            }
        }
        else // SingleFrameLive
        {
            // Apply once immediately if possible
            var frame = LoadLiveFrame();
            if (frame != null && frame.Count > 0)
            {
                _lastLiveFrame = frame;
                ApplyFrame(_lastLiveFrame, snapInstant: true);
                if (debugLogs) Debug.Log("[PlaybackV2] Applied initial live frame (snap)");
            }
            else
            {
                Debug.LogWarning("[PlaybackV2] Live mode: could not load initial frame. (This is OK if you will provide it after Play).");
            }
        }
    }

    void Update()
    {
        EnsureSphereCacheValid();

        if (mode == Mode.SequencePlayback)
        {
            if (_frames == null || _frames.Count == 0) return;

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

                ApplyFrame(_frames[_frameIndex], snapInstant: false);
            }
        }
        else // SingleFrameLive
        {
            _liveAccum += Time.deltaTime;
            if (_liveAccum < Mathf.Max(0.001f, updateTickSeconds)) return;
            _liveAccum = 0f;

            var frame = LoadLiveFrame();
            if (frame != null && frame.Count > 0)
            {
                _lastLiveFrame = frame;
                ApplyFrame(_lastLiveFrame, snapInstant: false);
            }
            else
            {
                if (!keepLastOnParseFail) return;

                // Keep last known good frame (do nothing)
                if (debugLogs) Debug.Log("[PlaybackV2] Live frame parse failed; keeping last frame.");
            }
        }
    }

    Dictionary<string, Vector3> LoadLiveFrame()
    {
        string raw = null;

        // If a file path is provided, use it
        if (!string.IsNullOrWhiteSpace(liveFrameFilePath))
        {
            try
            {
                if (File.Exists(liveFrameFilePath))
                    raw = File.ReadAllText(liveFrameFilePath);
                else
                    return null;
            }
            catch
            {
                return null;
            }
        }
        else if (liveFrameJson != null)
        {
            raw = liveFrameJson.text;
        }

        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Accept either:
        // 1) {"units":"meters","joints":{...}}
        // 2) {"joints":{...}}
        // 3) a sequence JSON, we’ll just take the FIRST "joints" block
        int k = raw.IndexOf("\"joints\"");
        if (k < 0) return null;

        int open = raw.IndexOf('{', k);
        if (open < 0) return null;

        int depth = 0;
        int close = -1;
        for (int i = open; i < raw.Length; i++)
        {
            if (raw[i] == '{') depth++;
            else if (raw[i] == '}')
            {
                depth--;
                if (depth == 0) { close = i; break; }
            }
        }
        if (close < 0) return null;

        string jointsBlock = raw.Substring(open + 1, close - open - 1);
        var jointMap = ParseJointMap(jointsBlock);
        return jointMap;
    }

    void EnsureSphereCacheValid()
    {
        if (_sphereByJoint.Count == 0)
        {
            CacheSpheresFromJointDebug();
            return;
        }

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

        Transform root = jointDebugRoot != null ? jointDebugRoot : transform;

        var spheresRoot = root.Find(jointSpheresRootName);
        if (spheresRoot == null)
        {
            Debug.LogWarning(
                $"[PlaybackV2] Could not find '{jointSpheresRootName}' under '{root.name}'. " +
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
                string key = Norm(t.name.Substring(2));
                _sphereByJoint[key] = t;
                count++;
            }
        }

        if (debugLogs) Debug.Log($"[PlaybackV2] Cached spheres from JointDebug = {count}");
    }

    void ApplyFrame(Dictionary<string, Vector3> jointsRaw, bool snapInstant)
    {
        if (jointsRaw == null || jointsRaw.Count == 0) return;

        var joints = new Dictionary<string, Vector3>(jointsRaw.Count);
        foreach (var kv in jointsRaw)
        {
            string key = Norm(kv.Key);
            joints[key] = FixCoords(kv.Value);
        }

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

            Vector3 targetPos = hasPelvis
                ? pelvisPos + (worldPos - pelvisPos) * rigScale
                : worldPos;

            if (snapInstant || positionLerp >= 0.999f)
                sphereT.position = targetPos;
            else
                sphereT.position = Vector3.Lerp(sphereT.position, targetPos, positionLerp);

            updated++;
        }

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

                Vector3 targetPos = hasPelvis
                    ? pelvisPos + (worldPos - pelvisPos) * rigScale
                    : worldPos;

                if (snapInstant || positionLerp >= 0.999f)
                    sphereT.position = targetPos;
                else
                    sphereT.position = Vector3.Lerp(sphereT.position, targetPos, positionLerp);

                updated++;
            }

            if (updated == 0)
            {
                Debug.LogWarning("[PlaybackV2] Still updated 0 spheres after recache.");
                if (printKeySamplesOnFail) PrintKeySamples(joints);
            }
            else if (debugLogs)
            {
                Debug.Log($"[PlaybackV2] Updated {updated} spheres after recache.");
            }
        }
        else if (debugLogs && mode == Mode.SequencePlayback && (_frameIndex % 10 == 0))
        {
            Debug.Log($"[PlaybackV2] Updated {updated} spheres on frame {_frameIndex}/{_frames.Count}");
        }
    }

    void PrintKeySamples(Dictionary<string, Vector3> joints)
    {
        int shown = 0;
        foreach (var k in _sphereByJoint.Keys)
        {
            Debug.Log("[PlaybackV2] Cached sphere key: " + k);
            shown++;
            if (shown >= 8) break;
        }

        shown = 0;
        foreach (var k in joints.Keys)
        {
            Debug.Log("[PlaybackV2] Frame key: " + k);
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

    // Parses sequence JSON by scanning for multiple "joints" blocks.
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
