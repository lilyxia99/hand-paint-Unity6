using TMPro;
using UnityEngine;
using System.Collections.Generic;

namespace FingerPaint
{
    /// <summary>
    /// Immersive gallery: when toggled ON, loads all saved works as 3D meshes
    /// and places them in a ring around the player's current position.
    /// Uses TextMeshPro for hint and label text with optional SDF glow.
    /// </summary>
    public class GalleryUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GalleryManager _galleryManager;

        [Header("Placement")]
        [Tooltip("Radius of the ring where objects are placed around the player (meters).")]
        [SerializeField] private float _ringRadius = 1.0f;

        [Tooltip("Height of the objects relative to player's eye level (meters).")]
        [SerializeField] private float _heightOffset = -0.3f;

        [Tooltip("Scale applied to each gallery object.")]
        [SerializeField] private float _objectScale = 0.15f;

        [Tooltip("Slow rotation speed for gallery objects (degrees/sec).")]
        [SerializeField] private float _rotationSpeed = 20f;

        [Header("Hint UI")]
        [SerializeField] private float _hintDistance = 0.6f;
        [SerializeField] private float _hintVerticalOffset = -0.15f;

        [Header("Text")]
        [Tooltip("Optional TMP font asset. Leave empty for the default TMP font.")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("Glow (TMP Shader)")]
        [Tooltip("Enable the TMP SDF shader glow effect.")]
        [SerializeField] private bool _enableGlow = true;

        [Tooltip("Drag TMP_SDF.shader here (Assets/TextMesh Pro/Shaders/TMP_SDF.shader).")]
        [SerializeField] private Shader _sdfGlowShader;

        [SerializeField] private Color _glowColor = new Color(0.5f, 0.8f, 1f, 0.5f);
        [SerializeField] [Range(-1f, 1f)] private float _glowOffset = 0f;
        [SerializeField] [Range(0f, 1f)] private float _glowInner = 0.15f;
        [SerializeField] [Range(0f, 1f)] private float _glowOuter = 0.35f;
        [SerializeField] [Range(0f, 1f)] private float _glowPower = 0.6f;

        // ─── State ──────────────────────────────────────────────────────

        private bool _isVisible;
        private Camera _cam;

        // Spawned gallery objects
        private readonly List<GameObject> _spawnedObjects = new List<GameObject>();
        private readonly List<Mesh> _loadedMeshes = new List<Mesh>();

        // Hint UI elements
        private Transform _hintRoot;
        private TextMeshPro _hintTMP;
        private bool _hintBuilt;

        // Label elements (per-object)
        private readonly List<GameObject> _labelObjects = new List<GameObject>();

        // ─── Public API ─────────────────────────────────────────────────

        public bool IsVisible => _isVisible;

        public void Show()
        {
            _cam = Camera.main;
            if (_cam == null) return;

            if (_galleryManager != null)
                _galleryManager.LoadManifest();

            int count = _galleryManager != null ? _galleryManager.WorkCount : 0;
            if (count == 0)
            {
                Debug.Log("[GalleryUI] No saved works to display.");
                ShowHint("No saved works yet");
                return;
            }

            _isVisible = true;

            // Spawn objects in a ring around the player
            Vector3 playerPos = _cam.transform.position;
            playerPos.y += _heightOffset;

            float angleStep = 360f / count;

            for (int i = 0; i < count; i++)
            {
                var entry = _galleryManager.GetEntry(i);
                if (entry == null) continue;

                Mesh mesh = _galleryManager.LoadObjMesh(entry.filename);
                if (mesh == null) continue;

                _loadedMeshes.Add(mesh);

                // Position in a ring
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(
                    Mathf.Sin(angle) * _ringRadius,
                    0f,
                    Mathf.Cos(angle) * _ringRadius);

                Vector3 worldPos = playerPos + offset;

                // Create the gallery object
                var go = new GameObject($"GalleryWork_{i}");
                go.transform.position = worldPos;

                // Auto-scale to target size
                float meshSize = mesh.bounds.size.magnitude;
                float scale = meshSize > 0.001f ? _objectScale / meshSize : 1f;
                go.transform.localScale = Vector3.one * scale;

                // Center mesh on its bounds
                Vector3 boundsCenter = mesh.bounds.center * scale;
                go.transform.position = worldPos - boundsCenter;

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;

                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = CreateGalleryMat(i, count);

                // Face the player
                Vector3 lookDir = playerPos - worldPos;
                lookDir.y = 0f;
                if (lookDir.sqrMagnitude > 0.001f)
                    go.transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);

                _spawnedObjects.Add(go);

                // Add a floating label below each object
                CreateLabel(entry, i, worldPos);
            }

