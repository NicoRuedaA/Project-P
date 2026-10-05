using System;
using System.Collections.Generic;
using EmeraldArena.Vfx;
using UnityEngine;

namespace BotwVfx
{
    /// <summary>
    /// Movimiento Emerald con estilo BotW: la animación horneada original
    /// (EmeraldVfxPlayer, ahora con el shader "BotwVFX/Emerald Toon") se sincroniza
    /// con la timeline BotW, que añade las capas Shuriken, las partes animadas a mano
    /// (auras, cuchillas, bandas, rayos), la luz, la sacudida y la cámara lenta.
    /// Marco local (el del módulo original): atacante en (-3,0,0), objetivo en (+3,0,0).
    /// En reposo (t &lt; 0) el reproductor queda desactivado y no dibuja nada.
    /// </summary>
    [DefaultExecutionOrder(-50)] // fija previewTime antes de que el reproductor dibuje en su Update
    public class EmeraldMoveVfx : VfxTimeline
    {
        /// <summary>
        /// Parte animada a mano (malla, línea, pivote). Todas las curvas usan el tiempo
        /// absoluto de la timeline, así que la receta escribe los instantes tal cual.
        /// Fuera de [start, end] el renderer (si lo hay) se oculta.
        /// </summary>
        [Serializable]
        public class PartTrack
        {
            public Transform part;
            public Renderer renderer;
            public float start;
            public float end = 1f;

            [Tooltip("k(t): posición = lerp(posFrom, posTo, k) + posArc·4k(1-k). Vacía = no se mueve.")]
            public AnimationCurve posCurve;
            public Vector3 posFrom, posTo, posArc;

            [Tooltip("k(t): escala = lerp(scaleFrom, scaleTo, k). Vacía = no escala.")]
            public AnimationCurve scaleCurve;
            public Vector3 scaleFrom = Vector3.one, scaleTo = Vector3.one;

            [Tooltip("k(t): giro = lerp(eulerFrom, eulerTo, k) + spin·(t - start).")]
            public AnimationCurve rotCurve;
            public Vector3 eulerFrom, eulerTo;
            [Tooltip("Grados por segundo (local) desde start.")]
            public Vector3 spin;

            [Tooltip("Propiedad float del material (vía MaterialPropertyBlock), p. ej. _Erosion, _Dissolve, _Opacity.")]
            public string property;
            public AnimationCurve propertyCurve;

            [NonSerialized] MaterialPropertyBlock block;
            [NonSerialized] int propertyId = -1;

            static bool Has(AnimationCurve c) => c != null && c.length > 0;

            public void Evaluate(float t)
            {
                bool on = t >= 0f && t >= start && t <= end;
                if (renderer != null && renderer.enabled != on)
                    renderer.enabled = on;
                if (!on || part == null)
                    return;

                if (Has(posCurve))
                {
                    float k = posCurve.Evaluate(t);
                    part.localPosition = Vector3.LerpUnclamped(posFrom, posTo, k) + posArc * (4f * k * (1f - k));
                }
                if (Has(scaleCurve))
                    part.localScale = Vector3.LerpUnclamped(scaleFrom, scaleTo, scaleCurve.Evaluate(t));
                if (Has(rotCurve) || spin != Vector3.zero)
                {
                    var euler = Has(rotCurve) ? Vector3.LerpUnclamped(eulerFrom, eulerTo, rotCurve.Evaluate(t)) : eulerFrom;
                    part.localRotation = Quaternion.Euler(euler + spin * (t - start));
                }
                if (renderer != null && !string.IsNullOrEmpty(property) && Has(propertyCurve))
                {
                    block ??= new MaterialPropertyBlock();
                    if (propertyId < 0)
                        propertyId = Shader.PropertyToID(property);
                    renderer.GetPropertyBlock(block);
                    block.SetFloat(propertyId, propertyCurve.Evaluate(t));
                    renderer.SetPropertyBlock(block);
                }
            }
        }

