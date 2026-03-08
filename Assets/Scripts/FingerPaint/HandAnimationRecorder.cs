using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif

namespace FingerPaint
{
    /// <summary>
    /// Records both hands' bone animations to separate AnimationClips using
    /// GameObjectRecorder (Editor-only, works via Quest Link).
    ///
    /// Setup:
    ///   1. Ensure HandMeshVisualizer is in the scene with hand prefabs assigned.
    ///   2. Add an empty GameObject -> HandAnimationRecorder component.
    ///   3. Drag the HandMeshVisualizer GameObject into Left/Right Hand Parent.
    ///   4. Press Play -> wait for hands to appear -> press Space to start/stop.
    ///   5. Clips appear in the configured save folder.
    ///
    /// How it works:
    ///   - Defers hand discovery until HandMeshVisualizer has instantiated the
    ///     "LeftHand" / "RightHand" children at runtime.
    ///   - Creates two GameObjectRecorder instances (one per hand) and binds all
    ///     child Transform components so every bone is captured.
    ///   - Saves timestamped .anim clips and optionally generates matching
    ///     AnimatorController assets for easy drag-and-drop playback.
    /// </summary>
    public class HandAnimationRecorder : MonoBehaviour
    {
        [Header("Hand Parents")]
        [Tooltip("GameObject that contains the instantiated 'LeftHand' child (typically the HandMeshVisualizer).")]
        [SerializeField] private Transform _leftHandParent;

        [Tooltip("GameObject that contains the instantiated 'RightHand' child (typically the HandMeshVisualizer).")]
        [SerializeField] private Transform _rightHandParent;

        [Header("Recording Settings")]
        [Tooltip("Animation sample rate (frames per second).")]
        [SerializeField] private float _sampleRate = 60f;

        [Tooltip("Folder inside Assets/ where recordings are saved.")]
        [SerializeField] private string _saveFolder = "Assets/Recordings/HandAnim";

        [Tooltip("Automatically create an AnimatorController (.controller) alongside each clip for easy playback.")]
        [SerializeField] private bool _createAnimatorController = true;

        [Header("Status (read-only)")]
        [SerializeField] private bool _isRecording;
        [SerializeField] private bool _handsFound;

        // ─── Runtime state ──────────────────────────────────────────────

        private Transform _leftHandRoot;   // The actual "LeftHand" child
        private Transform _rightHandRoot;  // The actual "RightHand" child

#if UNITY_EDITOR
        private GameObjectRecorder _leftRecorder;
        private GameObjectRecorder _rightRecorder;
#endif

        // ─── Lifecycle ──────────────────────────────────────────────────

        private void Update()
        {
            // Deferred hand discovery — retry each frame until found
            if (!_handsFound)
            {
                TryFindHands();
                return; // Don't process input until hands are ready
            }

            // Toggle recording with Space key
            if (UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                if (!_isRecording)
                    StartRecording();
                else
                    StopRecording();
            }

#if UNITY_EDITOR
            // Capture frame
            if (_isRecording)
            {
                if (_leftRecorder != null)
                    _leftRecorder.TakeSnapshot(Time.deltaTime);

                if (_rightRecorder != null)
                    _rightRecorder.TakeSnapshot(Time.deltaTime);
            }
#endif
        }

        private void OnDestroy()
        {
            // If still recording when destroyed, save what we have
            if (_isRecording)
            {
                Debug.LogWarning("[HandAnimationRecorder] OnDestroy while recording — saving current data.");
                StopRecording();
            }
        }

        // ─── Hand Discovery ─────────────────────────────────────────────

        private void TryFindHands()
        {
            if (_leftHandParent != null && _leftHandRoot == null)
            {
                _leftHandRoot = _leftHandParent.Find("LeftHand");
            }

            if (_rightHandParent != null && _rightHandRoot == null)
            {
                _rightHandRoot = _rightHandParent.Find("RightHand");
            }

            bool leftOk  = _leftHandRoot != null;
            bool rightOk = _rightHandRoot != null;

            if (leftOk || rightOk)
            {
                _handsFound = true;

                string status = "";
                if (leftOk)  status += $"LeftHand ({CountBones(_leftHandRoot)} bones) ";
                if (rightOk) status += $"RightHand ({CountBones(_rightHandRoot)} bones)";

                Debug.Log($"[HandAnimationRecorder] Hands discovered: {status}. Press Space to record.");
            }
        }

