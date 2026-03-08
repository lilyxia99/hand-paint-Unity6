using System;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace FingerPaint
{
    /// <summary>
    /// Reads XRHandSubsystem joint data for all 10 fingertips each frame.
    /// Exposes per-finger world-space positions and "active" state (finger extended).
    /// </summary>
    public class HandTrackingManager : MonoBehaviour
    {
        public enum FingerID
        {
            LeftThumb, LeftIndex, LeftMiddle, LeftRing, LeftLittle,
            RightThumb, RightIndex, RightMiddle, RightRing, RightLittle
        }

        public const int FingerCount = 10;

        public struct FingerState
        {
            public bool IsTracked;
            public bool IsExtended;
            public Vector3 TipPosition;
        }

        public FingerState[] Fingers { get; } = new FingerState[FingerCount];

        /// <summary>
        /// Extension threshold: if the tip-to-metacarpal distance relative to
        /// proximal-to-metacarpal distance exceeds this ratio, the finger counts
        /// as extended. Thumb uses a simpler angle check.
        /// </summary>
        [SerializeField] private float _extensionRatio = 0.85f;

        private XRHandSubsystem _handSubsystem;

        private static readonly XRHandJointID[] TipJoints =
        {
            XRHandJointID.ThumbTip,
            XRHandJointID.IndexTip,
            XRHandJointID.MiddleTip,
            XRHandJointID.RingTip,
            XRHandJointID.LittleTip
        };

        private static readonly XRHandJointID[] DistalJoints =
        {
            XRHandJointID.ThumbDistal,
            XRHandJointID.IndexDistal,
            XRHandJointID.MiddleDistal,
            XRHandJointID.RingDistal,
            XRHandJointID.LittleDistal
        };

        private static readonly XRHandJointID[] ProximalJoints =
        {
            XRHandJointID.ThumbProximal,
            XRHandJointID.IndexProximal,
            XRHandJointID.MiddleProximal,
            XRHandJointID.RingProximal,
            XRHandJointID.LittleProximal
        };

        private static readonly XRHandJointID[] MetacarpalJoints =
        {
            XRHandJointID.ThumbMetacarpal,
            XRHandJointID.IndexMetacarpal,
            XRHandJointID.MiddleMetacarpal,
            XRHandJointID.RingMetacarpal,
            XRHandJointID.LittleMetacarpal
        };

        private void Update()
        {
            EnsureSubsystem();
            if (_handSubsystem == null || !_handSubsystem.running)
                return;

            ReadHand(_handSubsystem.leftHand, 0);
            ReadHand(_handSubsystem.rightHand, 5);
        }

        private void ReadHand(XRHand hand, int offset)
        {
            if (!hand.isTracked)
            {
                for (int i = 0; i < 5; i++)
                    Fingers[offset + i].IsTracked = false;
                return;
            }

            for (int i = 0; i < 5; i++)
            {
                var tipJoint = hand.GetJoint(TipJoints[i]);
                var distalJoint = hand.GetJoint(DistalJoints[i]);
                var proximalJoint = hand.GetJoint(ProximalJoints[i]);
                var metacarpalJoint = hand.GetJoint(MetacarpalJoints[i]);

                bool tipTracked = tipJoint.TryGetPose(out Pose tipPose);
                bool proxTracked = proximalJoint.TryGetPose(out Pose proxPose);
                bool metaTracked = metacarpalJoint.TryGetPose(out Pose metaPose);
                distalJoint.TryGetPose(out Pose distalPose);

                Fingers[offset + i].IsTracked = tipTracked;

                if (tipTracked)
                    Fingers[offset + i].TipPosition = tipPose.position;

                // Determine extension: compare tip-to-metacarpal vs proximal-to-metacarpal distances
                if (tipTracked && proxTracked && metaTracked)
                {
                    float tipToMeta = Vector3.Distance(tipPose.position, metaPose.position);
                    float proxToMeta = Vector3.Distance(proxPose.position, metaPose.position);

                    Fingers[offset + i].IsExtended =
                        proxToMeta > 0.001f && (tipToMeta / proxToMeta) > _extensionRatio;
                }
                else
                {
                    Fingers[offset + i].IsExtended = false;
                }
            }
        }

        private void EnsureSubsystem()
        {
            if (_handSubsystem != null && _handSubsystem.running)
                return;

            var subsystems = new System.Collections.Generic.List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            _handSubsystem = subsystems.Count > 0 ? subsystems[0] : null;
        }

        /// <summary>
        /// Returns true if the specified hand's thumb is pointing generally upward
        /// and other fingers are curled. Used by GestureDetector.
        /// </summary>
        public bool IsThumbsUp(bool leftHand)
        {
            int offset = leftHand ? 0 : 5;

            // Thumb must be tracked and extended
            if (!Fingers[offset].IsTracked || !Fingers[offset].IsExtended)
                return false;

            // Other four fingers must not be extended (curled)
            for (int i = 1; i < 5; i++)
            {
                if (!Fingers[offset + i].IsTracked)
                    return false;
                if (Fingers[offset + i].IsExtended)
                    return false;
            }

            // Check thumb tip is above the metacarpal (pointing up-ish)
            XRHand hand = leftHand ? GetHand(true) : GetHand(false);
            if (hand.isTracked)
            {
                var thumbTip = hand.GetJoint(XRHandJointID.ThumbTip);
                var thumbMeta = hand.GetJoint(XRHandJointID.ThumbMetacarpal);
                if (thumbTip.TryGetPose(out Pose tipPose) && thumbMeta.TryGetPose(out Pose metaPose))
                {
                    // Thumb tip should be notably above metacarpal
                    if (tipPose.position.y - metaPose.position.y < 0.02f)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns the palm normal vector (pointing outward from the palm surface)
        /// for the specified hand. Useful for detecting palm-facing-self gestures.
        /// </summary>
        public bool TryGetPalmNormal(bool leftHand, out Vector3 palmNormal)
        {
            palmNormal = Vector3.zero;
            XRHand hand = GetHand(leftHand);

            if (!hand.isTracked)
                return false;

            var palmJoint = hand.GetJoint(XRHandJointID.Palm);
            if (!palmJoint.TryGetPose(out Pose palmPose))
                return false;

            // OpenXR convention: palm normal (outward from palm surface)
            // is the negative forward direction of the palm joint
            palmNormal = -palmPose.forward;
            return true;
        }

        private XRHand GetHand(bool left)
        {
            if (_handSubsystem == null)
                return default;
            return left ? _handSubsystem.leftHand : _handSubsystem.rightHand;
        }
    }
}
