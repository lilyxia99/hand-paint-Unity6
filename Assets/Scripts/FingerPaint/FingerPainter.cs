using System.Collections.Generic;
using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Spawns small spheres at active fingertip positions, throttled by minimum distance.
    /// Maintains per-finger point lists for later mesh export.
    /// Supports four trigger modes including DualMode: temporary balls when silent,
    /// permanent voice-reactive balls when speaking.
    /// </summary>
    public class FingerPainter : MonoBehaviour
    {
        // ─── Trigger mode ────────────────────────────────────────────────

        public enum DrawTriggerMode
        {
            /// <summary>Original: draw when finger is extended.</summary>
            FingerExtension,
            /// <summary>Draw when microphone detects voice. All tracked fingers paint.</summary>
            Voice,
            /// <summary>Both finger extension AND voice must be active to draw.</summary>
            Combined,
            /// <summary>Always spawns when extended. Temporary if silent, permanent if speaking.</summary>
            DualMode
        }

        [Header("References")]
        [SerializeField] private HandTrackingManager _handTracking;

        [Header("Trigger")]
        [SerializeField] private DrawTriggerMode _triggerMode = DrawTriggerMode.FingerExtension;
        [Tooltip("Required when Trigger Mode is Voice, Combined, or DualMode.")]
        [SerializeField] private VoiceDetector _voiceDetector;

        [Header("Voice Brush (Optional)")]
        [Tooltip("If set, voice spectral features drive per-spawn visual variation.")]
        [SerializeField] private VoiceBrushController _voiceBrush;

        [Tooltip("Procedural mesh library for voice-driven shape variation.")]
        [SerializeField] private BrushMeshLibrary _meshLibrary;

        [Header("Painting Settings")]
        [SerializeField] private float _minDistance = 0.005f; // 5mm between points
        [SerializeField] private float _sphereRadius = 0.004f; // 4mm sphere radius
        [SerializeField] private int _sphereSegments = 6; // Low-poly sphere for performance

        // ─── Material override ───────────────────────────────────────────

        [Header("Material Override")]
        [Tooltip("If set, this single material is used for ALL fingers (overrides per-finger and auto-generated).")]
        [SerializeField] private Material _sharedMaterialOverride;

        [Tooltip("Per-finger material overrides (array of 10). Null entries fall back to shared override or auto-generated color.")]
        [SerializeField] private Material[] _perFingerMaterialOverrides = new Material[HandTrackingManager.FingerCount];

        // ─── Auto-generated fallback colors ──────────────────────────────

        [Header("Finger Colors (fallback when no material override is set)")]
        [SerializeField] private Color[] _fingerColors = new Color[HandTrackingManager.FingerCount]
        {
            new Color(1f, 0.2f, 0.2f),   // L Thumb  - red
            new Color(1f, 0.5f, 0f),       // L Index  - orange
            new Color(1f, 1f, 0.2f),       // L Middle - yellow
            new Color(0.2f, 1f, 0.2f),     // L Ring   - green
            new Color(0.2f, 0.5f, 1f),     // L Little - blue
            new Color(1f, 0.2f, 0.2f),     // R Thumb  - red
            new Color(1f, 0.5f, 0f),       // R Index  - orange
            new Color(1f, 1f, 0.2f),       // R Middle - yellow
            new Color(0.2f, 1f, 0.2f),     // R Ring   - green
            new Color(0.2f, 0.5f, 1f),     // R Little - blue
        };

        // ─── Dual-mode temporary ball settings ──────────────────────────

        [Header("Dual-Mode (Temporary Balls)")]
        [Tooltip("Maximum number of temporary balls in the scene before oldest start fading.")]
        [SerializeField] private int _maxTemporaryBalls = 500;

        [Tooltip("How long a temporary ball takes to fade out once marked for removal (seconds).")]
        [SerializeField] private float _fadeOutDuration = 1.5f;

        [Tooltip("Base color for temporary (silent) balls.")]
        [SerializeField] private Color _tempBallColor = new Color(0.6f, 0.6f, 0.8f, 1f);

        [Tooltip("Opacity for temporary (silent) balls.")]
        [SerializeField] [Range(0f, 1f)] private float _tempBallOpacity = 0.25f;

        // ─── Idle trail fade settings ──────────────────────────────────

        [Header("Idle Trail Fade")]
        [Tooltip("Seconds of inactivity (no voice + hand still) before trailing balls fade.")]
        [SerializeField] private float _idleFadeDelay = 1.0f;

        [Tooltip("Number of trailing balls per finger that fade when idle.")]
        [SerializeField] private int _idleFadeCount = 5;

        [Tooltip("Duration of the trail fade shrink + disappear (seconds).")]
        [SerializeField] private float _trailFadeDuration = 1.5f;

        [Tooltip("Per-frame movement threshold below which the hand is 'still' (meters).")]
        [SerializeField] private float _stillThreshold = 0.003f;

        // ─── Runtime state ───────────────────────────────────────────────

        /// <summary>Per-finger list of spawned permanent GameObjects (spheres).</summary>
        public List<GameObject>[] PointsByFinger { get; private set; }

        private Vector3[] _lastSpawnPos;
        private Material[] _fingerMaterials;
        private Mesh _sharedSphereMesh;

        // ─── Idle trail fade state ───────────────────────────────────────

        private float[] _fingerIdleTime;
        private Vector3[] _prevFramePos;
        private bool[] _trailFadeTriggered;

        private struct TrailFadeEntry
        {
            public GameObject Go;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock PropBlock;
            public float FadeStartTime;
            public int FingerIndex;
            public Vector3 OriginalScale;
        }

        private List<TrailFadeEntry> _trailFading;

        // ─── MaterialPropertyBlock for voice brush per-spawn overrides ──
        private MaterialPropertyBlock _propBlock;

        private static readonly int _idBaseColor         = Shader.PropertyToID("_BaseColor");
        private static readonly int _idOpacity           = Shader.PropertyToID("_Opacity");
        private static readonly int _idEmissionColor     = Shader.PropertyToID("_EmissionColor");
        private static readonly int _idEmissionIntensity = Shader.PropertyToID("_EmissionIntensity");
        private static readonly int _idFresnelScale      = Shader.PropertyToID("_FresnelScale");

        // ─── Temporary ball ring buffer (DualMode) ──────────────────────

        private struct TemporaryBall
        {
            public GameObject Go;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock PropBlock;
            public float FadeStartTime; // -1 = not fading
            public float BaseOpacity;
        }

        private const int RingBufferCapacity = 600;
        private TemporaryBall[] _tempRing;
        private int _tempHead; // index of oldest entry
        private int _tempCount; // number of active entries
        private int _tempFadingCount; // how many are currently fading

        // ─── Lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            PointsByFinger = new List<GameObject>[HandTrackingManager.FingerCount];
            _lastSpawnPos = new Vector3[HandTrackingManager.FingerCount];

            for (int i = 0; i < HandTrackingManager.FingerCount; i++)
            {
                PointsByFinger[i] = new List<GameObject>();
                _lastSpawnPos[i] = Vector3.positiveInfinity;
            }

            _propBlock = new MaterialPropertyBlock();

            // Initialize ring buffer for temporary balls
            _tempRing = new TemporaryBall[RingBufferCapacity];
            _tempHead = 0;
            _tempCount = 0;
            _tempFadingCount = 0;

            // Initialize idle trail fade state
            _fingerIdleTime = new float[HandTrackingManager.FingerCount];
            _prevFramePos = new Vector3[HandTrackingManager.FingerCount];
            _trailFadeTriggered = new bool[HandTrackingManager.FingerCount];
            for (int i = 0; i < HandTrackingManager.FingerCount; i++)
                _prevFramePos[i] = Vector3.positiveInfinity;
            _trailFading = new List<TrailFadeEntry>();

            BuildSharedMesh();
            BuildMaterials();
        }

        private void Update()
        {
            if (_handTracking == null)
                return;

            if (_triggerMode == DrawTriggerMode.DualMode)
            {
                UpdateDualMode();
            }
            else
            {
                UpdateClassicMode();
            }
        }

        // ─── Classic mode (unchanged original behavior) ─────────────────

        private void UpdateClassicMode()
        {
            for (int i = 0; i < HandTrackingManager.FingerCount; i++)
            {
                ref var finger = ref _handTracking.Fingers[i];

                if (!finger.IsTracked)
                    continue;

                if (!ShouldDraw(ref finger))
                    continue;

                float dist = Vector3.Distance(finger.TipPosition, _lastSpawnPos[i]);
                if (dist < _minDistance)
                    continue;

                SpawnPoint(i, finger.TipPosition);
                _lastSpawnPos[i] = finger.TipPosition;
            }

            UpdateIdleTrailFade();
        }

        // ─── Dual mode: temporary when silent, permanent when speaking ──

        private void UpdateDualMode()
        {
            bool voiceActive = _voiceDetector != null && _voiceDetector.IsActive;

            for (int i = 0; i < HandTrackingManager.FingerCount; i++)
            {
                ref var finger = ref _handTracking.Fingers[i];

                if (!finger.IsTracked || !finger.IsExtended)
                    continue;

                float dist = Vector3.Distance(finger.TipPosition, _lastSpawnPos[i]);
                if (dist < _minDistance)
                    continue;

                if (voiceActive)
                    SpawnPoint(i, finger.TipPosition);       // permanent, voice-reactive
                else
                    SpawnTemporaryPoint(i, finger.TipPosition); // temporary, fades away

                _lastSpawnPos[i] = finger.TipPosition;
            }

            UpdateTemporaryBalls();
            UpdateIdleTrailFade();
        }

        private void OnValidate()
        {
            if (_triggerMode != DrawTriggerMode.FingerExtension && _voiceDetector == null)
            {
                Debug.LogWarning(
                    "[FingerPainter] VoiceDetector reference is required " +
                    "when Trigger Mode is Voice, Combined, or DualMode.");
            }
        }

        // ─── Trigger logic (classic modes only) ─────────────────────────

        private bool ShouldDraw(ref HandTrackingManager.FingerState finger)
        {
            switch (_triggerMode)
            {
                case DrawTriggerMode.FingerExtension:
                    return finger.IsExtended;

                case DrawTriggerMode.Voice:
                    return _voiceDetector != null && _voiceDetector.IsActive;

                case DrawTriggerMode.Combined:
                    return finger.IsExtended
                        && _voiceDetector != null
                        && _voiceDetector.IsActive;

                default:
                    return finger.IsExtended;
            }
        }

        // ─── Permanent spawning (existing logic) ────────────────────────

        private void SpawnPoint(int fingerIndex, Vector3 worldPos)
        {
            var go = new GameObject($"Paint_{fingerIndex}_{PointsByFinger[fingerIndex].Count}");
            go.transform.SetParent(transform, false);
            go.transform.position = worldPos;

            // ── Resolve size, mesh, and per-instance visuals ───────────
            bool useVoiceBrush = _voiceBrush != null && _voiceBrush.CurrentBrush.IsValid;
            BrushState brush = useVoiceBrush ? _voiceBrush.CurrentBrush : default;

            // Size: voice brush overrides VoiceDetector's simple volume scaling
            float sizeMultiplier;
            if (useVoiceBrush)
            {
                sizeMultiplier = brush.SizeMultiplier;
            }
            else if (_voiceDetector != null
                     && (_triggerMode == DrawTriggerMode.Voice
                         || _triggerMode == DrawTriggerMode.Combined
                         || _triggerMode == DrawTriggerMode.DualMode))
            {
                sizeMultiplier = _voiceDetector.GetSizeMultiplier();
            }
            else
            {
                sizeMultiplier = 1f;
            }

            go.transform.localScale = Vector3.one * (_sphereRadius * 2f * sizeMultiplier);

            // Mesh: voice brush can select from the mesh library
            var mf = go.AddComponent<MeshFilter>();
            if (useVoiceBrush && _meshLibrary != null && _meshLibrary.MeshCount > 0)
            {
                mf.sharedMesh = _meshLibrary.GetMesh(brush.MeshIndex);
            }
            else
            {
                mf.sharedMesh = _sharedSphereMesh;
            }

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _fingerMaterials[fingerIndex];

            // Per-instance MaterialPropertyBlock overrides (no new Material alloc)
            if (useVoiceBrush)
            {
                _propBlock.Clear();
                _propBlock.SetColor(_idBaseColor,         brush.BaseColor);
                _propBlock.SetFloat(_idOpacity,           brush.Opacity);
                _propBlock.SetColor(_idEmissionColor,     brush.EmissionColor);
                _propBlock.SetFloat(_idEmissionIntensity, brush.EmissionIntensity);
                _propBlock.SetFloat(_idFresnelScale,      brush.FresnelScale);
                mr.SetPropertyBlock(_propBlock);
            }

            PointsByFinger[fingerIndex].Add(go);
        }

        // ─── Temporary ball spawning (DualMode, silent) ─────────────────

        private void SpawnTemporaryPoint(int fingerIndex, Vector3 worldPos)
        {
            // If ring buffer is full, force-destroy the oldest entry
            if (_tempCount >= RingBufferCapacity)
            {
                ForceDestroyOldestTemporary();
            }

            var go = new GameObject($"Temp_{fingerIndex}_{_tempCount}");
            go.transform.SetParent(transform, false);
            go.transform.position = worldPos;
            go.transform.localScale = Vector3.one * (_sphereRadius * 2f);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _sharedSphereMesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _fingerMaterials[fingerIndex];

            // Set transparent appearance for temporary ball
            var propBlock = new MaterialPropertyBlock();
            propBlock.SetColor(_idBaseColor, _tempBallColor);
            propBlock.SetFloat(_idOpacity, _tempBallOpacity);
            propBlock.SetFloat(_idEmissionIntensity, 0f);
            propBlock.SetFloat(_idFresnelScale, 0.3f);
            mr.SetPropertyBlock(propBlock);

            // Add to ring buffer
            int idx = (_tempHead + _tempCount) % RingBufferCapacity;
            _tempRing[idx] = new TemporaryBall
            {
                Go = go,
                Renderer = mr,
                PropBlock = propBlock,
                FadeStartTime = -1f,
                BaseOpacity = _tempBallOpacity
            };
            _tempCount++;
        }

        // ─── Temporary ball lifecycle ───────────────────────────────────

        private void UpdateTemporaryBalls()
        {
            // Phase 1: Mark oldest non-fading balls for fade if count exceeds limit
            int activeCount = _tempCount - _tempFadingCount;
            int excess = activeCount - _maxTemporaryBalls;

            if (excess > 0)
            {
                int marked = 0;
                for (int i = 0; i < _tempCount && marked < excess; i++)
                {
                    int idx = (_tempHead + i) % RingBufferCapacity;
                    if (_tempRing[idx].FadeStartTime < 0f)
                    {
                        _tempRing[idx].FadeStartTime = Time.time;
                        _tempFadingCount++;
                        marked++;
                    }
                }
            }

            // Phase 2: Update fading balls and destroy completed ones
            while (_tempCount > 0)
            {
                ref var ball = ref _tempRing[_tempHead];

                // Skip non-fading balls at the front (can't dequeue past them)
                if (ball.FadeStartTime < 0f)
                    break;

                float elapsed = Time.time - ball.FadeStartTime;
                if (elapsed >= _fadeOutDuration)
                {
                    // Fully faded — destroy and dequeue
                    if (ball.Go != null)
                        Destroy(ball.Go);

                    ball = default;
                    _tempHead = (_tempHead + 1) % RingBufferCapacity;
                    _tempCount--;
                    _tempFadingCount--;
                }
                else
                {
                    // Still fading — update opacity
                    float t = elapsed / _fadeOutDuration;
                    float newOpacity = Mathf.Lerp(ball.BaseOpacity, 0f, t);

                    if (ball.Renderer != null)
                    {
                        ball.PropBlock.SetFloat(_idOpacity, newOpacity);
                        ball.Renderer.SetPropertyBlock(ball.PropBlock);
                    }
                    break; // Older balls are still fading, stop here
                }
            }

            // Phase 3: Also update opacity for any fading balls beyond the head
            // (in case multiple are fading simultaneously)
            for (int i = 1; i < _tempCount; i++)
            {
                int idx = (_tempHead + i) % RingBufferCapacity;
                ref var ball = ref _tempRing[idx];

                if (ball.FadeStartTime < 0f)
                    continue;

                float elapsed = Time.time - ball.FadeStartTime;
                if (elapsed >= _fadeOutDuration)
                {
                    // Can't dequeue from middle of ring buffer, just destroy GO
                    if (ball.Go != null)
                        Destroy(ball.Go);
                    ball.Go = null;
                    ball.Renderer = null;
                    // It will be cleaned up when it reaches the head
                }
                else
                {
                    float t = elapsed / _fadeOutDuration;
                    float newOpacity = Mathf.Lerp(ball.BaseOpacity, 0f, t);

                    if (ball.Renderer != null)
                    {
                        ball.PropBlock.SetFloat(_idOpacity, newOpacity);
                        ball.Renderer.SetPropertyBlock(ball.PropBlock);
                    }
                }
            }

            // Phase 4: Clean up destroyed entries at the head
            while (_tempCount > 0 && _tempRing[_tempHead].Go == null)
            {
                if (_tempRing[_tempHead].FadeStartTime >= 0f)
                    _tempFadingCount--;

                _tempRing[_tempHead] = default;
                _tempHead = (_tempHead + 1) % RingBufferCapacity;
                _tempCount--;
            }
        }

        private void ForceDestroyOldestTemporary()
        {
            if (_tempCount <= 0) return;

            ref var ball = ref _tempRing[_tempHead];
            if (ball.Go != null)
                Destroy(ball.Go);

            if (ball.FadeStartTime >= 0f)
                _tempFadingCount--;

            ball = default;
            _tempHead = (_tempHead + 1) % RingBufferCapacity;
            _tempCount--;
        }

        // ─── Idle trail fade ──────────────────────────────────────────────

        /// <summary>
        /// Per-finger idle detection: when voice is off and the hand is still
        /// for _idleFadeDelay seconds, start fading the last N balls.
        /// Uses shrink + opacity fade so it works with any material/shader.
        /// </summary>
        private void UpdateIdleTrailFade()
        {
            // Only relevant when voice is part of the trigger
            if (_voiceDetector == null)
            {
                UpdateTrailFadeBalls();
                return;
            }

            bool voiceActive = _voiceDetector.IsActive;

            for (int i = 0; i < HandTrackingManager.FingerCount; i++)
            {
                ref var finger = ref _handTracking.Fingers[i];

                if (!finger.IsTracked)
                {
                    _fingerIdleTime[i] = 0f;
                    _trailFadeTriggered[i] = false;
                    _prevFramePos[i] = Vector3.positiveInfinity;
                    continue;
                }

                // Detect per-frame hand movement
                float movement = (_prevFramePos[i].x < float.MaxValue)
                    ? Vector3.Distance(finger.TipPosition, _prevFramePos[i])
                    : 0f;
                _prevFramePos[i] = finger.TipPosition;

                bool isStill = movement < _stillThreshold;
                bool isIdle = !voiceActive && isStill;

                if (isIdle)
                {
                    _fingerIdleTime[i] += Time.deltaTime;

                    if (_fingerIdleTime[i] >= _idleFadeDelay && !_trailFadeTriggered[i])
                    {
                        StartTrailFadeForFinger(i);
                        _trailFadeTriggered[i] = true;
                    }
                }
                else
                {
                    _fingerIdleTime[i] = 0f;
                    _trailFadeTriggered[i] = false;
                }
            }

            UpdateTrailFadeBalls();
        }

        /// <summary>
        /// Marks the last N permanent balls for a given finger to begin trail-fading.
        /// </summary>
        private void StartTrailFadeForFinger(int fingerIndex)
        {
            var points = PointsByFinger[fingerIndex];
            int count = Mathf.Min(_idleFadeCount, points.Count);
            if (count <= 0) return;

            for (int j = points.Count - count; j < points.Count; j++)
            {
                var go = points[j];
                if (go == null) continue;

                // Skip if already in the fade list
                bool alreadyFading = false;
                for (int k = 0; k < _trailFading.Count; k++)
                {
                    if (_trailFading[k].Go == go)
                    {
                        alreadyFading = true;
                        break;
                    }
                }
                if (alreadyFading) continue;

                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer == null) continue;

                var propBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(propBlock);

                _trailFading.Add(new TrailFadeEntry
                {
                    Go = go,
                    Renderer = renderer,
                    PropBlock = propBlock,
                    FadeStartTime = Time.time,
                    FingerIndex = fingerIndex,
                    OriginalScale = go.transform.localScale
                });
            }
        }

        /// <summary>
        /// Updates all trail-fading balls: shrinks them toward zero and reduces
        /// opacity. When fully faded, destroys the GameObject and removes it
        /// from PointsByFinger.
        /// </summary>
        private void UpdateTrailFadeBalls()
        {
            for (int i = _trailFading.Count - 1; i >= 0; i--)
            {
                var entry = _trailFading[i];

                if (entry.Go == null)
                {
                    _trailFading.RemoveAt(i);
                    continue;
                }

                float elapsed = Time.time - entry.FadeStartTime;
                float t = Mathf.Clamp01(elapsed / _trailFadeDuration);

                if (t >= 1f)
                {
                    // Fully faded — destroy and remove from permanent list
                    PointsByFinger[entry.FingerIndex].Remove(entry.Go);
                    Destroy(entry.Go);
                    _trailFading.RemoveAt(i);
                }
                else
                {
                    // Shrink + reduce opacity
                    float fade = 1f - t;
                    entry.Go.transform.localScale = entry.OriginalScale * fade;

                    entry.PropBlock.SetFloat(_idOpacity, fade);
                    entry.Renderer.SetPropertyBlock(entry.PropBlock);
                }
            }
        }

        // ─── Queries ─────────────────────────────────────────────────────

        /// <summary>Returns a flat list of all spawned permanent point GameObjects across all fingers.</summary>
        public List<GameObject> GetAllPoints()
        {
            var all = new List<GameObject>();
            for (int i = 0; i < HandTrackingManager.FingerCount; i++)
                all.AddRange(PointsByFinger[i]);
            return all;
        }

        /// <summary>Total number of permanent spawned points.</summary>
        public int TotalPointCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < HandTrackingManager.FingerCount; i++)
                    count += PointsByFinger[i].Count;
                return count;
            }
        }

        /// <summary>Number of temporary balls currently in the scene.</summary>
        public int TemporaryBallCount => _tempCount;

        /// <summary>Clears all painted points (both permanent and temporary).</summary>
        public void ClearAll()
        {
            // Clear permanent balls
            for (int i = 0; i < HandTrackingManager.FingerCount; i++)
            {
                foreach (var go in PointsByFinger[i])
                {
                    if (go != null)
                        Destroy(go);
                }
                PointsByFinger[i].Clear();
                _lastSpawnPos[i] = Vector3.positiveInfinity;
            }

            // Clear trail fade entries
            if (_trailFading != null)
            {
                _trailFading.Clear();
                for (int i = 0; i < HandTrackingManager.FingerCount; i++)
                {
                    _fingerIdleTime[i] = 0f;
                    _trailFadeTriggered[i] = false;
                }
            }

            // Clear temporary balls
            if (_tempRing != null)
            {
                for (int i = 0; i < _tempCount; i++)
                {
                    int idx = (_tempHead + i) % RingBufferCapacity;
                    if (_tempRing[idx].Go != null)
                        Destroy(_tempRing[idx].Go);
                    _tempRing[idx] = default;
                }
                _tempHead = 0;
                _tempCount = 0;
                _tempFadingCount = 0;
            }
        }

        // ─── Mesh & material setup ───────────────────────────────────────

        private void BuildSharedMesh()
        {
            // Prefer the BrushMeshLibrary's low-poly sphere if available
            if (_meshLibrary != null && _meshLibrary.MeshCount > 0)
            {
                _sharedSphereMesh = _meshLibrary.GetMesh(BrushMeshLibrary.SPHERE);
                return;
            }

            // Fallback: use Unity's built-in sphere mesh
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sharedSphereMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);
        }

        private void BuildMaterials()
        {
            _fingerMaterials = new Material[HandTrackingManager.FingerCount];

            // Use URP Lit shader if available, otherwise fallback
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard");

            for (int i = 0; i < HandTrackingManager.FingerCount; i++)
            {
                // Priority: per-finger override → shared override → auto-generated
                if (_perFingerMaterialOverrides != null
                    && i < _perFingerMaterialOverrides.Length
                    && _perFingerMaterialOverrides[i] != null)
                {
                    _fingerMaterials[i] = _perFingerMaterialOverrides[i];
                }
                else if (_sharedMaterialOverride != null)
                {
                    _fingerMaterials[i] = _sharedMaterialOverride;
                }
                else
                {
                    // Auto-generate an emissive material from the finger color
                    var mat = new Material(shader);
                    mat.color = _fingerColors[i];
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", _fingerColors[i] * 0.3f);
                    _fingerMaterials[i] = mat;
                }
            }
        }
    }
}
