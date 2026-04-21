using UnityEngine;

public class PoseRig3DSpheres : MonoBehaviour
{
    public PoseReceiverWS receiver;

    [Header("Mapping")]
    public bool mirrorX = true;
    public float worldScale = 2.2f;
    public float depthScale = 1.2f; // temporary until depth camera arrives
    public Vector3 worldOffset = new Vector3(0f, 1.3f, 2.5f);

    [Header("Stability")]
    [Range(0f, 1f)] public float smoothing = 0.25f;
    [Range(0f, 1f)] public float minConf = 0.3f;

    [Header("Visual")]
    public float jointSize = 0.06f;

    private Transform[] joints = new Transform[33];
    private Vector3[] smoothed = new Vector3[33];

    void Start()
    {
        for (int i = 0; i < 33; i++)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = $"J{i:00}";
            s.transform.SetParent(transform, false);
            s.transform.localScale = Vector3.one * jointSize;
            var col = s.GetComponent<Collider>();
            if (col) Destroy(col);

            joints[i] = s.transform;
            smoothed[i] = s.transform.position;
        }
    }

    void Update()
    {
        if (receiver == null || !receiver.HasPose) return;

        var lms = receiver.Latest.landmarks;

        for (int i = 0; i < 33; i++)
        {
            var lm = lms[i];

            if (lm.v < minConf)
            {
                joints[i].gameObject.SetActive(false);
                continue;
            }
            joints[i].gameObject.SetActive(true);

            float nx = lm.x;
            float ny = lm.y;
            float nz = lm.z;

            if (mirrorX) nx = 1f - nx;

            float x = (nx - 0.5f) * worldScale;
            float y = (0.5f - ny) * worldScale;
            float z = -nz * depthScale;

            Vector3 target = worldOffset + new Vector3(x, y, z);

            smoothed[i] = Vector3.Lerp(smoothed[i], target, smoothing);
            joints[i].position = smoothed[i];
        }
    }
}
