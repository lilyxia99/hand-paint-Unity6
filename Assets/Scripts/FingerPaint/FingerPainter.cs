using System.Collections.Generic;
using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Spawns small spheres at active fingertip positions, throttled by minimum distance.
    /// Maintains per-finger point lists for later mesh export.
    /// </summary>
    public class FingerPainter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private HandTrackingManager _handTracking;

        [Header("Painting Settings")]
        [SerializeField] private float _minDistance = 0.005f; // 5mm between points
        [SerializeField] private float _sphereRadius = 0.004f; // 4mm sphere radius
        [SerializeField] private int _sphereSegments = 6; // Low-poly sphere for performance

        [Header("Finger Colors")]
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

        /// <summary>Per-finger list of spawned GameObjects (spheres).</summary>
        public List<GameObject>[] PointsByFinger { get; private set; }

        private Vector3[] _lastSpawnPos;
        private Material[] _fingerMaterials;
        private Mesh _sharedSphereMesh;

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
                if (!finger.IsTracked || !finger.IsExtended)
                    continue;

                float dist = Vector3.Distance(finger.TipPosition, _lastSpawnPos[i]);
                if (dist < _minDistance)
                    continue;

                SpawnPoint(i, finger.TipPosition);
                _lastSpawnPos[i] = finger.TipPosition;
            }
        }

        private void SpawnPoint(int fingerIndex, Vector3 worldPos)
        {
            var go = new GameObject($"Paint_{fingerIndex}_{PointsByFinger[fingerIndex].Count}");
            go.transform.SetParent(transform, false);
            go.transform.position = worldPos;
            go.transform.localScale = Vector3.one * (_sphereRadius * 2f);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _sharedSphereMesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _fingerMaterials[fingerIndex];

            PointsByFinger[fingerIndex].Add(go);
        }

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
                var mat = new Material(shader);
                mat.color = _fingerColors[i];
                // Make it emissive so dots are visible in dim VR scenes
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", _fingerColors[i] * 0.3f);
                _fingerMaterials[i] = mat;
            }
        }
    }
}
