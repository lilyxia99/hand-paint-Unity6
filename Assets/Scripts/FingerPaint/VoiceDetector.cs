using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace FingerPaint
{
    /// <summary>
    /// Detects voice/mouth sounds via the device microphone.
    /// Exposes a smoothed volume level and an active state that other scripts
    /// (e.g. FingerPainter) can use as a drawing trigger.
    /// Louder voice → larger size multiplier for paint spheres.
    /// </summary>
    public class VoiceDetector : MonoBehaviour
    {
        // ─── Public read-only state ──────────────────────────────────────

        /// <summary>True when smoothed volume exceeds the activation threshold.</summary>
        public bool IsActive { get; private set; }

        /// <summary>Smoothed normalised volume in [0, 1].</summary>
        public float NormalizedVolume { get; private set; }

        /// <summary>True once the microphone is capturing successfully.</summary>
        public bool IsMicrophoneAvailable { get; private set; }

        // ─── Configuration ───────────────────────────────────────────────

        [Header("Microphone")]
        [Tooltip("Sample rate in Hz. 16 kHz is sufficient for voice detection.")]
        [SerializeField] private int _sampleRate = 16000;

        [Tooltip("Length of the rolling AudioClip ring buffer in seconds.")]
        [SerializeField] private int _clipLengthSeconds = 1;

        [Header("Detection")]
        [Tooltip("Volume must exceed this to start drawing.")]
        [SerializeField] [Range(0f, 1f)] private float _activationThreshold = 0.02f;

        [Tooltip("Volume must drop below this to stop drawing (hysteresis).")]
        [SerializeField] [Range(0f, 1f)] private float _deactivationThreshold = 0.01f;

        [Tooltip("Smoothing factor for the exponential moving average (0 = no change, 1 = instant).")]
        [SerializeField] [Range(0.01f, 1f)] private float _smoothingFactor = 0.15f;

        [Header("Size Mapping")]
        [Tooltip("Sphere size multiplier at the activation threshold volume.")]
        [SerializeField] private float _minSizeMultiplier = 0.5f;

        [Tooltip("Sphere size multiplier at maximum volume.")]
        [SerializeField] private float _maxSizeMultiplier = 3.0f;

        [Tooltip("Curve that maps normalised volume (0–1) to the size lerp factor.")]
        [SerializeField] private AnimationCurve _volumeToSizeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        // ─── Private state ───────────────────────────────────────────────

        private AudioClip _micClip;
        private string _micDeviceName;
        private int _lastSamplePosition;
        private float[] _sampleBuffer;
        private float _rawVolume;

        private bool _permissionGranted;
        private bool _permissionRequested;

        // ─── Lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
#if UNITY_EDITOR
            _permissionGranted = true;
#elif UNITY_ANDROID
            _permissionGranted = Permission.HasUserAuthorizedPermission("android.permission.RECORD_AUDIO");
#else
            _permissionGranted = true;
#endif
        }

        private void Start()
        {
            if (_permissionGranted)
                StartMicrophone();
            else
                RequestMicrophonePermission();
        }

        private void Update()
        {
            if (!_permissionGranted || !IsMicrophoneAvailable)
                return;

            // Guard: mic may stop if the app was backgrounded
            if (!Microphone.IsRecording(_micDeviceName))
            {
                IsMicrophoneAvailable = false;
                Debug.LogWarning("[VoiceDetector] Microphone stopped unexpectedly.");
                return;
            }

            ProcessMicrophoneData();
            UpdateSmoothedVolume();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && _permissionGranted && !IsMicrophoneAvailable)
                StartMicrophone();
        }

        private void OnDisable() => StopMicrophone();
        private void OnDestroy() => StopMicrophone();

        // ─── Public API ──────────────────────────────────────────────────

        /// <summary>
        /// Returns a sphere size multiplier based on current voice volume.
        /// Mapped through <see cref="_volumeToSizeCurve"/> from
        /// [<see cref="_minSizeMultiplier"/>, <see cref="_maxSizeMultiplier"/>].
        /// Returns 1.0 when voice detection is not active.
        /// </summary>
        public float GetSizeMultiplier()
        {
            if (!IsActive)
                return 1f;

            // Map volume from [threshold … 1] → [0 … 1]
            float t = Mathf.InverseLerp(_activationThreshold, 1f, NormalizedVolume);
            float curved = _volumeToSizeCurve.Evaluate(t);
            return Mathf.Lerp(_minSizeMultiplier, _maxSizeMultiplier, curved);
        }

        // ─── Microphone management ───────────────────────────────────────

        private void StartMicrophone()
        {
            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning("[VoiceDetector] No microphone device found.");
                IsMicrophoneAvailable = false;
                return;
            }

            _micDeviceName = Microphone.devices[0];
            _micClip = Microphone.Start(_micDeviceName, loop: true, _clipLengthSeconds, _sampleRate);

            // Pre-allocate a buffer for ~100 ms of audio
            int samplesPerChunk = Mathf.Max(1, _sampleRate / 10);
            _sampleBuffer = new float[samplesPerChunk];
            _lastSamplePosition = 0;

            IsMicrophoneAvailable = true;
            Debug.Log($"[VoiceDetector] Microphone started: {_micDeviceName} @ {_sampleRate} Hz");
        }

        private void StopMicrophone()
        {
            if (_micDeviceName != null && Microphone.IsRecording(_micDeviceName))
                Microphone.End(_micDeviceName);

            if (_micClip != null)
            {
                Destroy(_micClip);
                _micClip = null;
            }

            IsMicrophoneAvailable = false;
        }

        // ─── Audio processing ────────────────────────────────────────────

        private void ProcessMicrophoneData()
        {
            if (_micClip == null) return;

            int currentPosition = Microphone.GetPosition(_micDeviceName);
            if (currentPosition == _lastSamplePosition) return;

            // How many new samples are available (ring-buffer aware)
            int totalSamples = _micClip.samples;
            int samplesToRead = currentPosition > _lastSamplePosition
                ? currentPosition - _lastSamplePosition
                : (totalSamples - _lastSamplePosition) + currentPosition;

            // Clamp to the pre-allocated buffer size (read most recent chunk)
            if (samplesToRead > _sampleBuffer.Length)
            {
                int readStart = currentPosition - _sampleBuffer.Length;
                if (readStart < 0) readStart += totalSamples;
                _micClip.GetData(_sampleBuffer, readStart);
                samplesToRead = _sampleBuffer.Length;
            }
            else
            {
                _micClip.GetData(_sampleBuffer, _lastSamplePosition);
            }

            _lastSamplePosition = currentPosition;

            // RMS (root-mean-square) for volume
            float sumSquares = 0f;
            for (int i = 0; i < samplesToRead; i++)
            {
                float s = _sampleBuffer[i];
                sumSquares += s * s;
            }

            _rawVolume = Mathf.Sqrt(sumSquares / samplesToRead);
        }

        private void UpdateSmoothedVolume()
        {
            // Exponential moving average
            NormalizedVolume = Mathf.Lerp(NormalizedVolume, _rawVolume, _smoothingFactor);
            NormalizedVolume = Mathf.Clamp01(NormalizedVolume);

            // Hysteresis to avoid rapid on/off flickering
            if (!IsActive && NormalizedVolume >= _activationThreshold)
                IsActive = true;
            else if (IsActive && NormalizedVolume < _deactivationThreshold)
                IsActive = false;
        }

        // ─── Android permissions ─────────────────────────────────────────

        private void RequestMicrophonePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_permissionRequested) return;
            _permissionRequested = true;

            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ =>
            {
                Debug.Log("[VoiceDetector] Microphone permission granted.");
                _permissionGranted = true;
                StartMicrophone();
            };
            callbacks.PermissionDenied += _ =>
            {
                Debug.LogWarning("[VoiceDetector] Microphone permission denied.");
                IsMicrophoneAvailable = false;
            };
            callbacks.PermissionDeniedAndDontAskAgain += _ =>
            {
                Debug.LogWarning("[VoiceDetector] Microphone permission permanently denied.");
                IsMicrophoneAvailable = false;
            };

            Permission.RequestUserPermission("android.permission.RECORD_AUDIO", callbacks);
#endif
        }
    }
}
