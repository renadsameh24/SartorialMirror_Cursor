using System;
using System.Collections.Generic;
using UnityEngine;

public class JointPlayback_Localized : MonoBehaviour
{
    [Header("Input sequence")]
    public TextAsset sequenceJson;

    [Header("Scene references")]
    public Transform characterRoot;       // CharacterRoot
    public Transform spheresRoot;         // JointSpheresRoot

    [Header("Keys in JSON")]
    public string pelvisKey = "pelvis";

    [Header("Axis conversion (set ONCE and stop touching later)")]
    public bool swapYZ = true;    // JSON Z-up -> Unity Y-up
    public bool invertX = false;
    public bool invertY = false;
    public bool invertZ = false;

    [Header("Scale (meters -> Unity units)")]
    public float positionScale = 1f;

    [Header("Playback")]
    public int fps = 30;
    public bool playOnStart = true;

    Dictionary<string, Transform> sphereByKey = new();
    List<Dictionary<string, Vector3>> frames = new();
    float t0;

    void Start()
    {
        BuildSphereMap();
        ParseSequence();
        if (playOnStart) t0 = Time.time;
    }

    void Update()
    {
        if (frames.Count == 0 || characterRoot == null) return;

        int frame = Mathf.FloorToInt((Time.time - t0) * fps) % frames.Count;

        var f = frames[frame];
        if (!f.ContainsKey(pelvisKey)) return;

        // pelvis-relative (kills drifting / flying away)
        Vector3 pelvis = Fix(f[pelvisKey]);

        foreach (var kv in f)
        {
            if (!sphereByKey.TryGetValue(kv.Key, out var s)) continue;

            Vector3 p = Fix(kv.Value);
            Vector3 local = (p - pelvis); // local coords around pelvis

            // place in character space
            s.position = characterRoot.position + local;
        }
    }

    void BuildSphereMap()
    {
        sphereByKey.Clear();
        if (!spheresRoot) return;

        foreach (Transform child in spheresRoot)
        {
            // sphere names like "J_pelvis" or "J_l_wrist"
            // we map "pelvis" -> that transform by stripping "J_"
            string n = child.name;
            if (n.StartsWith("J_")) n = n.Substring(2);
            sphereByKey[n] = child;
        }
    }

    Vector3 Fix(Vector3 v)
    {
        v *= positionScale;

        if (swapYZ) v = new Vector3(v.x, v.z, v.y);
        if (invertX) v.x *= -1;
        if (invertY) v.y *= -1;
        if (invertZ) v.z *= -1;

        return v;
    }

    // Minimal JSON reader assuming your schema:
    // { "fps": 30, "frames": [ { "joints": { "pelvis":[..], ... } }, ... ] }
    void ParseSequence()
    {
        frames.Clear();
        if (!sequenceJson) { Debug.LogWarning("No sequenceJson"); return; }

        object parsed = MiniJSON.Deserialize(sequenceJson.text);
        if (parsed is not Dictionary<string, object> root) { Debug.LogWarning("Bad JSON root"); return; }

        if (root.TryGetValue("fps", out var fpsObj)) fps = Convert.ToInt32(fpsObj);

        if (!root.TryGetValue("frames", out var framesObj) || framesObj is not List<object> fl)
        {
            Debug.LogWarning("No frames[]");
            return;
        }

        foreach (var item in fl)
        {
            if (item is not Dictionary<string, object> frameDict) continue;
            if (!frameDict.TryGetValue("joints", out var jointsObj)) continue;
            if (jointsObj is not Dictionary<string, object> joints) continue;

            var map = new Dictionary<string, Vector3>();
            foreach (var kv in joints)
            {
                if (kv.Value is List<object> arr && arr.Count >= 3)
                {
                    float x = Convert.ToSingle(arr[0]);
                    float y = Convert.ToSingle(arr[1]);
                    float z = Convert.ToSingle(arr[2]);
                    map[kv.Key] = new Vector3(x, y, z);
                }
            }
            frames.Add(map);
        }

        Debug.Log($"✅ Loaded {frames.Count} frames @ {fps} fps");
    }
}