            ShowHint("Look at left palm + pinch to exit gallery");
            Debug.Log($"[GalleryUI] Showing {_spawnedObjects.Count} gallery works.");
        }

        public void Hide()
        {
            _isVisible = false;

            foreach (var go in _spawnedObjects)
            {
                if (go != null) Destroy(go);
            }
            _spawnedObjects.Clear();

            foreach (var go in _labelObjects)
            {
                if (go != null) Destroy(go);
            }
            _labelObjects.Clear();

            foreach (var mesh in _loadedMeshes)
            {
                if (mesh != null) Destroy(mesh);
            }
            _loadedMeshes.Clear();

            HideHint();
            Debug.Log("[GalleryUI] Gallery hidden, all objects removed.");
        }

        public void Toggle()
        {
            if (_isVisible) Hide();
            else Show();
        }

        // ─── Lifecycle ──────────────────────────────────────────────────

        private void Start()
        {
            _cam = Camera.main;
        }

        private void LateUpdate()
        {
            if (!_isVisible) return;

            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            // Slowly rotate gallery objects
            foreach (var go in _spawnedObjects)
            {
                if (go != null)
                    go.transform.Rotate(Vector3.up, _rotationSpeed * Time.deltaTime, Space.World);
            }

            UpdateHintFollow();
        }

        // ─── Hint UI ────────────────────────────────────────────────────

        private void ShowHint(string message)
        {
            if (!_hintBuilt)
                BuildHint();

            _hintTMP.text = message;
            _hintRoot.gameObject.SetActive(true);

            if (_cam != null)
                SnapHintToCamera();
        }

        private void HideHint()
        {
            if (_hintRoot != null)
                _hintRoot.gameObject.SetActive(false);
        }

        private void UpdateHintFollow()
        {
            if (_hintRoot == null || !_hintRoot.gameObject.activeSelf || _cam == null)
                return;

            var camT = _cam.transform;
            Vector3 forward = camT.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
                forward = camT.forward;
            forward.Normalize();

            Vector3 target = camT.position
                + forward * _hintDistance
                + Vector3.up * _hintVerticalOffset;

            _hintRoot.position = Vector3.Lerp(_hintRoot.position, target, Time.deltaTime * 5f);
            _hintRoot.rotation = Quaternion.LookRotation(
                _hintRoot.position - camT.position, Vector3.up);
        }

        private void SnapHintToCamera()
        {
            var camT = _cam.transform;
            Vector3 forward = camT.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
                forward = camT.forward;
            forward.Normalize();

            _hintRoot.position = camT.position
                + forward * _hintDistance
                + Vector3.up * _hintVerticalOffset;
            _hintRoot.rotation = Quaternion.LookRotation(
                _hintRoot.position - camT.position, Vector3.up);
        }

        private void BuildHint()
        {
            _hintRoot = new GameObject("GalleryHint").transform;
            _hintRoot.SetParent(transform, false);

            Color hintColor = new Color(0.8f, 0.9f, 1f);

            var cfg = TMPTextFactory.Config.Default;
            cfg.Name = "HintText";
            cfg.Parent = _hintRoot;
            cfg.FontSize = 36f;
            cfg.Color = hintColor;
            cfg.LocalScale = 0.005f;
            cfg.RectSize = new Vector2(600f, 100f);
            cfg.Font = _font;
            cfg.GlowShader = _sdfGlowShader;
            cfg.EnableGlow = _enableGlow;
            cfg.Glow = GetGlowSettings();
            cfg.Glow.Color = new Color(hintColor.r, hintColor.g, hintColor.b, 0.5f);

            var result = TMPTextFactory.Create(cfg);
            _hintTMP = result.TMP;

            _hintRoot.gameObject.SetActive(false);
            _hintBuilt = true;
        }

        // ─── Per-object labels ──────────────────────────────────────────

        private void CreateLabel(GalleryEntry entry, int index, Vector3 objPos)
        {
            var labelGO = new GameObject($"GalleryLabel_{index}");
            labelGO.transform.position = objPos + Vector3.down * (_objectScale * 0.7f);

            // Face the camera
            if (_cam != null)
            {
                Vector3 lookDir = labelGO.transform.position - _cam.transform.position;
                lookDir.y = 0f;
                if (lookDir.sqrMagnitude > 0.001f)
                    labelGO.transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            }

            var cfg = TMPTextFactory.Config.Default;
            cfg.Name = "LabelText";
            cfg.Parent = labelGO.transform;
            cfg.FontSize = 28f;
            cfg.Color = new Color(0.7f, 0.8f, 1f);
            cfg.Alignment = TextAlignmentOptions.Top;
            cfg.LocalScale = 0.004f;
            cfg.RectSize = new Vector2(300f, 80f);
            cfg.Font = _font;
            cfg.GlowShader = _sdfGlowShader;
            cfg.EnableGlow = _enableGlow;
            cfg.Glow = GetGlowSettings();

            var result = TMPTextFactory.Create(cfg);

            // Format: date + point count
            string display;
            if (System.DateTime.TryParse(entry.timestamp, out System.DateTime dt))
                display = dt.ToString("MM/dd HH:mm");
            else
                display = entry.id;

            result.TMP.text = $"{display}\n{entry.pointCount} pts";

            _labelObjects.Add(labelGO);
        }

        // ─── Cleanup ────────────────────────────────────────────────────

        private void OnDestroy()
        {
            Hide();
        }

        // ─── Material helpers ───────────────────────────────────────────

        private static Material CreateGalleryMat(int index, int total)
        {
            float hue = (float)index / Mathf.Max(1, total);
            Color color = Color.HSVToRGB(hue, 0.5f, 0.9f);

            var shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = color;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 0.2f);
            return mat;
        }

        // ─── Glow helper ───────────────────────────────────────────────

        private TMPTextFactory.GlowSettings GetGlowSettings()
        {
            return new TMPTextFactory.GlowSettings
            {
                Color = _glowColor,
                Offset = _glowOffset,
                Inner = _glowInner,
                Outer = _glowOuter,
                Power = _glowPower,
            };
        }
    }
}
