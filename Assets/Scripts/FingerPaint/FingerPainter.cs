using System.Collections.Generic;
using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Spawns small spheres at active fingertip positions, throttled by minimum distance.
    /// Maintains per-finger point lists for later mesh export.
    /// Supports three trigger modes: finger extension, voice activation, or both combined.
    /// Sphere size can scale dynamically based on voice volume.
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
            Combined
        }

        [Header("References")]
        [SerializeField] private HandTrackingManager _handTracking;

        [Header("Trigger")]
        [SerializeField] private DrawTriggerMode _triggerMode = DrawTriggerMode.FingerExtension;
        [Tooltip("Required when Trigger Mode is Voice or Combined.")]
        [SerializeField] private VoiceDetector _voiceDetector;

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

        // ─── Runtime state ───────────────────────────────────────────────

        /// <summary>Per-finger list of spawned GameObjects (spheres).</summary>
        public List<GameObject>[] PointsByFinger { get; private set; }

        private Vector3[] _lastSpawnPos;
        private Material[] _fingerMaterials;
        private Mesh _sharedSphereMesh;

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

            BuildSharedMesh();
            BuildMaterials();
        }

        private void Update()
        {
            if (_handTracking == null)
                return;

            for (int i = 0; i < HandTrackingManager.FingerCount; i++)
            {
                ref var finger = ref _handTracking.Fingers[i];

                // Finger must always be tracked (we need its position)
                if (!finger.IsTracked)
                    continue;

                // Check activation based on the selected trigger mode
                if (!ShouldDraw(ref finger))
                    continue;

                float dist = Vector3.Distance(finger.TipPosition, _lastSpawnPos[i]);
                if (dist < _minDistance)
                    continue;

                SpawnPoint(i, finger.TipPosition);
                _lastSpawnPos[i] = finger.TipPosition;
            }
        }

        private void OnValidate()
        {
            if (_triggerMode != DrawTriggerMode.FingerExtension && _voiceDetector == null)
            {
                Debug.LogWarning(
                    "[FingerPainter] VoiceDetector reference is required " +
                    "when Trigger Mode is Voice or Combined.");
            }
        }

        // ─── Trigger logic ───────────────────────────────────────────────

        private bool ShouldDraw(ref HandTrackingManager.FingerState finger)
        {
            switch (_triggerMode)
            {
                case DrawTriggerMode.FingerExtension:
                    return finger.IsExtended;

                case DrawTriggerMode.Voice:
                    // Voice alone controls activation; all tracked fingers paint
                    return _voiceDetector != null && _voiceDetector.IsActive;

                case DrawTriggerMode.Combined:
                    // Finger must be extended AND voice must be active
                    return finger.IsExtended
                        && _voiceDetector != null
                        && _voiceDetector.IsActive;

                default:
                    return finger.IsExtended;
            }
        }

        // ─── Spawning ────────────────────────────────────────────────────

        private void SpawnPoint(int fingerIndex, Vector3 worldPos)
        {
            var go = new GameObject($"Paint_{fingerIndex}_{PointsByFinger[fingerIndex].Count}");
            go.transform.SetParent(transform, false);
            go.transform.position = worldPos;

            // Base size, optionally scaled by voice volume
            float sizeMultiplier = 1f;
            if (_voiceDetector != null
                && (_triggerMode == DrawTriggerMode.Voice
                    || _triggerMode == DrawTriggerMode.Combined))
            {
                sizeMultiplier = _voiceDetector.GetSizeMultiplier();
            }

            go.transform.localScale = Vector3.one * (_sphereRadius * 2f * sizeMultiplier);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _sharedSphereMesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _fingerMaterials[fingerIndex];

            PointsByFinger[fingerIndex].Add(go);
        }

        // ─── Queries ─────────────────────────────────────────────────────

        /// <summary>Returns a flat list of all spawned point GameObjects across all fingers.</summary>
        public List<GameObject> GetAllPoints()
        {
            var all = new List<GameObject>();
            for (int i = 0; i < HandTrackingManager.FingerCount; i++)
                all.AddRange(PointsByFinger[i]);
            return all;
        }

        /// <summary>Total number of spawned points.</summary>
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

        /// <summary>Clears all painted points.</summary>
        public void ClearAll()
        {
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
        }

        // ─── Mesh & material setup ───────────────────────────────────────

        private void BuildSharedMesh()
        {
            // Generate a low-poly icosphere-ish UV sphere for paint dots
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
