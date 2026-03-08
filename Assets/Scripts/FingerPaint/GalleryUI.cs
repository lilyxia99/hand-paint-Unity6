using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// World-space gallery panel for browsing and previewing saved finger-paint works.
    /// Uses Quad + TextMesh (no Canvas/UGUI). Follows head like VoiceDebugUI.
    /// Navigation via index-finger proximity + thumb-index pinch.
    /// </summary>
    public class GalleryUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GalleryManager _galleryManager;
        [SerializeField] private HandTrackingManager _handTracking;

        [Header("Positioning")]
        [SerializeField] private float _distance = 0.7f;
        [SerializeField] private float _verticalOffset = -0.05f;

        [Header("Panel")]
        [SerializeField] private float _panelWidth = 0.45f;
        [SerializeField] private float _panelHeight = 0.30f;
        [SerializeField] private int _visibleEntries = 5;

        [Header("Interaction")]
        [Tooltip("Distance in meters for thumb-index pinch to count as selection.")]
        [SerializeField] private float _pinchThreshold = 0.02f;

        // ─── State ──────────────────────────────────────────────────────

        private bool _isVisible;
        private int _scrollIndex;
        private int _selectedIndex = -1;
        private Camera _mainCam;

        // ─── UI Elements ────────────────────────────────────────────────

        private Transform _root;
        private TextMesh _titleText;
        private TextMesh[] _entryTexts;
        private Transform[] _entryBgs;
        private Material[] _entryBgMats;
        private TextMesh _infoText;

        // ─── Preview ────────────────────────────────────────────────────

        private Transform _previewRoot;
        private MeshFilter _previewMeshFilter;
        private MeshRenderer _previewRenderer;
        private Mesh _currentPreviewMesh;

        // ─── Colors ─────────────────────────────────────────────────────

        private static readonly Color ColorBg          = new Color(0.05f, 0.05f, 0.1f, 0.85f);
        private static readonly Color ColorEntryNormal = new Color(0.1f, 0.1f, 0.15f, 0.7f);
        private static readonly Color ColorEntryHover  = new Color(0.2f, 0.25f, 0.4f, 0.85f);
        private static readonly Color ColorEntrySelect = new Color(0.15f, 0.5f, 0.3f, 0.9f);
        private static readonly Color ColorTitle       = new Color(0.8f, 0.9f, 1f);
        private static readonly Color ColorTextNormal  = new Color(0.7f, 0.7f, 0.7f);
        private static readonly Color ColorTextSelect  = new Color(0.3f, 1f, 0.6f);

        // ─── Public API ─────────────────────────────────────────────────

        public bool IsVisible => _isVisible;

        public void Show()
        {
            if (_root == null)
                BuildPanel();

            _isVisible = true;
            _root.gameObject.SetActive(true);
            _scrollIndex = 0;
            _selectedIndex = -1;

            if (_galleryManager != null)
                _galleryManager.LoadManifest();

            RefreshList();
        }

        public void Hide()
        {
            _isVisible = false;
            if (_root != null)
                _root.gameObject.SetActive(false);

            ClearPreview();
        }

        public void Toggle()
        {
            if (_isVisible) Hide();
            else Show();
        }

        // ─── Lifecycle ──────────────────────────────────────────────────

        private void Start()
        {
            _mainCam = Camera.main;
            BuildPanel();
            _root.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!_isVisible || _mainCam == null)
                return;

            FollowHead();
            HandleInteraction();
            RotatePreview();
        }

        private void OnDestroy()
        {
            if (_currentPreviewMesh != null)
                Destroy(_currentPreviewMesh);
        }

        // ─── Head tracking ──────────────────────────────────────────────

        private void FollowHead()
        {
            var camT = _mainCam.transform;
            Vector3 forward = camT.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
                forward = camT.forward;
            forward.Normalize();

            Vector3 target = camT.position
                           + forward * _distance
                           + Vector3.up * _verticalOffset;

            _root.position = Vector3.Lerp(_root.position, target, Time.deltaTime * 5f);
            _root.rotation = Quaternion.LookRotation(
                _root.position - camT.position, Vector3.up);
        }

        // ─── Interaction ────────────────────────────────────────────────

        private void HandleInteraction()
        {
            if (_galleryManager == null || _handTracking == null)
                return;

            int workCount = _galleryManager.WorkCount;
            if (workCount == 0)
                return;

            // Check right hand index finger proximity to entry rows
            int hoveredRow = -1;
            int rIndexIdx = (int)HandTrackingManager.FingerID.RightIndex;
            ref var rIndex = ref _handTracking.Fingers[rIndexIdx];

            if (rIndex.IsTracked)
            {
                // Convert finger tip to panel local space
                Vector3 localTip = _root.InverseTransformPoint(rIndex.TipPosition);

                // Check if within panel bounds (Z depth ~< 0.1m in front)
                if (localTip.z < 0.1f && localTip.z > -0.1f)
                {
                    float entryHeight = _panelHeight / (_visibleEntries + 2);
                    float listTop = _panelHeight * 0.3f;

                    for (int i = 0; i < _visibleEntries; i++)
                    {
                        int dataIdx = _scrollIndex + i;
                        if (dataIdx >= workCount) break;

                        float entryY = listTop - (i + 0.5f) * entryHeight;
                        if (Mathf.Abs(localTip.y - entryY) < entryHeight * 0.5f
                            && Mathf.Abs(localTip.x) < _panelWidth * 0.45f)
                        {
                            hoveredRow = dataIdx;
                            break;
                        }
                    }
                }
            }

            // Update highlights
            for (int i = 0; i < _visibleEntries; i++)
            {
                int dataIdx = _scrollIndex + i;
                if (dataIdx == _selectedIndex)
                    _entryBgMats[i].color = ColorEntrySelect;
                else if (dataIdx == hoveredRow)
                    _entryBgMats[i].color = ColorEntryHover;
                else
                    _entryBgMats[i].color = ColorEntryNormal;
            }

            // Check pinch (thumb+index) on right hand for selection
            if (hoveredRow >= 0)
            {
                int rThumbIdx = (int)HandTrackingManager.FingerID.RightThumb;
                ref var rThumb = ref _handTracking.Fingers[rThumbIdx];

                if (rThumb.IsTracked && rIndex.IsTracked)
                {
                    float pinchDist = Vector3.Distance(rThumb.TipPosition, rIndex.TipPosition);
                    if (pinchDist < _pinchThreshold)
                    {
                        SelectEntry(hoveredRow);
                    }
                }
            }

            // Scroll: left hand index+thumb pinch near top/bottom of panel
            int lIndexIdx = (int)HandTrackingManager.FingerID.LeftIndex;
            int lThumbIdx = (int)HandTrackingManager.FingerID.LeftThumb;
            ref var lIndex = ref _handTracking.Fingers[lIndexIdx];
            ref var lThumb = ref _handTracking.Fingers[lThumbIdx];

            if (lIndex.IsTracked && lThumb.IsTracked)
            {
                float lPinch = Vector3.Distance(lThumb.TipPosition, lIndex.TipPosition);
                if (lPinch < _pinchThreshold)
                {
                    Vector3 localL = _root.InverseTransformPoint(lIndex.TipPosition);
                    if (localL.y > 0f && _scrollIndex > 0)
                    {
                        _scrollIndex--;
                        RefreshList();
                    }
                    else if (localL.y < 0f && _scrollIndex + _visibleEntries < workCount)
                    {
                        _scrollIndex++;
                        RefreshList();
                    }
                }
            }
        }

        private void SelectEntry(int dataIndex)
        {
            if (_selectedIndex == dataIndex)
                return;

            _selectedIndex = dataIndex;
            RefreshList();
            LoadPreview(dataIndex);
        }

        // ─── Preview ────────────────────────────────────────────────────

        private void LoadPreview(int dataIndex)
        {
            var entry = _galleryManager.GetEntry(dataIndex);
            if (entry == null) return;

            ClearPreview();

            _currentPreviewMesh = _galleryManager.LoadObjMesh(entry.filename);
            if (_currentPreviewMesh == null) return;

            _previewMeshFilter.sharedMesh = _currentPreviewMesh;
            _previewRoot.gameObject.SetActive(true);

            // Auto-scale to fit nicely beside the panel
            float size = _currentPreviewMesh.bounds.size.magnitude;
            float targetSize = 0.15f;
            float scale = size > 0.001f ? targetSize / size : 1f;
            _previewRoot.localScale = Vector3.one * scale;

            // Center on bounds
            _previewRoot.localPosition = new Vector3(
                _panelWidth * 0.5f + 0.08f,
                0f,
                -0.01f
            );

            _infoText.text = $"{entry.pointCount} pts | {entry.vertexCount} verts";
            _infoText.color = ColorTextSelect;
        }

        private void ClearPreview()
        {
            if (_currentPreviewMesh != null)
            {
                Destroy(_currentPreviewMesh);
                _currentPreviewMesh = null;
            }

            if (_previewRoot != null)
                _previewRoot.gameObject.SetActive(false);

            if (_infoText != null)
                _infoText.text = "";
        }

        private void RotatePreview()
        {
            if (_previewRoot != null && _previewRoot.gameObject.activeSelf)
            {
                _previewRoot.Rotate(Vector3.up, 30f * Time.deltaTime, Space.Self);
            }
        }

        // ─── List refresh ───────────────────────────────────────────────

        private void RefreshList()
        {
            int workCount = _galleryManager != null ? _galleryManager.WorkCount : 0;

            _titleText.text = $"Saved Works ({workCount})";

            for (int i = 0; i < _visibleEntries; i++)
            {
                int dataIdx = _scrollIndex + i;
                if (dataIdx < workCount)
                {
                    var entry = _galleryManager.GetEntry(dataIdx);
                    // Parse ISO timestamp to a short display format
                    string display;
                    if (System.DateTime.TryParse(entry.timestamp, out System.DateTime dt))
                        display = dt.ToString("MM/dd HH:mm");
                    else
                        display = entry.id;

                    _entryTexts[i].text = $" {dataIdx + 1}. {display} - {entry.pointCount}pts";
                    _entryTexts[i].color = (dataIdx == _selectedIndex)
                        ? ColorTextSelect : ColorTextNormal;
                    _entryBgs[i].gameObject.SetActive(true);
                }
                else
                {
                    _entryTexts[i].text = "";
                    _entryBgs[i].gameObject.SetActive(false);
                }
            }
        }

        // ─── Build panel ────────────────────────────────────────────────

        private void BuildPanel()
        {
            _root = new GameObject("GalleryPanel").transform;
            _root.SetParent(transform, false);

            // Background
            var bg = CreateQuad("BG", _root, _panelWidth, _panelHeight);
            SetColor(bg, ColorBg);
            bg.localPosition = Vector3.zero;

            // Title
            _titleText = CreateText("Title", _root,
                new Vector3(-_panelWidth * 0.45f, _panelHeight * 0.42f, -0.002f),
                40, 0.006f, ColorTitle);
            _titleText.text = "Saved Works (0)";

            // Entry rows
            float entryHeight = _panelHeight / (_visibleEntries + 2);
            float entryWidth = _panelWidth * 0.9f;
            float listTop = _panelHeight * 0.3f;

            _entryTexts = new TextMesh[_visibleEntries];
            _entryBgs = new Transform[_visibleEntries];
            _entryBgMats = new Material[_visibleEntries];

            for (int i = 0; i < _visibleEntries; i++)
            {
                float y = listTop - (i + 0.5f) * entryHeight;

                // Entry background
                var entryBg = CreateQuad($"EntryBG_{i}", _root, entryWidth, entryHeight * 0.85f);
                entryBg.localPosition = new Vector3(0f, y, -0.0005f);
                _entryBgMats[i] = CreateUnlitMat(ColorEntryNormal);
                entryBg.GetComponent<MeshRenderer>().sharedMaterial = _entryBgMats[i];
                _entryBgs[i] = entryBg;

                // Entry text
                _entryTexts[i] = CreateText($"Entry_{i}", _root,
                    new Vector3(-_panelWidth * 0.42f, y + 0.002f, -0.001f),
                    32, 0.005f, ColorTextNormal);
            }

            // Info text (below list, shows details of selected entry)
            _infoText = CreateText("Info", _root,
                new Vector3(-_panelWidth * 0.45f, -_panelHeight * 0.42f, -0.002f),
                28, 0.004f, ColorTextNormal);

            // Scroll hint
            var scrollHint = CreateText("ScrollHint", _root,
                new Vector3(_panelWidth * 0.35f, -_panelHeight * 0.42f, -0.002f),
                24, 0.004f, new Color(0.5f, 0.5f, 0.5f));
            scrollHint.text = "L pinch: scroll";
            scrollHint.anchor = TextAnchor.LowerRight;

            // Preview mesh holder
            var previewGO = new GameObject("Preview");
            _previewRoot = previewGO.transform;
            _previewRoot.SetParent(_root, false);

            var pmf = previewGO.AddComponent<MeshFilter>();
            _previewMeshFilter = pmf;

            var pmr = previewGO.AddComponent<MeshRenderer>();
            _previewRenderer = pmr;

            // Use a simple unlit material for preview
            var previewMat = CreateUnlitMat(new Color(0.7f, 0.8f, 1f));
            pmr.sharedMaterial = previewMat;

            _previewRoot.gameObject.SetActive(false);

            // Initial position
            if (_mainCam != null)
            {
                _root.position = _mainCam.transform.position
                    + _mainCam.transform.forward * _distance
                    + Vector3.up * _verticalOffset;
            }
        }

        // ─── UI helpers ─────────────────────────────────────────────────

        private static Transform CreateQuad(string name, Transform parent, float width, float height)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(width, height, 1f);

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            return go.transform;
        }

        private static void SetColor(Transform quad, Color color)
        {
            var mr = quad.GetComponent<MeshRenderer>();
            var mat = CreateUnlitMat(color);
            mat.renderQueue = 3000;
            mr.sharedMaterial = mat;
        }

        private static Material CreateUnlitMat(Color color)
        {
            var shader = Shader.Find("Unlit/Color")
                      ?? Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader);
            mat.color = color;
            return mat;
        }

        private static TextMesh CreateText(string name, Transform parent, Vector3 localPos,
            int fontSize, float charSize, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var tm = go.AddComponent<TextMesh>();
            tm.fontSize = fontSize;
            tm.characterSize = charSize;
            tm.anchor = TextAnchor.MiddleLeft;
            tm.alignment = TextAlignment.Left;
            tm.color = color;
            tm.text = "";
            return tm;
        }
    }
}
