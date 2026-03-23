using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FingerPaint
{
    /// <summary>
    /// Spawns glowing firefly-like particles that drift around the player.
    /// Particles stay outside an inner radius (player's personal space) and
    /// within an outer radius. The emitter follows the camera each frame.
    /// Everything is created at runtime — no prefabs or materials needed.
    /// Generates its own soft circular texture and ensures bloom is active.
    /// </summary>
    public class AmbientFireflies : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Particles won't spawn closer than this to the camera")]
        [SerializeField] private float _innerRadius = 1.5f;

        [Tooltip("Maximum distance from camera for particle spawning")]
        [SerializeField] private float _outerRadius = 5f;

        [Header("Emission")]
        [Tooltip("Number of fireflies alive at once")]
        [SerializeField] private int _maxParticles = 60;

        [Tooltip("Particles emitted per second")]
        [SerializeField] private float _emissionRate = 8f;

        [Header("Movement")]
        [Tooltip("Base drift speed of each firefly")]
        [SerializeField] private float _driftSpeed = 0.15f;

        [Tooltip("Vertical bobbing amplitude")]
        [SerializeField] private float _bobAmplitude = 0.3f;

        [Header("Appearance")]
        [Tooltip("Base color of the fireflies (HDR for bloom)")]
        [SerializeField] [ColorUsage(true, true)]
        private Color _colorA = new Color(0.8f, 0.95f, 1f, 1f) * 5f;

        [Tooltip("Secondary color to lerp towards")]
        [SerializeField] [ColorUsage(true, true)]
        private Color _colorB = new Color(1f, 0.7f, 0.3f, 1f) * 5f;

        [Tooltip("Size range (min, max)")]
        [SerializeField] private Vector2 _sizeRange = new Vector2(0.015f, 0.04f);

        [Tooltip("Lifetime range in seconds (min, max)")]
        [SerializeField] private Vector2 _lifetimeRange = new Vector2(4f, 10f);

        [Header("Bloom")]
        [Tooltip("Automatically ensure bloom is enabled in the scene volume")]
        [SerializeField] private bool _ensureBloom = true;

        [Tooltip("Bloom intensity to set if auto-enabling")]
        [SerializeField] private float _bloomIntensity = 1f;

        [Tooltip("Bloom threshold (lower = more things glow)")]
        [SerializeField] private float _bloomThreshold = 0.8f;

        [Header("Pulsing")]
        [Tooltip("Fireflies pulse (fade in/out) over their lifetime")]
        [SerializeField] private float _pulseSpeed = 1.5f;

        private ParticleSystem _ps;
        private ParticleSystemRenderer _psr;
        private Material _particleMat;
        private Texture2D _softCircleTex;
        private Transform _cameraTransform;

        private void Start()
        {
            _cameraTransform = Camera.main != null ? Camera.main.transform : null;
            if (_cameraTransform == null)
            {
                foreach (var cam in FindObjectsOfType<Camera>())
                {
                    if (cam.isActiveAndEnabled)
                    {
                        _cameraTransform = cam.transform;
                        break;
                    }
                }
            }

            if (_ensureBloom)
                EnsureBloom();

            CreateParticleSystem();
        }

        private void Update()
        {
            if (_cameraTransform != null)
            {
                transform.position = _cameraTransform.position;
            }
        }

        private void OnDestroy()
        {
            if (_particleMat != null) Destroy(_particleMat);
            if (_softCircleTex != null) Destroy(_softCircleTex);
        }

        /// <summary>
        /// Find or create a URP Volume with Bloom enabled so particles actually glow.
        /// </summary>
        private void EnsureBloom()
        {
            // Check all existing volumes for bloom
            foreach (var vol in FindObjectsOfType<Volume>())
            {
                if (vol.profile != null && vol.profile.TryGet<Bloom>(out var existingBloom))
                {
                    if (existingBloom.intensity.value < _bloomIntensity)
                    {
                        existingBloom.intensity.Override(_bloomIntensity);
                        existingBloom.threshold.Override(_bloomThreshold);
                        existingBloom.scatter.Override(0.7f);
                        Debug.Log($"[AmbientFireflies] Boosted existing bloom on \"{vol.name}\" " +
                                  $"to intensity={_bloomIntensity}, threshold={_bloomThreshold}");
                    }
                    return;
                }
            }

            // No volume with bloom found — create a global one
            var go = new GameObject("FireflyBloomVolume");
            go.transform.SetParent(transform, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>();
            bloom.intensity.Override(_bloomIntensity);
            bloom.threshold.Override(_bloomThreshold);
            bloom.scatter.Override(0.7f);
            volume.profile = profile;

            Debug.Log("[AmbientFireflies] Created bloom volume for firefly glow.");
        }

        private void CreateParticleSystem()
        {
            var go = new GameObject("FireflyParticles");
            go.transform.SetParent(transform, false);

            _ps = go.AddComponent<ParticleSystem>();
            _psr = go.GetComponent<ParticleSystemRenderer>();

            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // ── Main module ──────────────────────────────────────────────
            var main = _ps.main;
            main.maxParticles = _maxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(_lifetimeRange.x, _lifetimeRange.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(_driftSpeed * 0.5f, _driftSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(_sizeRange.x, _sizeRange.y);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;
            main.playOnAwake = false;
            main.gravityModifier = -0.01f;

            main.startColor = new ParticleSystem.MinMaxGradient(_colorA, _colorB);

            // ── Emission ─────────────────────────────────────────────────
            var emission = _ps.emission;
            emission.enabled = true;
            emission.rateOverTime = _emissionRate;

            // ── Shape: sphere shell with inner radius ────────────────────
            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = _outerRadius;
            shape.radiusThickness = 1f - (_innerRadius / _outerRadius);

            // ── Velocity over lifetime (gentle wandering) ────────────────
            var vel = _ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.orbitalX = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            vel.radial = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);

            // ── Noise (organic wandering movement) ───────────────────────
            var noise = _ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(_bobAmplitude);
            noise.frequency = _pulseSpeed * 0.3f;
            noise.scrollSpeed = 0.5f;
            noise.damping = true;
            noise.octaveCount = 2;

            // ── Size over lifetime (pulse in and out) ────────────────────
            var sol = _ps.sizeOverLifetime;
            sol.enabled = true;
            var sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 0f);
            sizeCurve.AddKey(0.15f, 1f);
            sizeCurve.AddKey(0.7f, 1f);
            sizeCurve.AddKey(1f, 0f);
            sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // ── Color over lifetime (alpha pulse for twinkling) ──────────
            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[] {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.8f, 0.1f),
                    new GradientAlphaKey(1f, 0.3f),
                    new GradientAlphaKey(0.4f, 0.6f),
                    new GradientAlphaKey(1f, 0.8f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            col.color = new ParticleSystem.MinMaxGradient(gradient);

            // ── Renderer ─────────────────────────────────────────────────
            _particleMat = CreateGlowMaterial();
            _psr.material = _particleMat;
            _psr.renderMode = ParticleSystemRenderMode.Billboard;
            _psr.alignment = ParticleSystemRenderSpace.Facing;
            _psr.sortMode = ParticleSystemSortMode.Distance;
            _psr.minParticleSize = 0f;
            _psr.maxParticleSize = 0.1f;

            // ── Go ───────────────────────────────────────────────────────
            _ps.Play();
        }

        /// <summary>
        /// Generates a soft radial gradient texture (circle fading to transparent)
        /// so particles look like glowing orbs instead of squares.
        /// </summary>
        private Texture2D GenerateSoftCircleTexture(int size = 64)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "FireflySoftCircle";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float center = size * 0.5f;
            float maxRadius = center;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center + 0.5f;
                    float dy = y - center + 0.5f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float normalizedDist = dist / maxRadius;

                    // Smooth falloff: bright center fading to 0 at edges
                    // Using a quadratic falloff for a softer look
                    float alpha = Mathf.Clamp01(1f - normalizedDist);
                    alpha = alpha * alpha; // quadratic falloff for softer edge

                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply(false, true); // make non-readable for performance
            return tex;
        }

        /// <summary>
        /// Creates an additive glow material with a soft circle texture, compatible with URP.
        /// </summary>
        private Material CreateGlowMaterial()
        {
            _softCircleTex = GenerateSoftCircleTexture(64);

            // Try URP particle shader first, fall back to built-in
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null)
                shader = Shader.Find("Mobile/Particles/Additive");

            var mat = new Material(shader);

            // Assign the soft circle texture
            mat.SetTexture("_BaseMap", _softCircleTex);
            mat.SetTexture("_MainTex", _softCircleTex);
            mat.mainTexture = _softCircleTex;

            // Set to additive blending for glow
            mat.SetFloat("_Surface", 1f);  // Transparent
            mat.SetFloat("_Blend", 2f);    // Additive

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_BLENDMODE_ADD");
            mat.DisableKeyword("_ALPHATEST_ON");

            // Additive blend: src * srcAlpha + dst * 1
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);

            mat.SetColor("_BaseColor", Color.white);
            mat.SetColor("_Color", Color.white);
            mat.renderQueue = 3000;

            return mat;
        }
    }
}
