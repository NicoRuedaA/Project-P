using UnityEngine;

namespace BotwVfx
{
    /// <summary>
    /// Sacudida de cámara y cámara lenta compartidas por todos los efectos.
    /// La cámara lenta en el impacto (como hace el artículo de 80.lv al bloquear
    /// el rayo con el escudo) da muchísimo "peso" a los golpes.
    /// </summary>
    public class VfxDirector : MonoBehaviour
    {
        static VfxDirector instance;

        [Tooltip("Velocidad base del juego (la demo la cambia con el slider).")]
        [Range(0.05f, 2f)] public float baseTimeScale = 1f;
        public bool enableSlowMotion = true;
        [Range(0f, 2f)] public float shakeMultiplier = 1f;

        [Tooltip("Si no hay un controlador de cámara propio, aplica la sacudida directamente a Camera.main.")]
        public bool applyShakeToMainCamera;

        public Vector3 ShakeOffset { get; private set; }

        float shakeStrength;
        float shakeDuration;
        float shakeElapsed = float.MaxValue;
        float slowUntil;
        float slowScale = 1f;
        Vector3 appliedOffset;

        public static VfxDirector Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindAnyObjectByType<VfxDirector>();
                    if (instance == null && Application.isPlaying)
                        instance = new GameObject("VfxDirector").AddComponent<VfxDirector>();
                }
                return instance;
            }
        }

        public static void Shake(float strength, float duration)
        {
            var d = Instance;
            if (d == null)
                return;
            // Si ya hay una sacudida más fuerte en curso, no la pisamos.
            float remaining = d.shakeElapsed < d.shakeDuration ? d.shakeStrength * (1f - d.shakeElapsed / d.shakeDuration) : 0f;
            if (strength < remaining)
                return;
            d.shakeStrength = strength;
            d.shakeDuration = Mathf.Max(0.01f, duration);
            d.shakeElapsed = 0f;
        }

        public static void SlowMotion(float timeScale, float realSeconds)
        {
            var d = Instance;
            if (d == null || !d.enableSlowMotion)
                return;
            d.slowScale = timeScale;
            d.slowUntil = Time.unscaledTime + realSeconds;
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
                Time.timeScale = 1f;
            }
        }

        void Update()
        {
            float slow = Time.unscaledTime < slowUntil ? slowScale : 1f;
            Time.timeScale = baseTimeScale * slow;

            if (shakeElapsed < shakeDuration)
            {
                shakeElapsed += Time.unscaledDeltaTime;
                float k = 1f - Mathf.Clamp01(shakeElapsed / shakeDuration);
                k *= k;
                float t = Time.unscaledTime * 28f;
                var n = new Vector3(
                    Mathf.PerlinNoise(t, 0.31f) - 0.5f,
                    Mathf.PerlinNoise(0.73f, t) - 0.5f,
                    Mathf.PerlinNoise(t * 0.7f, 5.1f) - 0.5f);
                ShakeOffset = n * (2f * shakeStrength * k * shakeMultiplier);
            }
            else
            {
                ShakeOffset = Vector3.zero;
            }
        }

        void LateUpdate()
        {
            if (!applyShakeToMainCamera)
                return;
            var cam = Camera.main;
            if (cam == null)
                return;
            cam.transform.position += ShakeOffset - appliedOffset;
            appliedOffset = ShakeOffset;
        }
    }
}
