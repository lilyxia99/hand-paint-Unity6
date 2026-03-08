using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Lightweight hand mesh visualizer that instantiates pre-rigged XR Hands prefabs
    /// and allows runtime material swapping.
    ///
    /// How it works:
    /// - The prefabs contain XRHandTrackingEvents + XRHandSkeletonDriver + XRHandMeshController.
    /// - XRHandTrackingEvents subscribes to the XRHandSubsystem for tracking data.
    /// - XRHandSkeletonDriver drives the bone transforms from joint tracking data,
    ///   making the rigged FBX mesh deform to match the real hand.
    /// - XRHandMeshController is DISABLED by this script because it manages the
    ///   SkinnedMeshRenderer.enabled state internally and can hide the mesh when
    ///   runtime hand mesh data isn't available from the subsystem. By disabling it,
    ///   we take control of the renderer ourselves while the skeleton still animates.
    ///
    /// LateUpdate forces the renderer.enabled state every frame as a safety net,
    /// preventing any XR component from overriding our visibility setting.
    ///
    /// This script does NOT subscribe to any XRHandSubsystem events and does NOT call
    /// SetActive on the hand instances, avoiding interference with HandTrackingManager.
    /// </summary>
    public class HandMeshVisualizer : MonoBehaviour
    {
        [Header("Hand Prefabs (from XR Hands Sample)")]
        [Tooltip("Drag in 'Left Hand Tracking' prefab from Assets/Samples/XR Hands/.../Prefabs/")]
        [SerializeField] private GameObject _leftHandPrefab;

        [Tooltip("Drag in 'Right Hand Tracking' prefab from Assets/Samples/XR Hands/.../Prefabs/")]
        [SerializeField] private GameObject _rightHandPrefab;

        [Header("Material")]
        [Tooltip("Material to apply to both hand meshes. Leave null to keep the prefab's default material.")]
        [SerializeField] private Material _handMaterial;

        [Header("Visibility")]
        [SerializeField] private bool _showHands = true;

        // ─── Runtime state ──────────────────────────────────────────────

        private GameObject _leftHandInstance;
        private GameObject _rightHandInstance;
        private SkinnedMeshRenderer _leftRenderer;
        private SkinnedMeshRenderer _rightRenderer;
        private Material _originalLeftMaterial;
        private Material _originalRightMaterial;
        private bool _setupDone;

        // ─── Public API ─────────────────────────────────────────────────

        /// <summary>
        /// Swap the material on both hand meshes at runtime.
        /// Pass null to revert to the prefab's original default material.
        /// </summary>
        public void SetHandMaterial(Material mat)
        {
            _handMaterial = mat;
            ApplyMaterial();
        }

        /// <summary>
        /// Show or hide both hand meshes by toggling only the SkinnedMeshRenderer.
        /// Does NOT call SetActive on the hand GameObjects, so the prefab's internal
        /// XR components continue running undisturbed.
        /// </summary>
        public void SetVisibility(bool visible)
        {
            _showHands = visible;
        }

        // ─── Lifecycle ──────────────────────────────────────────────────

        private void Start()
        {
            InstantiateHands();
            ApplyMaterial();

            Debug.Log($"[HandMeshVisualizer] Start complete. " +
                      $"LeftRenderer: {(_leftRenderer != null ? _leftRenderer.gameObject.name : "NULL")}, " +
                      $"RightRenderer: {(_rightRenderer != null ? _rightRenderer.gameObject.name : "NULL")}");

            _setupDone = true;
        }

        /// <summary>
        /// Every LateUpdate, force the renderer.enabled to match our _showHands flag.
        /// This is the nuclear option: even if XRHandMeshController or any other XR
        /// component re-subscribes and toggles the renderer, we override it here.
        /// </summary>
        private void LateUpdate()
        {
            if (!_setupDone) return;

            if (_leftRenderer != null && _leftRenderer.enabled != _showHands)
            {
                _leftRenderer.enabled = _showHands;
            }

            if (_rightRenderer != null && _rightRenderer.enabled != _showHands)
            {
                _rightRenderer.enabled = _showHands;
            }
        }

        private void OnDestroy()
        {
            if (_leftHandInstance != null) Destroy(_leftHandInstance);
            if (_rightHandInstance != null) Destroy(_rightHandInstance);
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                ApplyMaterial();
            }
        }

        // ─── Instantiation ─────────────────────────────────────────────

        private void InstantiateHands()
        {
            if (_leftHandPrefab != null)
            {
                _leftHandInstance = Instantiate(_leftHandPrefab, transform);
                _leftHandInstance.name = "LeftHand";
                _leftHandInstance.transform.localPosition = Vector3.zero;
                _leftHandInstance.transform.localRotation = Quaternion.identity;

                SetupHand(_leftHandInstance, out _leftRenderer, out _originalLeftMaterial);
            }
            else
            {
                Debug.LogWarning("[HandMeshVisualizer] Left Hand Prefab is not assigned.");
            }

            if (_rightHandPrefab != null)
            {
                _rightHandInstance = Instantiate(_rightHandPrefab, transform);
                _rightHandInstance.name = "RightHand";
                _rightHandInstance.transform.localPosition = Vector3.zero;
                _rightHandInstance.transform.localRotation = Quaternion.identity;

                SetupHand(_rightHandInstance, out _rightRenderer, out _originalRightMaterial);
            }
            else
            {
                Debug.LogWarning("[HandMeshVisualizer] Right Hand Prefab is not assigned.");
            }
        }

        /// <summary>
        /// Configures an instantiated hand prefab:
        /// 1. Finds the SkinnedMeshRenderer and caches the original material.
        /// 2. Disables XRHandMeshController so it stops managing renderer.enabled.
        /// 3. Explicitly enables the SkinnedMeshRenderer so the mesh is visible.
        /// </summary>
        private static void SetupHand(
            GameObject handInstance,
            out SkinnedMeshRenderer renderer,
            out Material originalMaterial)
        {
            renderer = null;
            originalMaterial = null;

            // Find the SkinnedMeshRenderer (may be on root or a child)
            renderer = handInstance.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null)
                renderer = handInstance.GetComponentInChildren<SkinnedMeshRenderer>(true);

            if (renderer != null)
            {
                originalMaterial = renderer.sharedMaterial;
                renderer.enabled = true;

                Debug.Log($"[HandMeshVisualizer] Found SkinnedMeshRenderer on \"{renderer.gameObject.name}\" " +
                          $"(mesh: {(renderer.sharedMesh != null ? renderer.sharedMesh.name : "NULL")}, " +
                          $"bones: {renderer.bones.Length}, " +
                          $"material: {(renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "NULL")})");
            }
            else
            {
                Debug.LogError($"[HandMeshVisualizer] No SkinnedMeshRenderer found on {handInstance.name} or children!");
            }

            // Disable XRHandMeshController so it stops fighting over renderer.enabled.
            // We search by type name to avoid a hard compile dependency on the XR Hands assembly.
            DisableComponentByName(handInstance, "XRHandMeshController");
        }

        // ─── Material ───────────────────────────────────────────────────

        private void ApplyMaterial()
        {
            if (_leftRenderer != null)
            {
                _leftRenderer.sharedMaterial = _handMaterial != null
                    ? _handMaterial
                    : _originalLeftMaterial;
            }

            if (_rightRenderer != null)
            {
                _rightRenderer.sharedMaterial = _handMaterial != null
                    ? _handMaterial
                    : _originalRightMaterial;
            }
        }

        // ─── Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Finds and disables a MonoBehaviour by type name on the given GameObject
        /// or any of its children. Avoids a direct type reference that could cause
        /// compile errors if the assembly isn't accessible.
        /// </summary>
        private static void DisableComponentByName(GameObject root, string typeName)
        {
            foreach (var comp in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (comp != null && comp.GetType().Name == typeName)
                {
                    comp.enabled = false;
                    Debug.Log($"[HandMeshVisualizer] Disabled {typeName} on \"{comp.gameObject.name}\".");
                    return;
                }
            }

            // Not found — log so we know
            Debug.LogWarning($"[HandMeshVisualizer] Could not find {typeName} on {root.name} or children.");
        }
    }
}
