using System;
using System.Collections.Generic;
using UnityEngine;

namespace BotwVfx
{
    /// <summary>
    /// Base de todos los efectos. Un efecto BotW es una secuencia muy medida
    /// (anticipación -> pico -> disipación), así que aquí todo va por tiempos:
    /// - lanza sistemas de partículas y sub-efectos en su instante,
    /// - dispara sacudidas de cámara y cámara lenta,
    /// - anima las partes "a mano" (esferas, rayos, luces) en Evaluate(t).
    /// Preview(t) reproduce cualquier instante de forma determinista (lo usa el editor).
    /// </summary>
    public class VfxTimeline : MonoBehaviour
    {
        [Serializable]
        public class ParticleCue
        {
            public ParticleSystem system;
            public float time;
            [NonSerialized] public bool fired;
        }

        [Serializable]
        public class TimelineCue
        {
            public VfxTimeline timeline;
            public float time;
            [NonSerialized] public bool fired;
        }

        [Serializable]
        public class ShakeCue
        {
            public float time;
            public float strength = 0.3f;
            public float duration = 0.3f;
            [NonSerialized] public bool fired;
        }

        [Serializable]
        public class SlowMotionCue
        {
            public float time;
            [Range(0.01f, 1f)] public float timeScale = 0.2f;
            [Tooltip("Duración en segundos reales (sin escalar).")]
            public float duration = 0.12f;
            [NonSerialized] public bool fired;
        }

        [Header("Timeline")]
        public float duration = 3f;
        public bool playOnStart;
        public bool loop;
        public float loopDelay = 1f;

        [Header("Cues")]
        public List<ParticleCue> particles = new List<ParticleCue>();
        public List<TimelineCue> subEffects = new List<TimelineCue>();
        public List<ShakeCue> shakes = new List<ShakeCue>();
        public List<SlowMotionCue> slowMotion = new List<SlowMotionCue>();

        [Header("Flash de luz")]
        public Light flashLight;
        public AnimationCurve lightIntensity = AnimationCurve.Constant(0f, 1f, 0f);

        public bool IsPlaying => playing;
        public float CurrentTime => time;
        public event Action Finished;

        float time = -1f;
        bool playing;
        float loopTimer;

        protected virtual void Awake()
        {
            Evaluate(-1f);
            ApplyLight(-1f);
        }

        protected virtual void Start()
        {
            if (playOnStart)
                Play();
        }

        public void Play()
        {
            StopAndClear();
            time = 0f;
            playing = true;
            Tick(0f);
        }

        public void StopAndClear()
        {
            playing = false;
            time = -1f;
            foreach (var cue in particles)
            {
                cue.fired = false;
                if (cue.system != null)
                    cue.system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            foreach (var cue in subEffects)
            {
                cue.fired = false;
                if (cue.timeline != null)
                    cue.timeline.StopAndClear();
            }
            foreach (var cue in shakes) cue.fired = false;
            foreach (var cue in slowMotion) cue.fired = false;
            Evaluate(-1f);
            ApplyLight(-1f);
        }

        protected virtual void Update()
        {
            if (!playing)
            {
                if (loop && time < 0f)
                {
                    loopTimer += Time.deltaTime;
                    if (loopTimer >= loopDelay)
                    {
                        loopTimer = 0f;
                        Play();
                    }
                }
                return;
            }

            time += Time.deltaTime;
            Tick(time);

            if (time >= duration)
            {
                playing = false;
                time = -1f;
                Evaluate(-1f);
                ApplyLight(-1f);
                Finished?.Invoke();
            }
        }

        void Tick(float t)
        {
            // Primero colocamos las partes animadas (algunos emisores dependen de ellas).
            Evaluate(t);
            ApplyLight(t);

            foreach (var cue in particles)
            {
                if (!cue.fired && t >= cue.time && cue.system != null)
                {
                    cue.fired = true;
                    cue.system.Play(false);
                }
            }
            foreach (var cue in subEffects)
            {
                if (!cue.fired && t >= cue.time && cue.timeline != null)
                {
                    cue.fired = true;
                    cue.timeline.Play();
                }
            }
            foreach (var cue in shakes)
            {
                if (!cue.fired && t >= cue.time)
                {
                    cue.fired = true;
                    VfxDirector.Shake(cue.strength, cue.duration);
                }
            }
            foreach (var cue in slowMotion)
            {
                if (!cue.fired && t >= cue.time)
                {
                    cue.fired = true;
                    VfxDirector.SlowMotion(cue.timeScale, cue.duration);
                }
            }
        }

        /// <summary>
        /// Muestra el efecto congelado en el instante t (t &lt; 0 = estado de reposo).
        /// Sirve para previsualizar en el editor y para generar capturas.
        /// </summary>
        public void Preview(float t)
        {
            Evaluate(t >= 0f && t <= duration ? t : -1f);
            ApplyLight(t);

            foreach (var cue in particles)
            {
                if (cue.system == null)
                    continue;
                if (t >= cue.time)
                {
                    cue.system.Simulate(t - cue.time, false, true, true);
                }
                else
                {
                    cue.system.Simulate(0f, false, true, true);
                    cue.system.Clear(false);
                }
            }
            foreach (var cue in subEffects)
            {
                if (cue.timeline != null)
                    cue.timeline.Preview(t >= cue.time ? t - cue.time : -1f);
            }
        }

        void ApplyLight(float t)
        {
            if (flashLight == null)
                return;
            float intensity = t >= 0f && t <= duration ? lightIntensity.Evaluate(t) : 0f;
            flashLight.intensity = intensity;
            flashLight.enabled = intensity > 0.001f;
        }

        /// <summary>Anima las partes que no son partículas. t &lt; 0 = reposo.</summary>
        protected virtual void Evaluate(float t) { }

        // Utilidad para las subclases.
        protected static void SetVisible(Renderer r, bool visible)
        {
            if (r != null && r.enabled != visible)
                r.enabled = visible;
        }
    }
}
