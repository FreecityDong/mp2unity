using System.Collections.Generic;
using UnityEngine;

namespace Mp2Unity.Networking
{
    public class PoseRetargeter : MonoBehaviour
    {
        private enum RigJoint
        {
            Hips = 0,
            Spine = 1,
            Chest = 2,
            Head = 3,
            LeftUpperArm = 4,
            LeftLowerArm = 5,
            LeftHand = 6,
            RightUpperArm = 7,
            RightLowerArm = 8,
            RightHand = 9,
            LeftUpperLeg = 10,
            LeftLowerLeg = 11,
            LeftFoot = 12,
            RightUpperLeg = 13,
            RightLowerLeg = 14,
            RightFoot = 15,
            Count = 16
        }

        private struct BoneCalibration
        {
            public Transform Bone;
            public Transform Child;
            public RigJoint Start;
            public RigJoint End;
            public Vector3 BaseLocalDirection;
            public Quaternion BaseLocalRotation;
            public Quaternion BaseWorldRotation;
            public Vector3 BaseWorldDirection;
            public Vector3 SourceCalibrationDirectionWorld;
            public Quaternion RigNeutralWorldRotation;
            public bool HasSourceCalibration;
        }

        [Header("Source")]
        [SerializeField] private PoseUdpReceiver receiver;
        [SerializeField] private bool autoFindReceiverOnSameObject = true;

        [Header("Target Bones")]
        [SerializeField] private Transform rigRoot;
        [SerializeField] private Transform hips;
        [SerializeField] private Transform spine;
        [SerializeField] private Transform chest;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftUpperArm;
        [SerializeField] private Transform leftLowerArm;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightUpperArm;
        [SerializeField] private Transform rightLowerArm;
        [SerializeField] private Transform rightHand;
        [SerializeField] private Transform leftUpperLeg;
        [SerializeField] private Transform leftLowerLeg;
        [SerializeField] private Transform leftFoot;
        [SerializeField] private Transform rightUpperLeg;
        [SerializeField] private Transform rightLowerLeg;
        [SerializeField] private Transform rightFoot;

        [Header("Model Binding")]
        [SerializeField] private Animator targetAnimator;
        [SerializeField] private bool autoBindHumanoidAnimatorOnAwake = true;
        [SerializeField] private bool disableProxyWhenHumanoidBound = true;

        [Header("Proxy Rig")]
        [SerializeField] private bool autoCreateProxyRig = true;
        [SerializeField] private string proxyRigName = "ProxyRig";
        [SerializeField] private bool proxyOneToOnePositionMode = false;
        [SerializeField] private bool drawProxyGizmos = true;
        [SerializeField] private Color proxyBoneColor = new Color(1.0f, 0.65f, 0.2f, 1.0f);
        [SerializeField] private float proxyPointRadius = 0.018f;

        [Header("Coordinate Mapping")]
        [SerializeField] private bool useHipCenterAsOrigin = true;
        [SerializeField] private bool flipX = false;
        [SerializeField] private bool flipY = true;
        [SerializeField] private bool flipZ = false;
        [SerializeField] private bool swapLeftRightLandmarks = false;
        [SerializeField] private bool autoUprightByShoulderHip = false;
        [SerializeField] private float scaleMeters = 1.0f;
        [SerializeField] private Vector3 worldOffset = new Vector3(0.0f, 1.0f, 0.0f);

        [Header("Calibration")]
        [SerializeField] private bool autoCalibrateSourcePoseOnFirstFrame = true;
        [SerializeField] private bool autoFitProxyBoneLengthsOnSourceCalibration = true;

        [Header("Apply")]
        [SerializeField] private bool applyRootPosition = true;
        [SerializeField, Range(0.01f, 1f)] private float rootPositionLerp = 0.35f;
        [SerializeField, Range(0.01f, 1f)] private float rotationLerp = 0.35f;

        private readonly Vector3[] _converted = new Vector3[33];
        private readonly Vector3[] _rigTargets = new Vector3[(int)RigJoint.Count];
        private readonly List<BoneCalibration> _boneCalibrations = new List<BoneCalibration>(12);
        private bool _isCalibrated;
        private bool _hasSourceCalibration;
        private bool _hasProxyLengthFitted;

