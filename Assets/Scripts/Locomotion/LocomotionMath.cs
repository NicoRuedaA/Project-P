using System;
using UnityEngine;

namespace Wildbound.Locomotion
{
    // Port of arena-3d/src/gait.js, locomotion.js and multileg.js. All angles are radians.
    public static class LocomotionMath
    {
        public const float Tau = Mathf.PI * 2f;
        public static float Damp(float rate, float dt) { return 1f - Mathf.Exp(-rate * dt); }
        public static float Smooth(float value, float a, float b)
        {
            float t = Mathf.Clamp01((value - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
        public static float Wrap(float angle) { return Mathf.Atan2(Mathf.Sin(angle), Mathf.Cos(angle)); }
        public static float RelativeSpeed(float speed, float stride) { return speed / Mathf.Sqrt(9.81f * stride / 2f); }
        public static Vector3 GaitWeights(float relative)
        {
            float trot = Smooth(relative, .6f, .7f), gallop = Smooth(relative, 1.68f, 1.72f);
            return new Vector3(1f - trot, trot - gallop, gallop);
        }
        public static float DutyFactor(Vector3 weights, float relative)
        {
            float r = Mathf.Max(relative, 1e-6f);
            return weights.x * .65f + weights.y * Mathf.Min(.5f, .75f / r) + weights.z * Mathf.Min(.5f, .6f / r);
        }
        public static Vector3 Footfalls(bool right, bool front)
        {
            return -Tau * (front ? (right ? new Vector3(.75f, 1f, .6f) : new Vector3(.25f, .5f, .5f))
                                : (right ? new Vector3(.5f, .5f, .1f) : Vector3.zero));
        }
        public static Vector3 WaveFootfalls(bool right, int set, int sets)
        {
            float across = right ? .5f : 0f, wave = across + (float)set / Mathf.Max(1, sets);
            float tripod = wave + Mathf.Repeat(across + set / 2f - wave + 1.5f, 1f) - .5f;
            return -Tau * new Vector3(wave, tripod, tripod);
        }
        public struct Footing
        {
            public float cycle, sweep, lift;
            public bool stance;
        }
        public static Footing Step(float angle, float duty, bool linear = false)
        {
            duty = Mathf.Clamp(duty, .000001f, .999999f);
            float u = Mathf.Repeat((angle - Mathf.PI / 2f) / Tau, 1f);
            bool stance = u < duty;
            float turn = stance ? Mathf.PI / 2f + Mathf.PI * u / duty : Mathf.PI * 1.5f + Mathf.PI * (u - duty) / (1f - duty);
            return new Footing { cycle = u, stance = stance, sweep = stance && linear ? 1f - 2f * u / duty : Mathf.Sin(turn), lift = Mathf.Max(0f, Mathf.Cos(turn)) };
        }
        public struct Hop
        {
            public float lift, squash, pitch;
        }
        // hop.js (short bodies) and bounce.js (leaf/hanging-chain hoppers).
        public static Hop HopArc(float cycle, bool bouncing)
        {
            float air = bouncing ? .75f : .7f;
            cycle = Mathf.Repeat(cycle, 1f);
            bool aloft = cycle < air;
            float t = aloft ? cycle / air : (cycle - air) / (1f - air), curve = Mathf.Sin(Mathf.PI * t);
            return new Hop {
                lift = aloft ? curve : 0f,
                squash = (aloft ? (bouncing ? -.07f : -.06f) : (bouncing ? .16f : .14f)) * curve,
                pitch = bouncing ? .12f * (aloft ? Mathf.Cos(Mathf.PI * t) : -Mathf.Cos(Mathf.PI * t)) : (aloft ? -.12f * Mathf.Cos(Mathf.PI * t) : 0f)
            };
        }
        public static float Contraction(float phase)
        {
            float u = Mathf.Repeat(phase / Tau, 1f);
            float v = u < .3f ? Mathf.Sin(Mathf.PI / 2f * u / .3f) : Mathf.Cos(Mathf.PI / 2f * (u - .3f) / .7f);
            return v * v;
        }
        public struct Spring
        {
            public float value, velocity;
            public void Advance(float target, float rate, float ratio, float force, float dt, float limit)
            {
                for (float left = dt; left > 1e-9f; left -= 1f / 120f)
                {
                    float h = Mathf.Min(1f / 120f, left);
                    velocity += (rate * rate * (target - value) - 2f * ratio * rate * velocity + force) * h;
                    value += velocity * h;
                    if (Mathf.Abs(value) > limit) { value = Mathf.Sign(value) * limit; velocity = 0f; }
                }
            }
        }
        // Geometry-derived two-bone IK; no assumptions about imported joint local axes.
        public static void SolveLeg(Transform upper, Transform lower, Transform foot, Vector3 target, Vector3 pole, Quaternion sole)
        {
            Vector3 hip = upper.position, knee = lower.position, tip = foot.position;
            float a = Vector3.Distance(hip, knee), b = Vector3.Distance(knee, tip);
            if (a < 1e-6f || b < 1e-6f) return;
            Vector3 reach = target - hip;
            float d = Mathf.Clamp(reach.magnitude, Mathf.Abs(a - b) + 1e-5f, (a + b) * .999f);
            Vector3 direction = reach.sqrMagnitude > 1e-12f ? reach.normalized : Vector3.down;
            Vector3 bend = Vector3.ProjectOnPlane(pole, direction).normalized;
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.forward, direction).normalized;
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.right, direction).normalized;
            float along = (a * a + d * d - b * b) / (2f * d);
            Vector3 goalKnee = hip + direction * along + bend * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            upper.rotation = Quaternion.FromToRotation(knee - hip, goalKnee - hip) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(foot.position - lower.position, hip + direction * d - lower.position) * lower.rotation;
            foot.rotation = sole;
        }
    }

