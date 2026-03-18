using System.Collections;
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
    [DefaultExecutionOrder(-30)]
    public class VoiceDetector : MonoBehaviour
    {
        // ─── Public read-only state ──────────────────────────────────────

        /// <summary>True when smoothed volume exceeds the activation threshold.</summary>
        public bool IsActive { get; private set; }

        /// <summary>Smoothed normalised volume in [0, 1].</summary>
        public float NormalizedVolume { get; private set; }

        /// <summary>Un-smoothed raw RMS volume from the latest audio chunk.</summary>
        public float RawVolume { get; private set; }

        /// <summary>True once the microphone is capturing successfully.</summary>
        public bool IsMicrophoneAvailable { get; private set; }

        /// <summary>True once the RECORD_AUDIO permission has been granted.</summary>
        public bool IsPermissionGranted => _permissionGranted;

        /// <summary>Name of the active microphone device, or null.</summary>
        public string MicDeviceName => _micDeviceName;

        /// <summary>Number of microphone devices detected.</summary>
        public int MicDeviceCount { get; private set; }

        /// <summary>Current activation threshold (for debug display).</summary>
        public float ActivationThreshold => _activationThreshold;

        /// <summary>Current deactivation threshold (for debug display).</summary>
        public float DeactivationThreshold => _deactivationThreshold;

        /// <summary>The configured sample rate in Hz. Used by VoiceAnalyzer.</summary>
        public int SampleRate => _sampleRate;

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

        private bool _permissionGranted;
        private bool _permissionRequested;
        private int  _lastReadCount;
        private Coroutine _startCoroutine;
        private int _retryCount;

        // ─── Lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            MicDeviceCount = Microphone.devices.Length;
            Debug.Log($"[VoiceDetector] Awake — {MicDeviceCount} mic device(s) found.");
            for (int i = 0; i < MicDeviceCount; i++)
                Debug.Log($"[VoiceDetector]   [{i}] {Microphone.devices[i]}");

#if UNITY_EDITOR
            _permissionGranted = true;
            Debug.Log("[VoiceDetector] Running in Editor — permission auto-granted.");
#elif UNITY_ANDROID
            _permissionGranted = Permission.HasUserAuthorizedPermission("android.permission.RECORD_AUDIO");
            Debug.Log($"[VoiceDetector] Android RECORD_AUDIO permission: {_permissionGranted}");
#else
            _permissionGranted = true;
#endif
        }

        private void Start()
        {
            if (_permissionGranted)
            {
                StartMicrophone();
            }
            else
            {
                Debug.Log("[VoiceDetector] Permission not yet granted — requesting...");
                RequestMicrophonePermission();
            }
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
            {
                Debug.Log("[VoiceDetector] App regained focus — restarting microphone.");
                _retryCount = 0;
                StartMicrophone();
            }
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
            if (_startCoroutine != null)
                StopCoroutine(_startCoroutine);

            _startCoroutine = StartCoroutine(StartMicrophoneCoroutine());
        }

        private IEnumerator StartMicrophoneCoroutine()
        {
            MicDeviceCount = Microphone.devices.Length;
            if (MicDeviceCount == 0)
            {
                Debug.LogWarning("[VoiceDetector] No microphone device found.");
                IsMicrophoneAvailable = false;
                yield break;
            }

            // Log all devices on first attempt so we can diagnose
            if (_retryCount == 0)
            {
                for (int i = 0; i < MicDeviceCount; i++)
                {
                    Microphone.GetDeviceCaps(Microphone.devices[i], out int dMin, out int dMax);
                    Debug.Log($"[VoiceDetector]   [{i}] \"{Microphone.devices[i]}\" caps: {dMin}–{dMax} Hz");
                }
            }

            // ── Build a list of (deviceName, sampleRate) combos to try ──
            // On Android/Quest, null = system default mic (most reliable)
            var attempts = new System.Collections.Generic.List<(string device, int rate)>();

#if UNITY_ANDROID && !UNITY_EDITOR
            // Quest: try null (system default) first, at both configured and 48kHz
            attempts.Add((null, _sampleRate));
            if (_sampleRate != 48000)
                attempts.Add((null, 48000));
#endif
            // Then try each named device at configured rate
            for (int i = 0; i < MicDeviceCount; i++)
                attempts.Add((Microphone.devices[i], _sampleRate));

            // Then try each named device at 48kHz (Quest often needs this)
            if (_sampleRate != 48000)
            {
                for (int i = 0; i < MicDeviceCount; i++)
                    attempts.Add((Microphone.devices[i], 48000));
            }

            // Skip already-tried attempts
            int startIdx = Mathf.Clamp(_retryCount, 0, attempts.Count - 1);

            for (int attempt = startIdx; attempt < attempts.Count; attempt++)
            {
                _retryCount = attempt;
                var (deviceName, targetRate) = attempts[attempt];

                string displayName = deviceName ?? "(null/system default)";
                Debug.Log($"[VoiceDetector] Attempt [{attempt}]: device=\"{displayName}\" @ {targetRate} Hz");

                // Query supported frequency range
                Microphone.GetDeviceCaps(deviceName, out int minFreq, out int maxFreq);

                int effectiveRate = targetRate;
                if (minFreq != 0 || maxFreq != 0)
                {
                    effectiveRate = Mathf.Clamp(targetRate, minFreq, maxFreq);
                    if (effectiveRate != targetRate)
                        Debug.Log($"[VoiceDetector] Clamped rate {targetRate} → {effectiveRate} Hz");
                }

                // Stop any previous recording
                for (int i = 0; i < MicDeviceCount; i++)
                {
                    if (Microphone.IsRecording(Microphone.devices[i]))
                        Microphone.End(Microphone.devices[i]);
                }
                if (Microphone.IsRecording(null))
                    Microphone.End(null);

                _micDeviceName = deviceName;
                _micClip = Microphone.Start(deviceName, loop: true, _clipLengthSeconds, effectiveRate);

                if (_micClip == null)
                {
                    Debug.LogWarning($"[VoiceDetector] Microphone.Start returned null for \"{displayName}\"");
                    continue;
                }

                // Wait for mic to start producing samples (up to 3 seconds for Quest)
                float waitTime = 0f;
                while (Microphone.GetPosition(deviceName) <= 0 && waitTime < 3f)
                {
                    waitTime += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (Microphone.GetPosition(deviceName) <= 0)
                {
                    Debug.LogWarning($"[VoiceDetector] Mic \"{displayName}\" produced no samples after {waitTime:F1}s");
                    Microphone.End(deviceName);
                    if (_micClip != null) { Destroy(_micClip); _micClip = null; }
                    yield return new WaitForSeconds(0.3f);
                    continue;
                }

                // Pre-allocate a buffer for ~100 ms of audio
                int samplesPerChunk = Mathf.Max(1, effectiveRate / 10);
                _sampleBuffer = new float[samplesPerChunk];
                _lastSamplePosition = Microphone.GetPosition(deviceName);

                // Verify actual audio data is non-zero (wait up to 2 seconds)
                float verifyTime = 0f;
                bool hasRealData = false;
                while (verifyTime < 2f)
                {
                    verifyTime += Time.unscaledDeltaTime;
                    yield return null;

                    int pos = Microphone.GetPosition(deviceName);
                    if (pos != _lastSamplePosition && pos > 0)
                    {
                        int checkCount = Mathf.Min(256, _sampleBuffer.Length);
                        float[] checkBuf = new float[checkCount];
                        int readPos = pos - checkCount;
                        if (readPos < 0) readPos += _micClip.samples;
                        _micClip.GetData(checkBuf, readPos);

                        float maxSample = 0f;
                        for (int i = 0; i < checkCount; i++)
                            maxSample = Mathf.Max(maxSample, Mathf.Abs(checkBuf[i]));

                        if (maxSample > 0.0001f)
                        {
                            hasRealData = true;
                            Debug.Log($"[VoiceDetector] Verified real audio (peak: {maxSample:F6}) on \"{displayName}\" @ {effectiveRate} Hz");
                            break;
                        }
                    }
                }

                if (hasRealData)
                {
                    // Success!
                    _retryCount = 0;
                    _lastSamplePosition = Microphone.GetPosition(deviceName);
                    IsMicrophoneAvailable = true;
                    Debug.Log($"[VoiceDetector] Microphone ready: \"{displayName}\", " +
                              $"clip: {_micClip.samples} samples, {_micClip.channels} ch, " +
                              $"rate: {effectiveRate} Hz, position: {_lastSamplePosition}");
                    yield break;
                }
                else
                {
                    Debug.LogWarning($"[VoiceDetector] Mic \"{displayName}\" @ {effectiveRate} Hz produces only silence.");
                    Microphone.End(deviceName);
                    if (_micClip != null) { Destroy(_micClip); _micClip = null; }
                    yield return new WaitForSeconds(0.3f);
                }
            }

            // All attempts exhausted — use first device as best effort
            Debug.LogWarning("[VoiceDetector] All mic attempts failed. Using first device as best effort.");
            _micDeviceName = Microphone.devices[0];

#if UNITY_ANDROID && !UNITY_EDITOR
            // On Quest, try null as last resort
            _micDeviceName = null;
#endif
            _micClip = Microphone.Start(_micDeviceName, loop: true, _clipLengthSeconds, 48000);
            yield return null;

            int fallbackChunk = Mathf.Max(1, 48000 / 10);
            _sampleBuffer = new float[fallbackChunk];
            _lastSamplePosition = Microphone.GetPosition(_micDeviceName);

            _retryCount = 0;
            IsMicrophoneAvailable = true;
            Debug.Log($"[VoiceDetector] Best-effort mic ready: \"{_micDeviceName ?? "(null)"}\" @ 48000 Hz");
        }

        private void StopMicrophone()
        {
            if (_startCoroutine != null)
            {
                StopCoroutine(_startCoroutine);
                _startCoroutine = null;
            }

            if (Microphone.IsRecording(_micDeviceName))
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
            if (currentPosition < 0) return; // mic not ready
            if (currentPosition == _lastSamplePosition) return;

            // How many new samples are available (ring-buffer aware)
            int totalSamples = _micClip.samples;
            int samplesToRead = currentPosition > _lastSamplePosition
                ? currentPosition - _lastSamplePosition
                : (totalSamples - _lastSamplePosition) + currentPosition;

            if (samplesToRead <= 0) return;

            // Clamp to the pre-allocated buffer size (read most recent chunk)
            int readOffset;
            if (samplesToRead > _sampleBuffer.Length)
            {
                readOffset = currentPosition - _sampleBuffer.Length;
                if (readOffset < 0) readOffset += totalSamples;
                samplesToRead = _sampleBuffer.Length;
            }
            else
            {
                readOffset = _lastSamplePosition;
            }

            _micClip.GetData(_sampleBuffer, readOffset);
            _lastSamplePosition = currentPosition;

            // RMS (root-mean-square) for volume
            float sumSquares = 0f;
            for (int i = 0; i < samplesToRead; i++)
            {
                float s = _sampleBuffer[i];
                sumSquares += s * s;
            }

            RawVolume = Mathf.Sqrt(sumSquares / samplesToRead);
            _lastReadCount = samplesToRead;
        }

        /// <summary>
        /// Provides read access to the most recent mic samples for spectral analysis.
        /// The returned array is owned by VoiceDetector — do NOT hold a reference.
        /// </summary>
        /// <param name="buffer">The internal sample buffer (read-only).</param>
        /// <param name="sampleCount">Number of valid samples in the buffer.</param>
        /// <returns>True if valid data is available.</returns>
        public bool GetAnalysisBuffer(out float[] buffer, out int sampleCount)
        {
            if (_sampleBuffer == null || !IsMicrophoneAvailable)
            {
                buffer = null;
                sampleCount = 0;
                return false;
            }

            buffer = _sampleBuffer;
            sampleCount = _lastReadCount;
            return sampleCount > 0;
        }

        private void UpdateSmoothedVolume()
        {
            // Exponential moving average
            NormalizedVolume = Mathf.Lerp(NormalizedVolume, RawVolume, _smoothingFactor);
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

            Debug.Log("[VoiceDetector] Requesting android.permission.RECORD_AUDIO...");

            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += perm =>
            {
                Debug.Log($"[VoiceDetector] Permission granted: {perm}");
                _permissionGranted = true;
                StartMicrophone();
            };
            callbacks.PermissionDenied += perm =>
            {
                Debug.LogWarning($"[VoiceDetector] Permission denied: {perm}");
                IsMicrophoneAvailable = false;
            };
            callbacks.PermissionDeniedAndDontAskAgain += perm =>
            {
                Debug.LogWarning($"[VoiceDetector] Permission permanently denied: {perm}");
                IsMicrophoneAvailable = false;
            };

            Permission.RequestUserPermission("android.permission.RECORD_AUDIO", callbacks);
#else
            // Non-Android: no permission needed, just start
            _permissionGranted = true;
            StartMicrophone();
#endif
        }
    }
}
