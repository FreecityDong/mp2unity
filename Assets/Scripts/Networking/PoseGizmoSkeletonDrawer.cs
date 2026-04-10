using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Mp2Unity.Networking
{
    public class PoseGizmoSkeletonDrawer : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private PoseUdpReceiver receiver;
        [SerializeField] private bool autoFindReceiverOnSameObject = true;

        [Header("Display")]
        [SerializeField] private bool drawWhenPlayingOnly = true;
        [SerializeField] private bool drawPoints = true;
        [SerializeField] private bool drawBones = true;
        [SerializeField] private Color pointColor = new Color(0.25f, 0.95f, 0.35f, 1f);
        [SerializeField] private Color boneColor = new Color(0.20f, 0.65f, 1.00f, 1f);
        [SerializeField] private float pointRadius = 0.02f;
        [SerializeField] private float scaleMeters = 1.0f;
        [SerializeField] private Vector3 localOffset = new Vector3(0f, 1.0f, 0f);
        [SerializeField] private bool useObjectTransform = false;
        [SerializeField] private bool showJointLabels = true;
        [SerializeField] private bool showJointNames = false;
        [SerializeField] private Color labelColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] private Vector3 labelOffset = new Vector3(0.015f, 0.015f, 0f);

        [Header("Coordinate Mapping")]
        [SerializeField] private bool useHipCenterAsOrigin = true;
        [SerializeField] private bool flipX = false;
        [SerializeField] private bool flipY = true;
        [SerializeField] private bool flipZ = false;
        [SerializeField] private bool autoUprightByShoulderHip = true;

        private readonly Vector3[] _worldPoints = new Vector3[33];
        private readonly Vector3[] _convertedPoints = new Vector3[33];
        private static readonly string[] LandmarkNames =
        {
            "NOSE",
            "LEFT_EYE_INNER", "LEFT_EYE", "LEFT_EYE_OUTER",
            "RIGHT_EYE_INNER", "RIGHT_EYE", "RIGHT_EYE_OUTER",
            "LEFT_EAR", "RIGHT_EAR",
            "MOUTH_LEFT", "MOUTH_RIGHT",
            "LEFT_SHOULDER", "RIGHT_SHOULDER",
            "LEFT_ELBOW", "RIGHT_ELBOW",
            "LEFT_WRIST", "RIGHT_WRIST",
            "LEFT_PINKY", "RIGHT_PINKY",
            "LEFT_INDEX", "RIGHT_INDEX",
            "LEFT_THUMB", "RIGHT_THUMB",
            "LEFT_HIP", "RIGHT_HIP",
            "LEFT_KNEE", "RIGHT_KNEE",
            "LEFT_ANKLE", "RIGHT_ANKLE",
            "LEFT_HEEL", "RIGHT_HEEL",
            "LEFT_FOOT_INDEX", "RIGHT_FOOT_INDEX"
        };

        private static readonly (int start, int end)[] PoseConnections =
        {
            (0, 1), (1, 2), (2, 3), (3, 7),
            (0, 4), (4, 5), (5, 6), (6, 8),
            (9, 10),
            (11, 12),
            (11, 13), (13, 15), (15, 17), (15, 19), (15, 21),
            (17, 19),
            (12, 14), (14, 16), (16, 18), (16, 20), (16, 22),
            (18, 20),
            (11, 23), (12, 24), (23, 24),
            (23, 25), (24, 26),
            (25, 27), (26, 28),
            (27, 29), (28, 30),
            (29, 31), (30, 32),
            (27, 31), (28, 32)
        };

        private void Reset()
        {
            receiver = GetComponent<PoseUdpReceiver>();
        }

        private void OnValidate()
        {
            pointRadius = Mathf.Max(0.001f, pointRadius);
            scaleMeters = Mathf.Max(0.001f, scaleMeters);
            if (autoFindReceiverOnSameObject && receiver == null)
            {
                receiver = GetComponent<PoseUdpReceiver>();
            }
        }

        private void OnDrawGizmos()
        {
            if (drawWhenPlayingOnly && !Application.isPlaying)
            {
                return;
            }

            if (autoFindReceiverOnSameObject && receiver == null)
            {
                receiver = GetComponent<PoseUdpReceiver>();
            }

            if (receiver == null || !receiver.HasLatestPacket)
            {
                return;
            }

            PosePacket packet = receiver.LatestPacket;
            if (packet == null || packet.world_landmarks == null || packet.world_landmarks.Length < 33)
            {
                return;
            }

            for (int i = 0; i < 33; i++)
            {
                _convertedPoints[i] = ConvertToUnity(packet.world_landmarks[i]);
            }

            if (autoUprightByShoulderHip)
            {
                float shoulderY = (_convertedPoints[11].y + _convertedPoints[12].y) * 0.5f;
                float hipY = (_convertedPoints[23].y + _convertedPoints[24].y) * 0.5f;
                if (shoulderY < hipY)
                {
                    for (int i = 0; i < 33; i++)
                    {
                        Vector3 p = _convertedPoints[i];
                        p.y = -p.y;
                        _convertedPoints[i] = p;
                    }
                }
            }

            Vector3 origin = Vector3.zero;
            if (useHipCenterAsOrigin)
            {
                origin = (_convertedPoints[23] + _convertedPoints[24]) * 0.5f;
            }

            for (int i = 0; i < 33; i++)
            {
                Vector3 localPoint = (_convertedPoints[i] - origin) * scaleMeters;
                if (useObjectTransform)
                {
                    _worldPoints[i] = transform.TransformPoint(localPoint + localOffset);
                }
                else
                {
                    _worldPoints[i] = localPoint + localOffset;
                }
            }

            if (drawBones)
            {
                Gizmos.color = boneColor;
                for (int i = 0; i < PoseConnections.Length; i++)
                {
                    (int start, int end) = PoseConnections[i];
                    Gizmos.DrawLine(_worldPoints[start], _worldPoints[end]);
                }
            }

            if (drawPoints)
            {
                Gizmos.color = pointColor;
                for (int i = 0; i < _worldPoints.Length; i++)
                {
                    Gizmos.DrawSphere(_worldPoints[i], pointRadius);
                }
            }

            DrawJointLabels();
        }

        private Vector3 ConvertToUnity(PoseLandmark lm)
        {
            float x = lm.x;
            float y = lm.y;
            float z = lm.z;

            if (flipX) x = -x;
            if (flipY) y = -y;
            if (flipZ) z = -z;

            return new Vector3(x, y, z);
        }

        private void DrawJointLabels()
        {
#if UNITY_EDITOR
            if (!showJointLabels)
            {
                return;
            }

            var style = new GUIStyle
            {
                fontSize = 11,
                normal = { textColor = labelColor }
            };

            for (int i = 0; i < _worldPoints.Length; i++)
            {
                string text = showJointNames ? $"{i}:{LandmarkNames[i]}" : i.ToString();
                Handles.Label(_worldPoints[i] + labelOffset, text, style);
            }
#endif
        }
    }
}