        [Header("Movimiento")]
        public int moveId;
        public string moveName;
        [Tooltip("Tipo tal como aparece en catalogue.json (p. ej. \"Fuego\").")]
        public string moveType;
        public string family;
        [TextArea] public string description;
        [Tooltip("True si el movimiento tiene receta propia (EmeraldRecipes); false = capas genéricas por tipo.")]
        public bool hasRecipe;

        [Header("Animación horneada")]
        public EmeraldVfxPlayer player;
        [Tooltip("Duración original de la animación horneada en segundos (121 muestras).")]
        [Min(0.05f)] public float animDuration = 2f;
        [Tooltip("Instante de la timeline en el que empieza el clip horneado.")]
        public float bakedOffset;
        [Tooltip("Tiempo que tarda el clip en reproducirse dentro de la timeline (animDuration = velocidad original).")]
        [Min(0.05f)] public float bakedLength = 2f;
        [Tooltip("False = el clip horneado no se dibuja nunca (la receta lo sustituye entero).")]
        public bool bakedVisible = true;
        [Tooltip("Escala local del clip (el pivote es el punto medio entre atacante y objetivo).")]
        public Vector3 bakedScale = Vector3.one;

        [Header("Impacto")]
        [Tooltip("Instante del impacto en segundos.")]
        public float impactTime = 1.2f;
        [Range(0f, 1f)] public float impactStrength;
        [Tooltip("Punto local donde se lanzan las capas del impacto.")]
        public Vector3 impactPoint = new Vector3(3f, 0f, 0f);

        [Header("Instantes clave (capturas y comparativas)")]
        public float anticipationTime = 0.6f;
        public float peakTime = 1.35f;
        public float dissipationTime = 1.8f;

        [Header("Partes animadas")]
        public List<PartTrack> tracks = new List<PartTrack>();

        [Header("Encuadre")]
        [Tooltip("Caja XY local aproximada del efecto (la usa la galería para alejar la cámara).")]
        public Vector2 viewMin = new Vector2(-3f, 0f);
        public Vector2 viewMax = new Vector2(3f, 2f);

        public event Action<float> Evaluated;

        public string Title => $"{moveId:000} {moveName} — {moveType}";

        /// <summary>True si en el instante t de la timeline el clip horneado debe dibujarse.</summary>
        public bool BakedActiveAt(float t)
        {
            float local = t - bakedOffset;
            return bakedVisible && player != null && t >= 0f && t <= duration && local >= 0f && local <= bakedLength;
        }

        protected override void Awake()
        {
            if (player != null)
            {
                player.playOnEnable = false;
                player.loop = false;
                player.duration = bakedLength;
                player.transform.localScale = bakedScale;
            }
            base.Awake();
        }

        protected override void Evaluate(float t)
        {
            if (player != null)
            {
                bool active = BakedActiveAt(t);
                if (player.enabled != active)
                    player.enabled = active;
                if (active)
                {
                    player.previewTime = Mathf.Clamp01((t - bakedOffset) / bakedLength);
                    if (player.transform.localScale != bakedScale)
                        player.transform.localScale = bakedScale;
                }
            }
            for (int i = 0; i < tracks.Count; i++)
                tracks[i]?.Evaluate(t >= 0f && t <= duration ? t : -1f);
            Evaluated?.Invoke(t >= 0f && t <= duration ? t : -1f);
        }

        /// <summary>
        /// Encola ahora los dibujos de la animación horneada para una cámara concreta.
        /// En Play no hace falta (el reproductor dibuja en su Update); lo usan las capturas
        /// del editor tras Preview(t), donde no hay bucle de juego entre medias.
        /// </summary>
        public void DrawBakedNow(Camera cam)
        {
            if (player != null && player.enabled)
                player.RenderAt(player.previewTime, cam);
        }
    }
}