        private void Reset()
        {
            receiver = GetComponent<PoseUdpReceiver>();
            targetAnimator = GetComponentInChildren<Animator>();
        }

        private void OnValidate()
        {
            scaleMeters = Mathf.Max(0.001f, scaleMeters);
            proxyPointRadius = Mathf.Max(0.002f, proxyPointRadius);
            if (autoFindReceiverOnSameObject && receiver == null)
            {
                receiver = GetComponent<PoseUdpReceiver>();
            }
        }

        private void Awake()
        {
            if (autoBindHumanoidAnimatorOnAwake)
            {
                TryAutoBindHumanoidAnimator(verbose: false);
            }
            EnsureRigReady();
        }

        [ContextMenu("Auto Bind Humanoid Bones From Animator")]
        private void AutoBindHumanoidBonesFromAnimator()
        {
            if (!TryAutoBindHumanoidAnimator(verbose: true))
            {
                Debug.LogWarning("[PoseRetargeter] 自动绑定失败：未找到可用 Humanoid Animator。");
                return;
            }

            EnsureRigReady();
            Debug.Log("[PoseRetargeter] 已完成 Humanoid 自动绑骨。");
        }

        private bool TryAutoBindHumanoidAnimator(bool verbose)
        {
            if (targetAnimator == null)
            {
                targetAnimator = FindBestHumanoidAnimator();
            }

            if (targetAnimator == null)
            {
                return false;
            }

            if (!targetAnimator.isHuman)
            {
                if (verbose)
                {
                    Debug.LogWarning("[PoseRetargeter] Animator 不是 Humanoid，无法自动绑骨。");
                }
                return false;
            }

            Transform bindHips = targetAnimator.GetBoneTransform(HumanBodyBones.Hips);
            Transform bindSpine = targetAnimator.GetBoneTransform(HumanBodyBones.Spine);
            Transform bindChest = FirstNonNull(
                targetAnimator.GetBoneTransform(HumanBodyBones.Chest),
                targetAnimator.GetBoneTransform(HumanBodyBones.UpperChest),
                bindSpine
            );
            Transform bindHead = targetAnimator.GetBoneTransform(HumanBodyBones.Head);
            Transform bindLeftUpperArm = targetAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform bindLeftLowerArm = targetAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Transform bindLeftHand = targetAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform bindRightUpperArm = targetAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform bindRightLowerArm = targetAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Transform bindRightHand = targetAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform bindLeftUpperLeg = targetAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Transform bindLeftLowerLeg = targetAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            Transform bindLeftFoot = targetAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform bindRightUpperLeg = targetAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            Transform bindRightLowerLeg = targetAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            Transform bindRightFoot = targetAnimator.GetBoneTransform(HumanBodyBones.RightFoot);

            bool complete = bindHips != null &&
                            bindSpine != null &&
                            bindChest != null &&
                            bindHead != null &&
                            bindLeftUpperArm != null &&
                            bindLeftLowerArm != null &&
                            bindLeftHand != null &&
                            bindRightUpperArm != null &&
                            bindRightLowerArm != null &&
                            bindRightHand != null &&
                            bindLeftUpperLeg != null &&
                            bindLeftLowerLeg != null &&
                            bindLeftFoot != null &&
                            bindRightUpperLeg != null &&
                            bindRightLowerLeg != null &&
                            bindRightFoot != null;
            if (!complete)
            {
                if (verbose)
                {
                    Debug.LogWarning("[PoseRetargeter] Humanoid 骨骼不完整，无法自动绑骨。");
                }
                return false;
            }

            rigRoot = bindHips.root != null ? bindHips.root : targetAnimator.transform;
            hips = bindHips;
            spine = bindSpine;
            chest = bindChest;
            head = bindHead;
            leftUpperArm = bindLeftUpperArm;
            leftLowerArm = bindLeftLowerArm;
            leftHand = bindLeftHand;
            rightUpperArm = bindRightUpperArm;
            rightLowerArm = bindRightLowerArm;
            rightHand = bindRightHand;
            leftUpperLeg = bindLeftUpperLeg;
            leftLowerLeg = bindLeftLowerLeg;
            leftFoot = bindLeftFoot;
            rightUpperLeg = bindRightUpperLeg;
            rightLowerLeg = bindRightLowerLeg;
            rightFoot = bindRightFoot;

            if (disableProxyWhenHumanoidBound)
            {
                autoCreateProxyRig = false;
            }

            _isCalibrated = false;
            _hasSourceCalibration = false;
            _hasProxyLengthFitted = false;
            return true;
        }