    [Serializable]
    public sealed class GaitDriver
    {
        public float phase, weight, speed, travel, distance, vertical, turn, time, duty = .5f;
        public Vector3 weights = new Vector3(1f, 0f, 0f), localDirection = Vector3.forward;
        public float[] offsets = new float[0], duties = new float[0];
        public bool teleported;
        Vector3 previous;
        float previousTime, previousYaw;
        bool hasYaw;
        bool hasPrevious;
        public void Reset() {
            phase = weight = speed = travel = distance = vertical = turn = time = 0f; duty = .5f;
            weights = new Vector3(1f, 0f, 0f); hasPrevious = false; hasYaw = false; teleported = false;
            localDirection = Vector3.forward; offsets = new float[0]; duties = new float[0];
        }
        public void Sample(Vector3 position, float timestamp, float dt, float yaw, bool eligible, bool stepping, float stride, Vector3[] footfalls)
        {
            if (!(dt > 0f)) return;
            time += dt;
            distance = 0f; teleported = false;
            bool fresh = !hasPrevious || timestamp != previousTime;
            if (fresh)
            {
                float elapsed = hasPrevious ? timestamp - previousTime : dt;
                Vector3 displacement = hasPrevious ? position - previous : Vector3.zero;
                float horizontal = new Vector2(displacement.x, displacement.z).magnitude;
                teleported = hasPrevious && (elapsed <= 0f || horizontal >= Mathf.Max(1f, elapsed * 12f));
                bool moving = eligible && elapsed > 0f && horizontal > .00001f && !teleported;
                distance = moving ? horizontal : 0f;
                speed = moving ? horizontal / elapsed : 0f;
                vertical = hasPrevious && elapsed > 0f && !teleported ? displacement.y / elapsed : 0f;
                travel += distance;
                if (moving)
                {
                    localDirection = new Vector3((displacement.x * Mathf.Cos(yaw) - displacement.z * Mathf.Sin(yaw)) / horizontal, 0f,
                                                 (displacement.x * Mathf.Sin(yaw) + displacement.z * Mathf.Cos(yaw)) / horizontal);
                    if (stepping)
                    {
                        float cycles = distance / Mathf.Max(.01f, stride) * 2f * duty;
                        phase = Mathf.Repeat(phase + cycles * LocomotionMath.Tau, LocomotionMath.Tau);
                        if (footfalls != null && footfalls.Length > 0) Settle(stride, footfalls, weight == 0f ? float.PositiveInfinity : cycles);
                    }
                }
                previous = position; previousTime = timestamp; hasPrevious = true;
            }
            turn += (Mathf.Clamp((hasYaw ? LocomotionMath.Wrap(yaw - previousYaw) / dt : 0f), -4f, 4f) - turn) * LocomotionMath.Damp(9f, dt);
            previousYaw = yaw; hasYaw = true;
            if (!eligible) speed = vertical = 0f;
            weight = eligible && stepping ? weight + ((speed > 0f ? 1f : 0f) - weight) * LocomotionMath.Damp(16f, dt) : 0f;
            if (weight < .001f) weight = 0f;
        }
        public void Settle(float stride, Vector3[] footfalls, float cycles)
        {
            if (float.IsPositiveInfinity(cycles) || offsets.Length != footfalls.Length)
            {
                weights = new Vector3(0f, 1f, 0f); duty = .5f; phase = 0f;
                offsets = new float[footfalls.Length]; duties = new float[footfalls.Length];
                for (int i = 0; i < footfalls.Length; i++) { offsets[i] = footfalls[i].y; duties[i] = duty; }
                return;
            }
            float relative = LocomotionMath.RelativeSpeed(speed, stride), blend = LocomotionMath.Damp(1.5f, cycles);
            weights += (LocomotionMath.GaitWeights(relative) - weights) * blend;
            float goalDuty = LocomotionMath.DutyFactor(weights, relative);
            duty += (goalDuty - duty) * (goalDuty < duty ? LocomotionMath.Damp(4.5f, cycles) : blend);
            for (int i = 0; i < footfalls.Length; i++)
            {
                float goal = Vector3.Dot(footfalls[i], weights), own = duties[i];
                if (LocomotionMath.Step(phase + offsets[i], own).stance) own = Mathf.Min(own, duty);
                else { offsets[i] += (goal - offsets[i]) * blend; own = Mathf.Min(duty, LocomotionMath.Step(phase + offsets[i], own).cycle); }
                duties[i] = own;
            }
        }
    }
}
