using System;
using PaperSeizerStone.Presentation;
using UnityEngine;

namespace PaperSeizerStone.Configuration
{
    public sealed class BattleCameraDefinition : ScriptableObject
    {
        [Serializable]
        private struct CameraPose
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
        }

        [SerializeField] private CameraPose tablePose;
        [SerializeField] private CameraPose scoringPose;
        [SerializeField] private CameraPose shootingPose;
        [SerializeField, Min(0.05f)] private float transitionDuration = 0.3f;

        public float TransitionDuration => Mathf.Max(0.05f, transitionDuration);

        // Positions and rotations are relative to the BattleCameraRig root.
        public Pose GetLocalPose(BattleCameraPose pose)
        {
            CameraPose value;
            switch (pose)
            {
                case BattleCameraPose.Table: value = tablePose; break;
                case BattleCameraPose.Scoring: value = scoringPose; break;
                case BattleCameraPose.Shooting: value = shootingPose; break;
                default: throw new ArgumentOutOfRangeException(nameof(pose), pose, null);
            }
            return new Pose(value.localPosition, value.localRotation);
        }
    }
}