        private Animator FindBestHumanoidAnimator()
        {
            Animator local = GetComponentInChildren<Animator>(true);
            if (local != null && local.isHuman)
            {
                return local;
            }

            Animator[] all = Object.FindObjectsOfType<Animator>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Animator a = all[i];
                if (a == null || !a.isHuman)
                {
                    continue;
                }

                if (a.gameObject == gameObject)
                {
                    continue;
                }

                return a;
            }

            return null;
        }

        private static Transform FirstNonNull(params Transform[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] != null)
                {
                    return candidates[i];
                }
            }
            return null;
        }

        [ContextMenu("Build Proxy Rig Now")]
        private void BuildProxyRigNow()
        {
            BuildOrAttachProxyRig();
            if (HasFullRig())
            {
                CalibrateRigDirections();
            }
        }

        [ContextMenu("Capture Source Calibration (Current Pose)")]
        private void CaptureSourceCalibrationFromCurrentPose()
        {
            if (receiver == null || !receiver.HasLatestPacket)
            {
                Debug.LogWarning("[PoseRetargeter] 无可用姿态帧，无法进行源姿态标定。");
                return;
            }

            PosePacket packet = receiver.LatestPacket;
            if (packet == null || packet.world_landmarks == null || packet.world_landmarks.Length < 33)
            {
                Debug.LogWarning("[PoseRetargeter] 当前姿态包不完整，无法进行源姿态标定。");
                return;
            }

            EnsureRigReady();
            if (!_isCalibrated)
            {
                Debug.LogWarning("[PoseRetargeter] 骨骼尚未校准，请先生成/绑定骨架。");
                return;
            }

            BuildConvertedLandmarks(packet);
            BuildRigTargets();
            ApplySourceCalibrationFromCurrentTargets(forceRefitProxyLengths: true);
            Debug.Log("[PoseRetargeter] 已捕获当前源姿态作为重定向标定姿态。");
        }

        [ContextMenu("Reset Source Calibration")]
        private void ResetSourceCalibration()
        {
            _hasSourceCalibration = false;
            for (int i = 0; i < _boneCalibrations.Count; i++)
            {
                BoneCalibration c = _boneCalibrations[i];
                c.HasSourceCalibration = false;
                c.SourceCalibrationDirectionWorld = Vector3.zero;
                c.RigNeutralWorldRotation = c.BaseWorldRotation;
                _boneCalibrations[i] = c;
            }
            Debug.Log("[PoseRetargeter] 已清除源姿态标定。");
        }

        private void Update()
        {
            if (autoFindReceiverOnSameObject && receiver == null)
            {
                receiver = GetComponent<PoseUdpReceiver>();
            }

            if (receiver == null || !receiver.HasLatestPacket)
            {
                return;
            }

            EnsureRigReady();
            if (!HasFullRig())
            {
                return;
            }

            PosePacket packet = receiver.LatestPacket;
            if (packet == null || packet.world_landmarks == null || packet.world_landmarks.Length < 33)
            {
                return;
            }

            BuildConvertedLandmarks(packet);
            BuildRigTargets();
            if (!_hasSourceCalibration && autoCalibrateSourcePoseOnFirstFrame)
            {
                ApplySourceCalibrationFromCurrentTargets(forceRefitProxyLengths: false);
            }
            ApplyRetargeting();
        }

        private void EnsureRigReady()
        {
            if (autoCreateProxyRig && !HasFullRig())
            {
                BuildOrAttachProxyRig();
            }

            if (!_isCalibrated && HasFullRig())
            {
                CalibrateRigDirections();
            }
        }

        private bool HasFullRig()
        {
            return hips != null &&
                   spine != null &&
                   chest != null &&
                   head != null &&
                   leftUpperArm != null &&
                   leftLowerArm != null &&
                   leftHand != null &&
                   rightUpperArm != null &&
                   rightLowerArm != null &&
                   rightHand != null &&
                   leftUpperLeg != null &&
                   leftLowerLeg != null &&
                   leftFoot != null &&
                   rightUpperLeg != null &&
                   rightLowerLeg != null &&
                   rightFoot != null;
        }

        private void BuildOrAttachProxyRig()
        {
            if (rigRoot == null)
            {
                GameObject existed = GameObject.Find(proxyRigName);
                rigRoot = existed != null ? existed.transform : new GameObject(proxyRigName).transform;
            }

            if (rigRoot.parent != null)
            {
                rigRoot.SetParent(null, true);
            }

            hips = EnsureChild(rigRoot, "Hips", new Vector3(0f, 1.0f, 0f));
            spine = EnsureChild(hips, "Spine", new Vector3(0f, 0.22f, 0f));
            chest = EnsureChild(spine, "Chest", new Vector3(0f, 0.20f, 0f));
            head = EnsureChild(chest, "Head", new Vector3(0f, 0.30f, 0f));

            leftUpperArm = EnsureChild(chest, "LeftUpperArm", new Vector3(-0.20f, 0.12f, 0f));
            leftLowerArm = EnsureChild(leftUpperArm, "LeftLowerArm", new Vector3(-0.23f, 0f, 0f));
            leftHand = EnsureChild(leftLowerArm, "LeftHand", new Vector3(-0.20f, 0f, 0f));

            rightUpperArm = EnsureChild(chest, "RightUpperArm", new Vector3(0.20f, 0.12f, 0f));
            rightLowerArm = EnsureChild(rightUpperArm, "RightLowerArm", new Vector3(0.23f, 0f, 0f));
            rightHand = EnsureChild(rightLowerArm, "RightHand", new Vector3(0.20f, 0f, 0f));

            leftUpperLeg = EnsureChild(hips, "LeftUpperLeg", new Vector3(-0.11f, -0.30f, 0f));
            leftLowerLeg = EnsureChild(leftUpperLeg, "LeftLowerLeg", new Vector3(0f, -0.35f, 0f));
            leftFoot = EnsureChild(leftLowerLeg, "LeftFoot", new Vector3(0f, -0.33f, 0.08f));

            rightUpperLeg = EnsureChild(hips, "RightUpperLeg", new Vector3(0.11f, -0.30f, 0f));
            rightLowerLeg = EnsureChild(rightUpperLeg, "RightLowerLeg", new Vector3(0f, -0.35f, 0f));
            rightFoot = EnsureChild(rightLowerLeg, "RightFoot", new Vector3(0f, -0.33f, 0.08f));

            _isCalibrated = false;
            _hasSourceCalibration = false;
            _hasProxyLengthFitted = false;
        }

        private static Transform EnsureChild(Transform parent, string childName, Vector3 defaultLocalPosition)
        {
            Transform child = parent.Find(childName);
            if (child == null)
            {
                GameObject go = new GameObject(childName);
                child = go.transform;
                child.SetParent(parent, false);
            }

            child.localPosition = defaultLocalPosition;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            return child;
        }

        private void BuildConvertedLandmarks(PosePacket packet)
        {
            for (int i = 0; i < 33; i++)
            {
                PoseLandmark lm = packet.world_landmarks[i];
                float x = lm.x;
                float y = lm.y;
                float z = lm.z;

                if (flipX) x = -x;
                if (flipY) y = -y;
                if (flipZ) z = -z;

                _converted[i] = new Vector3(x, y, z);
            }

            if (autoUprightByShoulderHip)
            {
                float shoulderY = (_converted[11].y + _converted[12].y) * 0.5f;
                float hipY = (_converted[23].y + _converted[24].y) * 0.5f;
                if (shoulderY < hipY)
                {
                    for (int i = 0; i < 33; i++)
                    {
                        Vector3 p = _converted[i];
                        p.y = -p.y;
                        _converted[i] = p;
                    }
                }
            }
        }

        private void BuildRigTargets()
        {
            int leftShoulder = swapLeftRightLandmarks ? 12 : 11;
            int rightShoulder = swapLeftRightLandmarks ? 11 : 12;
            int leftElbow = swapLeftRightLandmarks ? 14 : 13;
            int rightElbow = swapLeftRightLandmarks ? 13 : 14;
            int leftWrist = swapLeftRightLandmarks ? 16 : 15;
            int rightWrist = swapLeftRightLandmarks ? 15 : 16;
            int leftHip = swapLeftRightLandmarks ? 24 : 23;
            int rightHip = swapLeftRightLandmarks ? 23 : 24;
            int leftKnee = swapLeftRightLandmarks ? 26 : 25;
            int rightKnee = swapLeftRightLandmarks ? 25 : 26;
            int leftAnkle = swapLeftRightLandmarks ? 28 : 27;
            int rightAnkle = swapLeftRightLandmarks ? 27 : 28;

            Vector3 hipCenter = (_converted[leftHip] + _converted[rightHip]) * 0.5f;
            Vector3 shoulderCenter = (_converted[leftShoulder] + _converted[rightShoulder]) * 0.5f;
            Vector3 headPoint = (_converted[0] + _converted[7] + _converted[8]) / 3f;
            Vector3 rootOrigin = useHipCenterAsOrigin ? hipCenter : Vector3.zero;

            _rigTargets[(int)RigJoint.Hips] = hipCenter;
            _rigTargets[(int)RigJoint.Spine] = Vector3.Lerp(hipCenter, shoulderCenter, 0.5f);
            _rigTargets[(int)RigJoint.Chest] = shoulderCenter;
            _rigTargets[(int)RigJoint.Head] = headPoint;

            _rigTargets[(int)RigJoint.LeftUpperArm] = _converted[leftShoulder];
            _rigTargets[(int)RigJoint.LeftLowerArm] = _converted[leftElbow];
            _rigTargets[(int)RigJoint.LeftHand] = _converted[leftWrist];
            _rigTargets[(int)RigJoint.RightUpperArm] = _converted[rightShoulder];
            _rigTargets[(int)RigJoint.RightLowerArm] = _converted[rightElbow];
            _rigTargets[(int)RigJoint.RightHand] = _converted[rightWrist];

            _rigTargets[(int)RigJoint.LeftUpperLeg] = _converted[leftHip];
            _rigTargets[(int)RigJoint.LeftLowerLeg] = _converted[leftKnee];
            _rigTargets[(int)RigJoint.LeftFoot] = _converted[leftAnkle];
            _rigTargets[(int)RigJoint.RightUpperLeg] = _converted[rightHip];
            _rigTargets[(int)RigJoint.RightLowerLeg] = _converted[rightKnee];
            _rigTargets[(int)RigJoint.RightFoot] = _converted[rightAnkle];

            for (int i = 0; i < (int)RigJoint.Count; i++)
            {
                _rigTargets[i] = (_rigTargets[i] - rootOrigin) * scaleMeters + worldOffset;
            }
        }

        private void CalibrateRigDirections()
        {
            _boneCalibrations.Clear();
            _hasSourceCalibration = false;
            TryAddCalibration(hips, spine, RigJoint.Hips, RigJoint.Spine);
            TryAddCalibration(spine, chest, RigJoint.Spine, RigJoint.Chest);
            TryAddCalibration(chest, head, RigJoint.Chest, RigJoint.Head);

            TryAddCalibration(leftUpperArm, leftLowerArm, RigJoint.LeftUpperArm, RigJoint.LeftLowerArm);
            TryAddCalibration(leftLowerArm, leftHand, RigJoint.LeftLowerArm, RigJoint.LeftHand);
            TryAddCalibration(rightUpperArm, rightLowerArm, RigJoint.RightUpperArm, RigJoint.RightLowerArm);
            TryAddCalibration(rightLowerArm, rightHand, RigJoint.RightLowerArm, RigJoint.RightHand);

            TryAddCalibration(leftUpperLeg, leftLowerLeg, RigJoint.LeftUpperLeg, RigJoint.LeftLowerLeg);
            TryAddCalibration(leftLowerLeg, leftFoot, RigJoint.LeftLowerLeg, RigJoint.LeftFoot);
            TryAddCalibration(rightUpperLeg, rightLowerLeg, RigJoint.RightUpperLeg, RigJoint.RightLowerLeg);
            TryAddCalibration(rightLowerLeg, rightFoot, RigJoint.RightLowerLeg, RigJoint.RightFoot);
            _isCalibrated = _boneCalibrations.Count > 0;
        }

        private void TryAddCalibration(Transform bone, Transform child, RigJoint start, RigJoint end)
        {
            if (bone == null || child == null)
            {
                return;
            }

            Quaternion parentRotation = bone.parent != null ? bone.parent.rotation : Quaternion.identity;
            Vector3 worldDir = child.position - bone.position;
            if (worldDir.sqrMagnitude < 1e-8f)
            {
                worldDir = Vector3.up;
            }

            BoneCalibration calibration = new BoneCalibration
            {
                Bone = bone,
                Child = child,
                Start = start,
                End = end,
                BaseLocalDirection = (Quaternion.Inverse(parentRotation) * worldDir).normalized,
                BaseLocalRotation = bone.localRotation,
                BaseWorldRotation = bone.rotation,
                BaseWorldDirection = worldDir.normalized,
                SourceCalibrationDirectionWorld = Vector3.zero,
                RigNeutralWorldRotation = bone.rotation,
                HasSourceCalibration = false
            };
            _boneCalibrations.Add(calibration);
        }

        private void CaptureSourceCalibrationFromRigTargets()
        {
            if (_boneCalibrations.Count <= 0)
            {
                return;
            }

            for (int i = 0; i < _boneCalibrations.Count; i++)
            {
                BoneCalibration c = _boneCalibrations[i];
                Vector3 start = _rigTargets[(int)c.Start];
                Vector3 end = _rigTargets[(int)c.End];
                Vector3 dir = end - start;
                if (dir.sqrMagnitude < 1e-8f)
                {
                    c.HasSourceCalibration = false;
                    c.SourceCalibrationDirectionWorld = Vector3.zero;
                    c.RigNeutralWorldRotation = c.BaseWorldRotation;
                }
                else
                {
                    c.HasSourceCalibration = true;
                    c.SourceCalibrationDirectionWorld = dir.normalized;
                    Quaternion neutralAlign = Quaternion.FromToRotation(
                        c.BaseWorldDirection,
                        c.SourceCalibrationDirectionWorld
                    );
                    c.RigNeutralWorldRotation = neutralAlign * c.BaseWorldRotation;
                }
                _boneCalibrations[i] = c;
            }

            _hasSourceCalibration = true;
        }

        private void ApplySourceCalibrationFromCurrentTargets(bool forceRefitProxyLengths)
        {
            if (autoCreateProxyRig &&
                autoFitProxyBoneLengthsOnSourceCalibration &&
                (forceRefitProxyLengths || !_hasProxyLengthFitted))
            {
                FitProxyBoneLengthsFromTargets();
                _hasProxyLengthFitted = true;
                CalibrateRigDirections();
            }

            CaptureSourceCalibrationFromRigTargets();
        }

        private float TargetLength(RigJoint a, RigJoint b)
        {
            return Vector3.Distance(_rigTargets[(int)a], _rigTargets[(int)b]);
        }

        private void FitProxyBoneLengthsFromTargets()
        {
            FitBoneLength(hips, spine, TargetLength(RigJoint.Hips, RigJoint.Spine));
            FitBoneLength(spine, chest, TargetLength(RigJoint.Spine, RigJoint.Chest));
            FitBoneLength(chest, head, TargetLength(RigJoint.Chest, RigJoint.Head));

            FitBoneLength(chest, leftUpperArm, TargetLength(RigJoint.Chest, RigJoint.LeftUpperArm));
            FitBoneLength(leftUpperArm, leftLowerArm, TargetLength(RigJoint.LeftUpperArm, RigJoint.LeftLowerArm));
            FitBoneLength(leftLowerArm, leftHand, TargetLength(RigJoint.LeftLowerArm, RigJoint.LeftHand));

            FitBoneLength(chest, rightUpperArm, TargetLength(RigJoint.Chest, RigJoint.RightUpperArm));
            FitBoneLength(rightUpperArm, rightLowerArm, TargetLength(RigJoint.RightUpperArm, RigJoint.RightLowerArm));
            FitBoneLength(rightLowerArm, rightHand, TargetLength(RigJoint.RightLowerArm, RigJoint.RightHand));

            FitBoneLength(hips, leftUpperLeg, TargetLength(RigJoint.Hips, RigJoint.LeftUpperLeg));
            FitBoneLength(leftUpperLeg, leftLowerLeg, TargetLength(RigJoint.LeftUpperLeg, RigJoint.LeftLowerLeg));
            FitBoneLength(leftLowerLeg, leftFoot, TargetLength(RigJoint.LeftLowerLeg, RigJoint.LeftFoot));

            FitBoneLength(hips, rightUpperLeg, TargetLength(RigJoint.Hips, RigJoint.RightUpperLeg));
            FitBoneLength(rightUpperLeg, rightLowerLeg, TargetLength(RigJoint.RightUpperLeg, RigJoint.RightLowerLeg));
            FitBoneLength(rightLowerLeg, rightFoot, TargetLength(RigJoint.RightLowerLeg, RigJoint.RightFoot));
        }

        private static void FitBoneLength(Transform parentBone, Transform childBone, float targetLength)
        {
            if (parentBone == null || childBone == null)
            {
                return;
            }

            if (targetLength < 1e-5f)
            {
                return;
            }

            Vector3 local = childBone.localPosition;
            Vector3 dir = local.sqrMagnitude > 1e-8f ? local.normalized : Vector3.up;
            childBone.localPosition = dir * targetLength;
        }

        private void ApplyRetargeting()
        {
            if (autoCreateProxyRig && proxyOneToOnePositionMode)
            {
                ApplyProxyOneToOnePositions();
                return;
            }

            if (!_isCalibrated)
            {
                return;
            }

            if (applyRootPosition && hips != null)
            {
                Vector3 targetRoot = _rigTargets[(int)RigJoint.Hips];
                hips.position = Vector3.Lerp(hips.position, targetRoot, Mathf.Clamp01(rootPositionLerp));
            }

            float rotBlend = Mathf.Clamp01(rotationLerp);
            for (int i = 0; i < _boneCalibrations.Count; i++)
            {
                BoneCalibration c = _boneCalibrations[i];
                if (c.Bone == null)
                {
                    continue;
                }

                Vector3 start = _rigTargets[(int)c.Start];
                Vector3 end = _rigTargets[(int)c.End];
                Vector3 targetDirWorld = end - start;
                if (targetDirWorld.sqrMagnitude < 1e-8f)
                {
                    continue;
                }

                Quaternion targetLocalRotation;
                if (_hasSourceCalibration && c.HasSourceCalibration)
                {
                    Quaternion sourceDelta = Quaternion.FromToRotation(c.SourceCalibrationDirectionWorld, targetDirWorld.normalized);
                    Quaternion targetWorldRotation = sourceDelta * c.RigNeutralWorldRotation;
                    Quaternion parentRot = c.Bone.parent != null ? c.Bone.parent.rotation : Quaternion.identity;
                    targetLocalRotation = Quaternion.Inverse(parentRot) * targetWorldRotation;
                }
                else
                {
                    Quaternion parentRot = c.Bone.parent != null ? c.Bone.parent.rotation : Quaternion.identity;
                    Vector3 targetLocalDir = (Quaternion.Inverse(parentRot) * targetDirWorld).normalized;
                    Quaternion delta = Quaternion.FromToRotation(c.BaseLocalDirection, targetLocalDir);
                    targetLocalRotation = delta * c.BaseLocalRotation;
                }

                c.Bone.localRotation = Quaternion.Slerp(c.Bone.localRotation, targetLocalRotation, rotBlend);
            }
        }

        private void ApplyProxyOneToOnePositions()
        {
            SetJointWorldPosition(hips, RigJoint.Hips);
            SetJointWorldPosition(spine, RigJoint.Spine);
            SetJointWorldPosition(chest, RigJoint.Chest);
            SetJointWorldPosition(head, RigJoint.Head);

            SetJointWorldPosition(leftUpperArm, RigJoint.LeftUpperArm);
            SetJointWorldPosition(leftLowerArm, RigJoint.LeftLowerArm);
            SetJointWorldPosition(leftHand, RigJoint.LeftHand);
            SetJointWorldPosition(rightUpperArm, RigJoint.RightUpperArm);
            SetJointWorldPosition(rightLowerArm, RigJoint.RightLowerArm);
            SetJointWorldPosition(rightHand, RigJoint.RightHand);

            SetJointWorldPosition(leftUpperLeg, RigJoint.LeftUpperLeg);
            SetJointWorldPosition(leftLowerLeg, RigJoint.LeftLowerLeg);
            SetJointWorldPosition(leftFoot, RigJoint.LeftFoot);
            SetJointWorldPosition(rightUpperLeg, RigJoint.RightUpperLeg);
            SetJointWorldPosition(rightLowerLeg, RigJoint.RightLowerLeg);
            SetJointWorldPosition(rightFoot, RigJoint.RightFoot);
        }

        private void SetJointWorldPosition(Transform joint, RigJoint id)
        {
            if (joint == null)
            {
                return;
            }

            joint.position = _rigTargets[(int)id];
        }

        private void OnDrawGizmos()
        {
            if (!drawProxyGizmos || !HasFullRig())
            {
                return;
            }

            Gizmos.color = proxyBoneColor;
            DrawConnection(hips, spine);
            DrawConnection(spine, chest);
            DrawConnection(chest, head);
            DrawConnection(chest, leftUpperArm);
            DrawConnection(leftUpperArm, leftLowerArm);
            DrawConnection(leftLowerArm, leftHand);
            DrawConnection(chest, rightUpperArm);
            DrawConnection(rightUpperArm, rightLowerArm);
            DrawConnection(rightLowerArm, rightHand);
            DrawConnection(hips, leftUpperLeg);
            DrawConnection(leftUpperLeg, leftLowerLeg);
            DrawConnection(leftLowerLeg, leftFoot);
            DrawConnection(hips, rightUpperLeg);
            DrawConnection(rightUpperLeg, rightLowerLeg);
            DrawConnection(rightLowerLeg, rightFoot);

            DrawJointPoint(hips);
            DrawJointPoint(spine);
            DrawJointPoint(chest);
            DrawJointPoint(head);
            DrawJointPoint(leftUpperArm);
            DrawJointPoint(leftLowerArm);
            DrawJointPoint(leftHand);
            DrawJointPoint(rightUpperArm);
            DrawJointPoint(rightLowerArm);
            DrawJointPoint(rightHand);
            DrawJointPoint(leftUpperLeg);
            DrawJointPoint(leftLowerLeg);
            DrawJointPoint(leftFoot);
            DrawJointPoint(rightUpperLeg);
            DrawJointPoint(rightLowerLeg);
            DrawJointPoint(rightFoot);
        }

        private static void DrawConnection(Transform a, Transform b)
        {
            if (a == null || b == null)
            {
                return;
            }
            Gizmos.DrawLine(a.position, b.position);
        }

        private void DrawJointPoint(Transform t)
        {
            if (t == null)
            {
                return;
            }
            Gizmos.DrawSphere(t.position, proxyPointRadius);
        }
    }
}
