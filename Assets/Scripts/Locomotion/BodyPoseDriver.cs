using System;
using UnityEngine;

namespace Pokemon3D.Locomotion
{
    public enum BodyPoseMode { Idle, Walk, Run, Swim, Climb, Jump, Fall, Land, Flight, Glide, Dodge }

    // Non-attack portion of arena-3d/src/procedural.js advancePose.
    // All motion values are measured by GaitDriver; this state never moves an actor.
    [Serializable]
    public sealed class BodyPoseDriver
    {
        public BodyPoseMode mode;
        public float[] weights = new float[11];
        public float landing;
        bool sampledGround, previousGround;
        float previousVertical;
        public float this[BodyPoseMode value] => weights[(int)value];
        public void Reset() {
            Array.Clear(weights, 0, weights.Length); weights[0] = 1;
            mode = BodyPoseMode.Idle; landing = previousVertical = 0; sampledGround = previousGround = false;
        }
        public void Advance(GaitDriver motion, float dt, bool grounded, TraversalMode traversal, bool hover, bool boost, bool dodge) {
            if (!(dt > 0)) return;
            if (sampledGround && !previousGround && grounded) landing = Mathf.Clamp01(-previousVertical / 8f);
            else landing *= Mathf.Exp(-12f * dt);
            sampledGround = true; previousGround = grounded; previousVertical = motion.vertical;
            bool airborne = traversal == TraversalMode.Flight || traversal == TraversalMode.Glide || hover;
            mode = dodge ? BodyPoseMode.Dodge : traversal == TraversalMode.Climb ? BodyPoseMode.Climb
                 : traversal == TraversalMode.Swim ? BodyPoseMode.Swim : airborne
                 ? (traversal == TraversalMode.Glide || motion.vertical < -1f || motion.speed > 7f && !boost ? BodyPoseMode.Glide : BodyPoseMode.Flight)
                 : !grounded ? (motion.vertical > .2f ? BodyPoseMode.Jump : BodyPoseMode.Fall)
                 : landing > .08f ? BodyPoseMode.Land : motion.speed > 5f ? BodyPoseMode.Run : motion.speed > .08f ? BodyPoseMode.Walk : BodyPoseMode.Idle;
            float blend = LocomotionMath.Damp(12f, dt);
            for (int i = 0; i < weights.Length; i++) weights[i] += ((i == (int)mode ? 1f : 0f) - weights[i]) * blend;
        }
    }
}
