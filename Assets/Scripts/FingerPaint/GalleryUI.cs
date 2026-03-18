using UnityEngine;
using System.Collections.Generic;

namespace FingerPaint
{
    /// <summary>
    /// Immersive gallery: when toggled ON, loads all saved works as 3D meshes
    /// and places them in a ring around the player's current position (stationary).
    /// A small hint UI follows the camera telling the user how to exit.
    /// When toggled OFF (same gesture), all gallery objects are destroyed.
    /// </summary>
    public class GalleryUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GalleryManager _galleryManager;

        [Header("Placement")]
        [Tooltip("Radius of the ring where objects are placed around the player (meters).")]
        [SerializeField] private float _ringRadius = 2.0f;

        [Tooltip("Height of the objects relative to player's eye level (meters).")]
        [SerializeField] private float _heightOffset = -0.3f;

        [Tooltip("Scale applied to each gallery object.")]
        [SerializeField] private float _objectScale = 0.15f;

        [Tooltip("Slow rotation speed for gallery objects (degrees/sec).")]
        [SerializeField] private float _rotationSpeed = 20f;

        [Header("Hint UI")]
        [SerializeField] private float _hintDistance = 0.6f;
        [SerializeField] private float _hintVerticalOffset = -0.15f;

        // ─── State ──────────────────────────────────────────────────────

        private bool _isVisible;
        private Camera _cam;

        // Spawned gallery objects
        private readonly List<GameObject> _spawnedObjects = new List<GameObject>();
        private readonly List<Mesh> _loadedMeshes = new List<Mesh>();

        // Hint UI elements
        private Transform _hintRoot;
        private TextMesh _hintText;
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
                // Show a brief "no works" hint
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
                CreateLabel(go.transform, entry, i, worldPos);
            }

            ShowHint("Look at left palm + pinch to exit gallery");
            Debug.Log($"[GalleryUI] Showing {_spawnedObjects.Count} gallery works.");
        }

        public void Hide()
        {
            _isVisible = false;

            // Destroy spawned objects
            foreach (var go in _spawnedObjects)
            {
                if (go != null) Destroy(go);
            }
            _spawnedObjects.Clear();

            // Destroy labels
            foreach (var go in _labelObjects)
            {
                if (go != null) Destroy(go);
            }
            _labelObjects.Clear();

            // Cleanup loaded meshes
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

            // Update hint UI to follow the camera
            UpdateHintFollow();
        }

        // ─── Hint UI ────────────────────────────────────────────────────

        private void ShowHint(string message)
        {
            if (!_hintBuilt)
                BuildHint();

            _hintText.text = message;
            _hintRoot.gameObject.SetActive(true);

            // Snap to position
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

            // Background
            var bgGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bgGO.name = "HintBG";
            bgGO.transform.SetParent(_hintRoot, false);
            bgGO.transform.localScale = new Vector3(0.35f, 0.05f, 1f);
            bgGO.transform.localPosition = Vector3.zero;

            var col = bgGO.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var bgMat = CreateUnlitMat(new Color(0.05f, 0.05f, 0.1f, 0.85f));
            bgMat.renderQueue = 3000;
            bgGO.GetComponent<MeshRenderer>().sharedMaterial = bgMat;

            // Text
            var textGO = new GameObject("HintText");
            textGO.transform.SetParent(_hintRoot, false);

            _hintText = textGO.AddComponent<TextMesh>();
            _hintText.fontSize = 36;
            _hintText.characterSize = 0.005f;
            _hintText.anchor = TextAnchor.MiddleCenter;
            _hintText.alignment = TextAlignment.Center;
            _hintText.color = new Color(0.8f, 0.9f, 1f);
            _hintText.text = "";

            textGO.transform.localPosition = new Vector3(0f, 0f, -0.002f);

            _hintRoot.gameObject.SetActive(false);
            _hintBuilt = true;
        }

        // ─── Per-object labels ──────────────────────────────────────────

        private void CreateLabel(Transform parent, GalleryEntry entry, int index, Vector3 objPos)
        {
            var labelGO = new GameObject($"GalleryLabel_{index}");
            // Position below the object, independent (not parented, stays stationary)
            labelGO.transform.position = objPos + Vector3.down * (_objectScale * 0.7f);

            // Face the camera
            if (_cam != null)
            {
                Vector3 lookDir = labelGO.transform.position - _cam.transform.position;
                lookDir.y = 0f;
                if (lookDir.sqrMagnitude > 0.001f)
                    labelGO.transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            }

            var tm = labelGO.AddComponent<TextMesh>();
            tm.fontSize = 28;
            tm.characterSize = 0.004f;
            tm.anchor = TextAnchor.UpperCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.7f, 0.8f, 1f);

            // Format: date + point count
            string display;
            if (System.DateTime.TryParse(entry.timestamp, out System.DateTime dt))
                display = dt.ToString("MM/dd HH:mm");
            else
                display = entry.id;

            tm.text = $"{display}\n{entry.pointCount} pts";

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
            // Give each object a unique hue from a gradient
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

        private static Material CreateUnlitMat(Color color)
        {
            var shader = Shader.Find("Unlit/Color")
                      ?? Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader);
            mat.color = color;
            return mat;
        }
    }
}