        private static int CountBones(Transform root)
        {
            // Count all child transforms (each is a bone)
            return root.GetComponentsInChildren<Transform>().Length;
        }

        // ─── Recording ──────────────────────────────────────────────────

        private void StartRecording()
        {
#if UNITY_EDITOR
            _leftRecorder = null;
            _rightRecorder = null;

            if (_leftHandRoot != null)
            {
                _leftRecorder = new GameObjectRecorder(_leftHandRoot.gameObject);
                _leftRecorder.BindComponentsOfType<Transform>(_leftHandRoot.gameObject, true);
                Debug.Log($"[HandAnimationRecorder] Left hand recorder bound ({CountBones(_leftHandRoot)} transforms).");
            }

            if (_rightHandRoot != null)
            {
                _rightRecorder = new GameObjectRecorder(_rightHandRoot.gameObject);
                _rightRecorder.BindComponentsOfType<Transform>(_rightHandRoot.gameObject, true);
                Debug.Log($"[HandAnimationRecorder] Right hand recorder bound ({CountBones(_rightHandRoot)} transforms).");
            }

            _isRecording = true;
            Debug.Log("[HandAnimationRecorder] *** RECORDING STARTED *** Move your hands!");
#else
            Debug.LogError("[HandAnimationRecorder] Recording is only available in the Unity Editor.");
#endif
        }

        private void StopRecording()
        {
            _isRecording = false;

#if UNITY_EDITOR
            string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HHmmss");

            // Ensure save folder exists
            EnsureFolderExists(_saveFolder);

            // Save left hand
            if (_leftRecorder != null && _leftRecorder.isRecording)
            {
                string clipName = $"LeftHand_{timestamp}";
                string clipPath = $"{_saveFolder}/{clipName}.anim";

                var clip = new AnimationClip { frameRate = _sampleRate };
                _leftRecorder.SaveToClip(clip);

                AssetDatabase.CreateAsset(clip, clipPath);
                Debug.Log($"[HandAnimationRecorder] Saved left hand clip: {clipPath}");

                if (_createAnimatorController)
                    CreateAnimatorController(clip, $"{_saveFolder}/{clipName}.controller");
            }

            // Save right hand
            if (_rightRecorder != null && _rightRecorder.isRecording)
            {
                string clipName = $"RightHand_{timestamp}";
                string clipPath = $"{_saveFolder}/{clipName}.anim";

                var clip = new AnimationClip { frameRate = _sampleRate };
                _rightRecorder.SaveToClip(clip);

                AssetDatabase.CreateAsset(clip, clipPath);
                Debug.Log($"[HandAnimationRecorder] Saved right hand clip: {clipPath}");

                if (_createAnimatorController)
                    CreateAnimatorController(clip, $"{_saveFolder}/{clipName}.controller");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _leftRecorder = null;
            _rightRecorder = null;

            Debug.Log($"[HandAnimationRecorder] *** RECORDING STOPPED *** Files saved to {_saveFolder}/");
#endif
        }

        // ─── Asset Helpers ──────────────────────────────────────────────

#if UNITY_EDITOR
        /// <summary>
        /// Creates a simple AnimatorController with one state playing the given clip.
        /// </summary>
        private static void CreateAnimatorController(AnimationClip clip, string path)
        {
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            // Get the base layer's state machine
            var rootStateMachine = controller.layers[0].stateMachine;

            // Add the clip as the default state
            var state = rootStateMachine.AddState(clip.name);
            state.motion = clip;

            Debug.Log($"[HandAnimationRecorder] Created AnimatorController: {path}");
        }

        /// <summary>
        /// Recursively creates folders to ensure the full save path exists.
        /// e.g. "Assets/Recordings/HandAnim" creates "Recordings" under "Assets",
        /// then "HandAnim" under "Assets/Recordings".
        /// </summary>
        private static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            string[] parts = folderPath.Replace("\\", "/").Split('/');
            string current = parts[0]; // "Assets"

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                    Debug.Log($"[HandAnimationRecorder] Created folder: {next}");
                }
                current = next;
            }
        }
#endif
    }
}
