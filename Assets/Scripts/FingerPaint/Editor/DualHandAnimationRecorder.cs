/*
 * DualHandAnimationRecorder — records BOTH hands simultaneously into
 * two separate AnimationClips with synchronized timestamps.
 *
 * Based on Meta's HandAnimationRecorder pattern but extended for dual-hand
 * recording and dual-ghost preview.
 *
 * Menu: Meta/Interaction/Dual Hand Animation Recorder
 */

using Oculus.Interaction;
using Oculus.Interaction.HandGrab.Editor;
using Oculus.Interaction.HandGrab.Visuals;
using Oculus.Interaction.Input;
using Oculus.Interaction.Utils;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace FingerPaint.Editor
{
    /// <summary>
    /// Reflection wrapper for UnityEditor.AudioUtil (internal class).
    /// </summary>
    internal static class AudioUtilReflection
    {
        private static readonly System.Type _type =
            typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AudioUtil");

        public static void StopAllPreviewClips()
        {
            var method = _type?.GetMethod("StopAllPreviewClips",
                BindingFlags.Static | BindingFlags.Public);
            method?.Invoke(null, null);
        }

        public static void PlayPreviewClip(AudioClip clip, int startSample = 0, bool loop = false)
        {
            var method = _type?.GetMethod("PlayPreviewClip",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new System.Type[] { typeof(AudioClip), typeof(int), typeof(bool) },
                null);
            method?.Invoke(null, new object[] { clip, startSample, loop });
        }
    }

    public class DualHandAnimationRecorder : EditorWindow
    {
        // ─── Hand Visual References ─────────────────────────────────────
        [SerializeField] private HandVisual _leftHandVisual;
        [SerializeField] private HandVisual _rightHandVisual;

        // ─── Recording Settings ─────────────────────────────────────────
        [SerializeField] private string _handLeftPrefix = "_l_";
        [SerializeField] private string _handRightPrefix = "_r_";

        [SerializeField]
        private HandFingerJointFlags _includedJoints =
#if ISDK_OPENXR_HAND
            HandFingerJointFlags.All;
#else
            HandFingerJointFlags.HandMaxSkinnable - 1;
#endif

        [SerializeField] private bool _includeJointPosition = true;
        [SerializeField] private int _framerate = 30;
        [SerializeField] private float _slopeRotationThreshold = 0.1f;
        [SerializeField] private float _slopePositionThreshold = 0.0005f;

        // ─── Output Settings ────────────────────────────────────────────
        [SerializeField] private string _folder = "GeneratedAnimations";
        [SerializeField] private string _clipBaseName = "HandAnimation";
        [SerializeField] private KeyCode _recordKey = KeyCode.Space;

        // ─── Voice Recording Settings ──────────────────────────────────
        [SerializeField] private bool _recordVoice = false;
        [SerializeField] private string _audioFolder = "AudioRecordings";
        [SerializeField] private int _voiceSampleRate = 44100;
        [SerializeField] private int _micDeviceIndex = 0;

        // ─── Ghost Preview ──────────────────────────────────────────────
        [SerializeField] private HandGhostProvider _ghostProvider;
#if ISDK_OPENXR_HAND
        [SerializeField] private HandGhostProvider _handGhostProvider;
#endif

        private HandGhostProvider GhostProvider
        {
#if ISDK_OPENXR_HAND
            get => _handGhostProvider;
#else
            get => _ghostProvider;
#endif
        }

        // ─── Recorded Clips ─────────────────────────────────────────────
        [SerializeField] private AnimationClip _leftClip;
        [SerializeField] private AnimationClip _rightClip;
        [SerializeField] private AudioClip _voiceClip;

        // ─── Recording State ────────────────────────────────────────────
        private JointRecord[] _leftJointRecords;
        private JointRecord _leftRootRecord;
        private JointRecord[] _rightJointRecords;
        private JointRecord _rightRootRecord;
        private float _startTime;
        private bool _isRecording;

        // ─── Voice Recording State ─────────────────────────────────────
        private EditorMicRecorder _micRecorder;

        // ─── Preview State ──────────────────────────────────────────────
        private HandGhost _leftGhost;
        private HandGhost _rightGhost;
        private bool _showMin = true;
        private float _trimMin = 0f;
        private float _trimMax = 1f;
        private bool _forceUpdateGhosts = true;

        // ─── Playback State ─────────────────────────────────────────────
        private bool _isPlaying;
        private float _playbackNormalizedTime = 0f;
        private double _lastPlaybackEditorTime = -1;
        private float _playbackSpeed = 1f;
        private static readonly float[] _speedOptions = { 0.25f, 0.5f, 1f, 2f, 4f };
        private static readonly string[] _speedLabels = { "0.25×", "0.5×", "1×", "2×", "4×" };

        // ─── Audio Scrub State ──────────────────────────────────────────
        private double _lastScrubEditorTime = -1;
        private bool _isScrubbing;

        // ─── Voice Playback ───────────────────────────────────────────
        private AudioSource _voiceAudioSource;

        // ─── UI State ───────────────────────────────────────────────────
        private GUIStyle _richTextStyle;
        private Vector2 _scrollPos = Vector2.zero;

        // ═══════════════════════════════════════════════════════════════
        // Menu
        // ═══════════════════════════════════════════════════════════════

        [MenuItem("Meta/Interaction/Dual Hand Animation Recorder")]
        private static void CreateWizard()
        {
            var window = GetWindow<DualHandAnimationRecorder>();
            window.titleContent = new GUIContent("Dual Hand Recorder");
            window.Show();
        }

        // ═══════════════════════════════════════════════════════════════
        // Lifecycle
        // ═══════════════════════════════════════════════════════════════

        private void OnEnable()
        {
            _richTextStyle = EditorGUIUtility.GetBuiltinSkin(
                EditorGUIUtility.isProSkin ? EditorSkin.Scene : EditorSkin.Inspector).label;
            _richTextStyle.richText = true;
            _richTextStyle.wordWrap = true;

            if (_ghostProvider == null)
                HandGhostProviderUtils.TryGetDefaultProvider(out _ghostProvider);

            _forceUpdateGhosts = true;
        }

        private void OnDisable()
        {
            if (_isRecording)
                StopRecording();

            StopPlayback();

            // Safety: clean up mic if StopRecording didn't already
            if (_micRecorder != null)
            {
                EditorApplication.update -= DrainMicSamples;
                _micRecorder.Dispose();
                _micRecorder = null;
            }

            StopVoicePlayback();
            DestroyVoiceAudioSource();
            StopVoiceScrub();
            DestroyGhosts();
        }

        // ═══════════════════════════════════════════════════════════════
        // GUI
        // ═══════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            // ── Keyboard shortcut ────────────────────────────────────
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == _recordKey)
            {
                if (!_isRecording) StartRecording();
                else StopRecording();
                e.Use();
            }

            GUILayout.Label(
                "Record <b>both hands simultaneously</b> during <b>Play Mode</b>.\n" +
                "Outputs two synchronized Animation Clips (left + right).",
                _richTextStyle);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            // ── Hand Visual assignments ──────────────────────────────
            GUILayout.Space(10);
            GUILayout.Label("<b>Hand Visuals</b> (drag from scene hierarchy):", _richTextStyle);
            HandAnimationUtils.GenerateObjectField(ref _leftHandVisual, "Left Hand Visual");
            HandAnimationUtils.GenerateObjectField(ref _rightHandVisual, "Right Hand Visual");

            bool hasLeft = _leftHandVisual != null;
            bool hasRight = _rightHandVisual != null;
            if (!hasLeft && !hasRight)
            {
                EditorGUILayout.HelpBox(
                    "Assign at least one HandVisual. For dual recording, assign both.",
                    MessageType.Warning);
            }

            // ── Recording settings ───────────────────────────────────
            GUILayout.Space(10);
            _includedJoints = (HandFingerJointFlags)EditorGUILayout.EnumFlagsField("Record Joints", _includedJoints);
            _includeJointPosition = EditorGUILayout.Toggle("Include Position", _includeJointPosition);
            _framerate = EditorGUILayout.IntField("Animation framerate", _framerate);
            _slopeRotationThreshold = EditorGUILayout.FloatField("Rotation compression delta", _slopeRotationThreshold);
            _slopePositionThreshold = EditorGUILayout.FloatField("Translation compression delta", _slopePositionThreshold);
            _handLeftPrefix = EditorGUILayout.TextField("Left prefix", _handLeftPrefix);
            _handRightPrefix = EditorGUILayout.TextField("Right prefix", _handRightPrefix);

            // ── Output settings ──────────────────────────────────────
            GUILayout.Space(10);
            GUILayout.Label("Output location:", _richTextStyle);
            _folder = EditorGUILayout.TextField("Assets sub-folder", _folder);
            _clipBaseName = EditorGUILayout.TextField("Base name", _clipBaseName);

            if (hasLeft || hasRight)
            {
                string preview = "";
                if (hasLeft) preview += $"  Left:  {_clipBaseName}_Left.anim\n";
                if (hasRight) preview += $"  Right: {_clipBaseName}_Right.anim";
                if (_recordVoice) preview += $"\n  Voice: {_clipBaseName}_Voice.wav";
                EditorGUILayout.HelpBox($"Will generate:\n{preview}", MessageType.Info);
            }

            // ── Voice recording settings ─────────────────────────────
            GUILayout.Space(10);
            GUILayout.Label("<b>Voice Recording</b>", _richTextStyle);
            _recordVoice = EditorGUILayout.Toggle("Record Voice", _recordVoice);
            if (_recordVoice)
            {
                // ── Microphone device selector ──────────────────────
                string[] micDevices = Microphone.devices;
                if (micDevices.Length == 0)
                {
                    EditorGUILayout.HelpBox("No microphone devices detected.", MessageType.Warning);
                }
                else
                {
                    // Clamp index to valid range (devices can be hot-plugged)
                    if (_micDeviceIndex >= micDevices.Length)
                        _micDeviceIndex = 0;

                    _micDeviceIndex = EditorGUILayout.Popup("Microphone", _micDeviceIndex, micDevices);
                }

                _audioFolder = EditorGUILayout.TextField("Audio sub-folder", _audioFolder);
                _voiceSampleRate = EditorGUILayout.IntField("Sample rate (Hz)", _voiceSampleRate);
                EditorGUILayout.HelpBox(
                    $"Voice will be saved to:\n  Assets/{_audioFolder}/{_clipBaseName}_Voice.wav",
                    MessageType.Info);
            }

            // ── Record button ────────────────────────────────────────
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Start/stop key:", _richTextStyle);
            _recordKey = (KeyCode)EditorGUILayout.EnumPopup(_recordKey);
            GUILayout.EndHorizontal();

            GUI.backgroundColor = _isRecording ? new Color(1f, 0.4f, 0.4f) : Color.white;
            string btnLabel = _isRecording ? "■  Stop Recording" : "●  Start Recording";
            if (GUILayout.Button(btnLabel, GUILayout.Height(60)))
            {
                if (!_isRecording) StartRecording();
                else StopRecording();
            }
            GUI.backgroundColor = Color.white;

            if (_isRecording)
            {
                float elapsed = Time.time - _startTime;
                EditorGUILayout.HelpBox($"Recording... {elapsed:F1}s elapsed", MessageType.None);
                Repaint(); // Keep updating the elapsed time display
            }

            // ── Ghost Provider ────────────────────────────────────────
            GUILayout.Space(10);
            GUILayout.Label("Ghost provider for preview:", _richTextStyle);
#if ISDK_OPENXR_HAND
            if (HandAnimationUtils.GenerateObjectField(ref _handGhostProvider))
#else
            if (HandAnimationUtils.GenerateObjectField(ref _ghostProvider))
#endif
            {
                _forceUpdateGhosts = true;
                HandGhostProviderUtils.SetLastDefaultProvider(GhostProvider);
            }

            // ── Recorded clips ───────────────────────────────────────
            GUILayout.Space(10);
            GUILayout.Label("<b>Recorded clips:</b>", _richTextStyle);
            _forceUpdateGhosts |= HandAnimationUtils.GenerateObjectField(ref _leftClip, "Left Clip");
            _forceUpdateGhosts |= HandAnimationUtils.GenerateObjectField(ref _rightClip, "Right Clip");
            _voiceClip = (AudioClip)EditorGUILayout.ObjectField("Voice Clip", _voiceClip, typeof(AudioClip), false);

            // ── Preview / Trim ───────────────────────────────────────
            bool hasAnyClip = _leftClip != null || _rightClip != null || _voiceClip != null;
            if (hasAnyClip)
            {
                float clipLength = 0f;
                if (_leftClip != null) clipLength = Mathf.Max(clipLength, _leftClip.length);
                if (_rightClip != null) clipLength = Mathf.Max(clipLength, _rightClip.length);
                if (_voiceClip != null) clipLength = Mathf.Max(clipLength, _voiceClip.length);

                // ── Playback controls ────────────────────────────────
                GUILayout.Space(10);
                GUILayout.Label("<b>Playback Preview</b>", _richTextStyle);

                // Time display
                float currentSeconds = _playbackNormalizedTime * clipLength;
                int curMin = (int)(currentSeconds / 60f);
                float curSec = currentSeconds - curMin * 60f;
                int totMin = (int)(clipLength / 60f);
                float totSec = clipLength - totMin * 60f;
                EditorGUILayout.LabelField(
                    $"  {curMin:D2}:{curSec:05.2f}  /  {totMin:D2}:{totSec:05.2f}",
                    EditorStyles.boldLabel);

                // Playback slider
                float prevPlayback = _playbackNormalizedTime;
                _playbackNormalizedTime = EditorGUILayout.Slider(_playbackNormalizedTime, 0f, 1f);
                bool playbackSliderChanged = !Mathf.Approximately(prevPlayback, _playbackNormalizedTime);
                if (playbackSliderChanged && _isPlaying)
                {
                    // User dragged slider while playing — resync voice from new position
                    _lastPlaybackEditorTime = EditorApplication.timeSinceStartup;
                    StartVoicePlayback(_playbackNormalizedTime);
                }

                // Play/Pause + Speed
                GUILayout.BeginHorizontal();

                GUI.backgroundColor = _isPlaying ? new Color(1f, 0.85f, 0.4f) : new Color(0.4f, 1f, 0.5f);
                string playLabel = _isPlaying ? "||  Pause" : ">  Play";
                if (GUILayout.Button(playLabel, GUILayout.Height(30), GUILayout.Width(100)))
                {
                    if (_isPlaying)
                    {
                        StopPlayback();
                        StopVoicePlayback();
                    }
                    else
                    {
                        StartPlayback();
                        StartVoicePlayback(_playbackNormalizedTime);
                    }
                    Repaint();
                }
                GUI.backgroundColor = Color.white;

                if (GUILayout.Button("|<", GUILayout.Height(30), GUILayout.Width(35)))
                {
                    _playbackNormalizedTime = 0f;
                    _lastPlaybackEditorTime = EditorApplication.timeSinceStartup;
                    playbackSliderChanged = true;
                    if (_isPlaying)
                        StartVoicePlayback(0f);
                    else
                        StopVoicePlayback();
                }

                // Speed selector
                GUILayout.Label("Speed:", GUILayout.Width(42));
                int currentSpeedIdx = System.Array.IndexOf(_speedOptions, _playbackSpeed);
                if (currentSpeedIdx < 0) currentSpeedIdx = 2; // default 1x
                int newSpeedIdx = EditorGUILayout.Popup(currentSpeedIdx, _speedLabels, GUILayout.Width(55));
                _playbackSpeed = _speedOptions[newSpeedIdx];
                if (_isPlaying && _voiceAudioSource != null)
                    _voiceAudioSource.pitch = _playbackSpeed;

                GUILayout.EndHorizontal();

                // Frame stepping
                GUILayout.BeginHorizontal();
                float frameStep1 = (clipLength > 0f) ? (1f / (_framerate * clipLength)) : 0f;
                float frameStep5 = frameStep1 * 5f;

                if (GUILayout.Button("<< 5f", GUILayout.Height(24)))
                {
                    _playbackNormalizedTime = Mathf.Max(0f, _playbackNormalizedTime - frameStep5);
                    playbackSliderChanged = true;
                }
                if (GUILayout.Button("< 1f", GUILayout.Height(24)))
                {
                    _playbackNormalizedTime = Mathf.Max(0f, _playbackNormalizedTime - frameStep1);
                    playbackSliderChanged = true;
                }
                if (GUILayout.Button("1f >", GUILayout.Height(24)))
                {
                    _playbackNormalizedTime = Mathf.Min(1f, _playbackNormalizedTime + frameStep1);
                    playbackSliderChanged = true;
                }
                if (GUILayout.Button("5f >>", GUILayout.Height(24)))
                {
                    _playbackNormalizedTime = Mathf.Min(1f, _playbackNormalizedTime + frameStep5);
                    playbackSliderChanged = true;
                }

                // Prev/Next keyframe
                if (GUILayout.Button("< Key", GUILayout.Height(24)))
                {
                    float t = FindAdjacentKeyframeTime(clipLength, -1);
                    if (t >= 0f) { _playbackNormalizedTime = t / clipLength; playbackSliderChanged = true; }
                }
                if (GUILayout.Button("Key >", GUILayout.Height(24)))
                {
                    float t = FindAdjacentKeyframeTime(clipLength, 1);
                    if (t >= 0f) { _playbackNormalizedTime = t / clipLength; playbackSliderChanged = true; }
                }
                GUILayout.EndHorizontal();

                // Set Trim Start / End buttons
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"Set Trim Start ({curMin:D2}:{curSec:04.1f})", GUILayout.Height(24)))
                {
                    _trimMin = _playbackNormalizedTime;
                    if (_trimMin > _trimMax) _trimMax = _trimMin;
                }
                if (GUILayout.Button($"Set Trim End ({curMin:D2}:{curSec:04.1f})", GUILayout.Height(24)))
                {
                    _trimMax = _playbackNormalizedTime;
                    if (_trimMax < _trimMin) _trimMin = _trimMax;
                }
                GUILayout.EndHorizontal();

                // ── Trim controls ────────────────────────────────────
                GUILayout.Space(10);
                GUILayout.Label("<b>Trim Range</b>", _richTextStyle);
                GUILayout.BeginHorizontal();

                float prevMin = _trimMin;
                float prevMax = _trimMax;
                EditorGUILayout.MinMaxSlider(ref _trimMin, ref _trimMax, 0f, 1f);
                _showMin = (prevMin != _trimMin) || (_showMin && prevMax == _trimMax);

                if (GUILayout.Button("Trim All", GUILayout.Height(20)))
                {
                    StopPlayback();
                    if (_leftClip != null)
                        SafeTrim(ref _leftClip, _trimMin, _trimMax);
                    if (_rightClip != null)
                        SafeTrim(ref _rightClip, _trimMin, _trimMax);
                    if (_voiceClip != null)
                        TrimVoiceClip(_trimMin, _trimMax);
                    _trimMin = 0f;
                    _trimMax = 1f;
                    _playbackNormalizedTime = 0f;
                }
                GUILayout.EndHorizontal();

                // Trim range time display
                float trimStartSec = _trimMin * clipLength;
                float trimEndSec = _trimMax * clipLength;
                EditorGUILayout.LabelField(
                    $"  Start: {(int)(trimStartSec/60):D2}:{(trimStartSec%60):04.1f}  —  End: {(int)(trimEndSec/60):D2}:{(trimEndSec%60):04.1f}  ({(trimEndSec - trimStartSec):F1}s)",
                    EditorStyles.miniLabel);

                // ── Mirror buttons ───────────────────────────────────
                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                if (_leftClip != null && GUILayout.Button("Mirror Left → Right"))
                    MirrorClip(_leftClip);
                if (_rightClip != null && GUILayout.Button("Mirror Right → Left"))
                    MirrorClip(_rightClip);
                GUILayout.EndHorizontal();

                // ── Advance playback ─────────────────────────────────
                if (_isPlaying)
                {
                    double now = EditorApplication.timeSinceStartup;
                    if (_lastPlaybackEditorTime > 0)
                    {
                        double delta = now - _lastPlaybackEditorTime;
                        float normalizedDelta = (float)(delta * _playbackSpeed) / clipLength;
                        _playbackNormalizedTime += normalizedDelta;

                        if (_playbackNormalizedTime >= 1f)
                        {
                            _playbackNormalizedTime = 1f;
                            StopPlayback();
                            StopVoicePlayback();
                        }
                    }
                    _lastPlaybackEditorTime = now;
                    Repaint();
                }

                // ── Update ghosts + audio scrub ──────────────────────
                float ghostTime;
                bool voiceScrubChanged;
                bool trimSliderChanged = (prevMin != _trimMin) || (prevMax != _trimMax);
                if (trimSliderChanged)
                {
                    // User is dragging the trim slider — show trim position
                    ghostTime = _showMin ? _trimMin : _trimMax;
                    voiceScrubChanged = true;
                }
                else
                {
                    // Use playback position (whether playing, paused, or scrubbing)
                    ghostTime = _playbackNormalizedTime;
                    voiceScrubChanged = playbackSliderChanged && !_isPlaying;
                }
                UpdateGhosts(ghostTime, _forceUpdateGhosts || _isPlaying || playbackSliderChanged || trimSliderChanged);
                ScrubVoicePreview(ghostTime, voiceScrubChanged);
            }
            else
            {
                StopPlayback();
                StopVoiceScrub();
                DestroyGhosts();
            }

            _forceUpdateGhosts = false;
            GUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════════════
        // Recording
        // ═══════════════════════════════════════════════════════════════

        private void StartRecording()
        {
            if (_isRecording) return;
            if (_leftHandVisual == null && _rightHandVisual == null)
            {
                Debug.LogError("[DualHandRecorder] No HandVisual assigned!");
                return;
            }

            _isRecording = true;
            _startTime = Time.time;

            if (_leftHandVisual != null)
            {
                InitializeRecords(_leftHandVisual, out _leftJointRecords, out _leftRootRecord);
                _leftHandVisual.WhenHandVisualUpdated += HandleLeftHandUpdated;
            }

            if (_rightHandVisual != null)
            {
                InitializeRecords(_rightHandVisual, out _rightJointRecords, out _rightRootRecord);
                _rightHandVisual.WhenHandVisualUpdated += HandleRightHandUpdated;
            }

            // ── Voice recording ───────────────────────────────────────
            if (_recordVoice)
            {
                _micRecorder = new EditorMicRecorder(_voiceSampleRate);

                // Resolve selected device name (null → first available)
                string selectedDevice = null;
                string[] micDevices = Microphone.devices;
                if (micDevices.Length > 0 && _micDeviceIndex < micDevices.Length)
                    selectedDevice = micDevices[_micDeviceIndex];

                if (_micRecorder.StartCapture(selectedDevice))
                {
                    EditorApplication.update += DrainMicSamples;
                    Debug.Log($"[DualHandRecorder] Voice recording started @ {_micRecorder.EffectiveSampleRate} Hz");
                }
                else
                {
                    Debug.LogWarning("[DualHandRecorder] Failed to start microphone. Voice will NOT be recorded.");
                    _micRecorder.Dispose();
                    _micRecorder = null;
                }
            }

            int count = (_leftHandVisual != null ? 1 : 0) + (_rightHandVisual != null ? 1 : 0);
            string voiceStr = _micRecorder != null ? " + VOICE" : "";
            Debug.Log($"[DualHandRecorder] *** RECORDING {count} HAND(S){voiceStr} *** Move your hands!");
        }

        private void StopRecording()
        {
            if (!_isRecording) return;
            _isRecording = false;

            if (_leftHandVisual != null)
            {
                _leftHandVisual.WhenHandVisualUpdated -= HandleLeftHandUpdated;
                _leftClip = GenerateClipAsset($"{_clipBaseName}_Left", _leftRootRecord, _leftJointRecords);
                Debug.Log($"[DualHandRecorder] Left clip saved: {_leftClip.length:F2}s, {_leftClip.frameRate}fps");
            }

            if (_rightHandVisual != null)
            {
                _rightHandVisual.WhenHandVisualUpdated -= HandleRightHandUpdated;
                _rightClip = GenerateClipAsset($"{_clipBaseName}_Right", _rightRootRecord, _rightJointRecords);
                Debug.Log($"[DualHandRecorder] Right clip saved: {_rightClip.length:F2}s, {_rightClip.frameRate}fps");
            }

            // ── Save voice recording ─────────────────────────────────
            if (_micRecorder != null)
            {
                EditorApplication.update -= DrainMicSamples;

                var samples = _micRecorder.StopCaptureAndGetSamples();
                int sampleRate = _micRecorder.EffectiveSampleRate;
                _micRecorder.Dispose();
                _micRecorder = null;

                if (samples.Count > 0)
                {
                    string targetFolder = Path.Combine("Assets", _audioFolder);
                    if (!Directory.Exists(targetFolder))
                        Directory.CreateDirectory(targetFolder);

                    string wavPath = Path.Combine(targetFolder, $"{_clipBaseName}_Voice.wav");
                    WavFileWriter.Write(wavPath, samples, sampleRate);
                    AssetDatabase.ImportAsset(wavPath);

                    // Auto-assign the voice clip reference
                    _voiceClip = AssetDatabase.LoadAssetAtPath<AudioClip>(wavPath);

                    float duration = (float)samples.Count / sampleRate;
                    Debug.Log($"[DualHandRecorder] Voice saved: {wavPath} ({duration:F2}s, {sampleRate}Hz)");
                }
                else
                {
                    Debug.LogWarning("[DualHandRecorder] No audio samples captured.");
                }
            }

            Debug.Log("[DualHandRecorder] *** RECORDING STOPPED ***");
            _forceUpdateGhosts = true;
        }

        // ─── Microphone drain callback ───────────────────────────────

        private void DrainMicSamples()
        {
            if (_micRecorder != null && _micRecorder.IsCapturing)
                _micRecorder.DrainSamples();
        }

        // ─── Per-hand update callbacks ───────────────────────────────

        private void HandleLeftHandUpdated()
        {
            float time = Time.time - _startTime;
            ReadPoses(time, _leftHandVisual, _leftJointRecords, _leftRootRecord);
        }

        private void HandleRightHandUpdated()
        {
            float time = Time.time - _startTime;
            ReadPoses(time, _rightHandVisual, _rightJointRecords, _rightRootRecord);
        }

        // ─── Shared recording logic ─────────────────────────────────

        private void InitializeRecords(HandVisual handVisual,
            out JointRecord[] jointRecords, out JointRecord rootRecord)
        {
            jointRecords = new JointRecord[(int)HandJointId.HandEnd];
            Transform root = handVisual.Root;
            foreach (HandJointId jointId in IncludedJointIds())
            {
                Transform jointTransform = handVisual.GetTransformByHandJointId(jointId);
                string path = HandAnimationUtils.GetGameObjectPath(jointTransform, root);
                jointRecords[(int)jointId] = new JointRecord(jointId, path);
            }
            rootRecord = new JointRecord(HandJointId.Invalid, "");
        }

        private void ReadPoses(float time, HandVisual handVisual,
            JointRecord[] jointRecords, JointRecord rootRecord)
        {
            rootRecord.RecordPose(time, handVisual.Root.GetPose(Space.World));
            foreach (HandJointId jointId in IncludedJointIds())
            {
                Pose pose = handVisual.GetJointPose(jointId, Space.Self);
                jointRecords[(int)jointId].RecordPose(time, pose);
            }
        }

        private AnimationClip GenerateClipAsset(string title,
            JointRecord rootRecord, JointRecord[] jointRecords)
        {
            var clip = new AnimationClip { frameRate = _framerate };

            HandAnimationUtils.WriteAnimationCurves(ref clip, rootRecord, true);
            foreach (HandJointId jointId in IncludedJointIds())
            {
                int index = (int)jointId;
                HandAnimationUtils.WriteAnimationCurves(ref clip, jointRecords[index], _includeJointPosition);
            }
            HandAnimationUtils.Compress(ref clip, _slopeRotationThreshold, _slopePositionThreshold);
            HandAnimationUtils.StoreAsset(clip, _folder, $"{title}.anim");
            return clip;
        }

        private IEnumerable<HandJointId> IncludedJointIds()
        {
            for (HandJointId jointId = HandJointId.HandStart; jointId < HandJointId.HandEnd; jointId++)
            {
                int index = (int)jointId;
                if (((int)_includedJoints & (1 << index)) == 0)
                    continue;
                yield return jointId;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Mirror
        // ═══════════════════════════════════════════════════════════════

        private void MirrorClip(AnimationClip clip)
        {
            HandVisual sourceVisual = clip == _leftClip ? _leftHandVisual : _rightHandVisual;
            if (sourceVisual == null)
            {
                Debug.LogError("[DualHandRecorder] Need the original HandVisual to mirror.");
                return;
            }

            if (!HandAnimationUtils.TryGetClipHandedness(clip, _handLeftPrefix, _handRightPrefix,
                    out Handedness fromHandedness))
            {
                fromHandedness = sourceVisual.Root.name.ToLower().Contains("left")
                    ? Handedness.Left : Handedness.Right;
            }

            HandFingerJointFlags jointIdMask = HandFingerJointFlags.None;
            foreach (var id in IncludedJointIds())
                jointIdMask |= (HandFingerJointFlags)(1 << (int)id);

            AnimationClip mirrorClip = HandAnimationUtils.Mirror(clip,
                sourceVisual.Joints, sourceVisual.Root, jointIdMask,
                fromHandedness, _handLeftPrefix, _handRightPrefix, _includeJointPosition);
            HandAnimationUtils.Compress(ref mirrorClip, _slopeRotationThreshold, _slopePositionThreshold);
            HandAnimationUtils.StoreAsset(mirrorClip, _folder, $"{clip.name}_mirror.anim");
        }

        // ═══════════════════════════════════════════════════════════════
        // Safe Trim (fixes Euler angle corruption in Meta SDK Trim)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Trims an AnimationClip to the normalized [minTime, maxTime] range.
        /// Only resamples a small buffer zone around trim boundaries to prevent
        /// Euler/tangent corruption, while keeping existing keyframes in the
        /// middle for speed.
        /// </summary>
        private void SafeTrim(ref AnimationClip clip, float minTime, float maxTime)
        {
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);

            float min = minTime * clip.length;
            float max = maxTime * clip.length;
            float frameRate = clip.frameRate > 0 ? clip.frameRate : 30f;
            float dt = 1f / frameRate;

            // Buffer zone: resample this many seconds around each boundary
            float bufferSec = 0.5f;
            float safeStart = min + bufferSec; // end of start-buffer
            float safeEnd = max - bufferSec;   // start of end-buffer

            foreach (var binding in bindings)
            {
                AnimationCurve srcCurve = AnimationUtility.GetEditorCurve(clip, binding);
                var newKeys = new List<Keyframe>();

                // 1) Resample the START boundary zone [min, min+buffer]
                for (float t = min; t < Mathf.Min(safeStart, max); t += dt)
                {
                    newKeys.Add(new Keyframe(t - min, srcCurve.Evaluate(t)));
                }

                // 2) Keep existing keyframes in the safe MIDDLE zone
                if (safeStart < safeEnd)
                {
                    Keyframe[] srcKeys = srcCurve.keys;
                    for (int k = 0; k < srcKeys.Length; k++)
                    {
                        float kt = srcKeys[k].time;
                        if (kt >= safeStart && kt <= safeEnd)
                        {
                            Keyframe kf = srcKeys[k];
                            kf.time -= min;
                            newKeys.Add(kf);
                        }
                    }
                }

                // 3) Resample the END boundary zone [max-buffer, max]
                float endStart = Mathf.Max(safeEnd, min);
                // Avoid re-adding the overlap if start+end buffers overlap
                if (endStart < safeStart) endStart = safeStart;
                for (float t = endStart; t <= max + dt * 0.25f; t += dt)
                {
                    float clamped = Mathf.Min(t, max);
                    newKeys.Add(new Keyframe(clamped - min, srcCurve.Evaluate(clamped)));
                }

                // Build new curve, sort by time to handle any overlap
                newKeys.Sort((a, b) => a.time.CompareTo(b.time));

                // Remove duplicate times (keep last)
                for (int i = newKeys.Count - 1; i > 0; i--)
                {
                    if (Mathf.Approximately(newKeys[i].time, newKeys[i - 1].time))
                        newKeys.RemoveAt(i - 1);
                }

                AnimationCurve newCurve = new AnimationCurve(newKeys.ToArray());

                // Smooth tangents
                for (int i = 0; i < newCurve.keys.Length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(newCurve, i,
                        AnimationUtility.TangentMode.ClampedAuto);
                    AnimationUtility.SetKeyRightTangentMode(newCurve, i,
                        AnimationUtility.TangentMode.ClampedAuto);
                }

                AnimationUtility.SetEditorCurve(clip, binding, newCurve);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Voice Trim
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Trim the voice AudioClip to the normalized [trimMin, trimMax] range.
        /// Overwrites the existing WAV file on disk and reloads it.
        /// </summary>
        private void TrimVoiceClip(float trimMin, float trimMax)
        {
            if (_voiceClip == null) return;

            string assetPath = AssetDatabase.GetAssetPath(_voiceClip);
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogWarning("[DualHandRecorder] Voice clip has no asset path — cannot trim.");
                return;
            }

            int channels = _voiceClip.channels;
            int sampleRate = _voiceClip.frequency;
            int totalSamplesPerChannel = _voiceClip.samples;

            // Read all interleaved samples from the AudioClip
            float[] allSamples = new float[totalSamplesPerChannel * channels];
            _voiceClip.GetData(allSamples, 0);

            // Calculate trim range (per-channel boundaries, then scale to interleaved)
            int startFrame = Mathf.Clamp((int)(trimMin * totalSamplesPerChannel), 0, totalSamplesPerChannel);
            int endFrame = Mathf.Clamp((int)(trimMax * totalSamplesPerChannel), startFrame, totalSamplesPerChannel);
            int startIdx = startFrame * channels;
            int endIdx = endFrame * channels;
            int trimmedCount = endIdx - startIdx;

            if (trimmedCount <= 0)
            {
                Debug.LogWarning("[DualHandRecorder] Trim range is empty — voice clip not modified.");
                return;
            }

            // Extract trimmed samples
            var trimmedSamples = new List<float>(trimmedCount);
            for (int i = startIdx; i < endIdx; i++)
                trimmedSamples.Add(allSamples[i]);

            // Overwrite the WAV file
            WavFileWriter.Write(assetPath, trimmedSamples, sampleRate, channels);
            AssetDatabase.ImportAsset(assetPath);

            // Reload
            _voiceClip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);

            float duration = (float)(endFrame - startFrame) / sampleRate;
            Debug.Log($"[DualHandRecorder] Voice trimmed: {duration:F2}s ({assetPath})");
        }

        // ═══════════════════════════════════════════════════════════════
        // Audio Scrub (editor preview while dragging trim slider)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Play a short preview of the voice clip at the given normalised position
        /// while the trim slider is being dragged. Automatically stops ~0.3s after
        /// the slider stops moving.
        /// </summary>
        private void ScrubVoicePreview(float normalizedTime, bool sliderChanged)
        {
            if (_voiceClip == null)
            {
                if (_isScrubbing) StopVoiceScrub();
                return;
            }

            if (sliderChanged)
            {
                // Start or restart playback from the scrub position
                int samplePos = Mathf.Clamp(
                    (int)(normalizedTime * _voiceClip.samples),
                    0, _voiceClip.samples - 1);

                AudioUtilReflection.StopAllPreviewClips();
                AudioUtilReflection.PlayPreviewClip(_voiceClip, samplePos, false);

                _isScrubbing = true;
                _lastScrubEditorTime = EditorApplication.timeSinceStartup;
                Repaint(); // keep OnGUI firing so we can detect the stop
            }
            else if (_isScrubbing)
            {
                // Slider stopped moving — let audio ring for a moment, then stop
                double elapsed = EditorApplication.timeSinceStartup - _lastScrubEditorTime;
                if (elapsed > 0.3)
                {
                    StopVoiceScrub();
                }
                else
                {
                    Repaint(); // keep ticking until we reach the timeout
                }
            }
        }

        private void StopVoiceScrub()
        {
            if (_isScrubbing)
            {
                AudioUtilReflection.StopAllPreviewClips();
                _isScrubbing = false;
                _lastScrubEditorTime = -1;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Playback Control
        // ═══════════════════════════════════════════════════════════════

        private void StartPlayback()
        {
            _isPlaying = true;
            _lastPlaybackEditorTime = EditorApplication.timeSinceStartup;
            if (_playbackNormalizedTime >= 1f)
                _playbackNormalizedTime = 0f;
        }

        private void StopPlayback()
        {
            _isPlaying = false;
            _lastPlaybackEditorTime = -1;
        }

        /// <summary>
        /// Start playing the voice clip from a normalized position.
        /// Uses AudioSource during Play Mode, AudioUtilReflection in Edit Mode.
        /// </summary>
        private void StartVoicePlayback(float normalizedTime)
        {
            if (_voiceClip == null) return;

            StopVoicePlayback(); // stop any existing playback first

            if (Application.isPlaying)
            {
                // Play Mode: use a runtime AudioSource (reliable)
                EnsureVoiceAudioSource();
                _voiceAudioSource.clip = _voiceClip;
                _voiceAudioSource.time = Mathf.Clamp(
                    normalizedTime * _voiceClip.length,
                    0f, _voiceClip.length - 0.01f);
                _voiceAudioSource.pitch = _playbackSpeed;
                _voiceAudioSource.Play();
            }
            else
            {
                // Edit Mode: use AudioUtil reflection (only option in Edit Mode)
                int samplePos = Mathf.Clamp(
                    (int)(normalizedTime * _voiceClip.samples),
                    0, _voiceClip.samples - 1);
                AudioUtilReflection.StopAllPreviewClips();
                AudioUtilReflection.PlayPreviewClip(_voiceClip, samplePos, false);
            }
        }

        /// <summary>
        /// Stop voice clip playback (both Play Mode and Edit Mode).
        /// </summary>
        private void StopVoicePlayback()
        {
            // Stop runtime AudioSource
            if (_voiceAudioSource != null && _voiceAudioSource.isPlaying)
                _voiceAudioSource.Stop();

            // Stop editor preview
            AudioUtilReflection.StopAllPreviewClips();
        }

        private void EnsureVoiceAudioSource()
        {
            if (_voiceAudioSource != null) return;
            var go = new GameObject("[DualHandRecorder_VoicePreview]");
            go.hideFlags = HideFlags.HideAndDontSave;
            _voiceAudioSource = go.AddComponent<AudioSource>();
            _voiceAudioSource.playOnAwake = false;
            _voiceAudioSource.spatialBlend = 0f; // 2D audio
        }

        private void DestroyVoiceAudioSource()
        {
            if (_voiceAudioSource != null)
            {
                DestroyImmediate(_voiceAudioSource.gameObject);
                _voiceAudioSource = null;
            }
        }

        /// <summary>
        /// Find the nearest keyframe time before (direction = -1) or after (direction = +1)
        /// the current playback position. Scans all curve bindings on both clips.
        /// Returns the keyframe time in seconds, or -1 if none found.
        /// </summary>
        private float FindAdjacentKeyframeTime(float clipLength, int direction)
        {
            float currentTime = _playbackNormalizedTime * clipLength;
            float bestTime = -1f;
            float epsilon = 0.001f; // small tolerance to avoid returning the same keyframe

            AnimationClip[] clips = new AnimationClip[] { _leftClip, _rightClip };
            foreach (var clip in clips)
            {
                if (clip == null) continue;
                var bindings = AnimationUtility.GetCurveBindings(clip);
                // Only scan a subset of bindings to keep it fast on large clips
                int step = Mathf.Max(1, bindings.Length / 20);
                for (int b = 0; b < bindings.Length; b += step)
                {
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, bindings[b]);
                    if (curve == null) continue;
                    Keyframe[] keys = curve.keys;
                    for (int k = 0; k < keys.Length; k++)
                    {
                        float kt = keys[k].time;
                        if (direction > 0 && kt > currentTime + epsilon)
                        {
                            if (bestTime < 0f || kt < bestTime) bestTime = kt;
                        }
                        else if (direction < 0 && kt < currentTime - epsilon)
                        {
                            if (bestTime < 0f || kt > bestTime) bestTime = kt;
                        }
                    }
                }
            }
            return bestTime;
        }

        // ═══════════════════════════════════════════════════════════════
        // Ghost Preview (dual)
        // ═══════════════════════════════════════════════════════════════

        private void UpdateGhosts(float normalizedTime, bool forceUpdate)
        {
            UpdateSingleGhost(ref _leftGhost, _leftClip, _leftHandVisual,
                Handedness.Left, normalizedTime, forceUpdate);
            UpdateSingleGhost(ref _rightGhost, _rightClip, _rightHandVisual,
                Handedness.Right, normalizedTime, forceUpdate);
        }

        private void UpdateSingleGhost(ref HandGhost ghost, AnimationClip clip,
            HandVisual handVisual, Handedness defaultHandedness,
            float normalizedTime, bool forceUpdate)
        {
            if (clip == null)
            {
                DestroyGhost(ref ghost);
                return;
            }

            if (GhostProvider == null)
                return;

            if (forceUpdate || ghost == null)
            {
                Handedness handedness;
                if (!HandAnimationUtils.TryGetClipHandedness(clip, _handLeftPrefix, _handRightPrefix,
                        out handedness))
                {
                    handedness = defaultHandedness;
                    if (handVisual != null && handVisual.Root != null)
                    {
                        handedness = handVisual.Root.name.ToLower().Contains("left")
                            ? Handedness.Left : Handedness.Right;
                    }
                    if (clip.name.Contains("mirror"))
                    {
                        handedness = handedness == Handedness.Left
                            ? Handedness.Right : Handedness.Left;
                    }
                }

                if (ghost == null)
                {
                    HandGhost prototype = GhostProvider.GetHand(handedness);
                    ghost = Instantiate(prototype);
                    ghost.gameObject.hideFlags = HideFlags.HideAndDontSave;
                }
            }

            if (ghost != null)
            {
                float time = normalizedTime * clip.length;
                clip.SampleAnimation(ghost.Root.gameObject, time);
            }
        }

        private void DestroyGhosts()
        {
            DestroyGhost(ref _leftGhost);
            DestroyGhost(ref _rightGhost);
        }

        private static void DestroyGhost(ref HandGhost ghost)
        {
            if (ghost != null)
            {
                DestroyImmediate(ghost.gameObject);
                ghost = null;
            }
        }
    }
}
