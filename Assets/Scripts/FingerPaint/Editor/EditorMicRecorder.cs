/*
 * EditorMicRecorder — editor-safe microphone capture wrapper.
 *
 * Designed for use in EditorWindows (which lack Update/StartCoroutine).
 * Call DrainSamples() from EditorApplication.update to continuously
 * read audio from the microphone ring buffer.
 *
 * Ring-buffer drain pattern adapted from VoiceDetector.ProcessMicrophoneData().
 */

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FingerPaint.Editor
{
    public class EditorMicRecorder : IDisposable
    {
        // ─── Config ────────────────────────────────────────────────
        private readonly int _requestedSampleRate;
        private readonly int _ringBufferSeconds;

        // ─── State ─────────────────────────────────────────────────
        private AudioClip _micClip;
        private string _micDeviceName;
        private int _lastSamplePosition;
        private float[] _readBuffer;
        private List<float> _accumulatedSamples;

        // ─── Public properties ─────────────────────────────────────

        /// <summary>True while the microphone is actively capturing.</summary>
        public bool IsCapturing { get; private set; }

        /// <summary>Actual sample rate used (may differ from requested).</summary>
        public int EffectiveSampleRate { get; private set; }

        /// <summary>Number of samples accumulated so far.</summary>
        public int SampleCount => _accumulatedSamples?.Count ?? 0;

        // ─── Constructor ───────────────────────────────────────────

        /// <param name="sampleRate">Desired sample rate (Hz). Default 44100.</param>
        /// <param name="ringBufferSeconds">
        /// Rolling AudioClip length in seconds. Longer = more margin if
        /// EditorApplication.update fires slowly. Default 10.
        /// </param>
        public EditorMicRecorder(int sampleRate = 44100, int ringBufferSeconds = 10)
        {
            _requestedSampleRate = sampleRate;
            _ringBufferSeconds = ringBufferSeconds;
        }

        // ─── Public API ────────────────────────────────────────────

        /// <summary>
        /// Start microphone capture. Must be called during Play mode.
        /// Returns false if no microphone device is available.
        /// </summary>
        /// <param name="deviceName">
        /// Name of the microphone device to use (from Microphone.devices).
        /// Pass null or empty string to use the first available device.
        /// </param>
        public bool StartCapture(string deviceName = null)
        {
            if (IsCapturing) return true;

            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning("[EditorMicRecorder] No microphone devices found.");
                return false;
            }

            // Use specified device or fall back to first available
            if (string.IsNullOrEmpty(deviceName))
            {
                _micDeviceName = Microphone.devices[0];
            }
            else
            {
                // Validate the device name exists
                bool found = false;
                foreach (var dev in Microphone.devices)
                {
                    if (dev == deviceName) { found = true; break; }
                }
                if (!found)
                {
                    Debug.LogWarning($"[EditorMicRecorder] Device \"{deviceName}\" not found. Falling back to \"{Microphone.devices[0]}\".");
                    _micDeviceName = Microphone.devices[0];
                }
                else
                {
                    _micDeviceName = deviceName;
                }
            }

            // Query device capabilities
            Microphone.GetDeviceCaps(_micDeviceName, out int minFreq, out int maxFreq);
            if (minFreq == 0 && maxFreq == 0)
            {
                // Device supports any rate
                EffectiveSampleRate = _requestedSampleRate;
            }
            else
            {
                EffectiveSampleRate = Mathf.Clamp(_requestedSampleRate, minFreq, maxFreq);
            }

            // Start recording into a looping ring buffer
            _micClip = Microphone.Start(_micDeviceName, true, _ringBufferSeconds, EffectiveSampleRate);
            if (_micClip == null)
            {
                Debug.LogWarning($"[EditorMicRecorder] Microphone.Start failed for \"{_micDeviceName}\".");
                return false;
            }

            // Pre-allocate read buffer for ~100ms chunks
            int samplesPerChunk = Mathf.Max(1, EffectiveSampleRate / 10);
            _readBuffer = new float[samplesPerChunk];
            _accumulatedSamples = new List<float>();
            _lastSamplePosition = 0;

            IsCapturing = true;

            Debug.Log($"[EditorMicRecorder] Capture started: \"{_micDeviceName}\", " +
                      $"{EffectiveSampleRate} Hz, {_ringBufferSeconds}s ring buffer");
            return true;
        }

        /// <summary>
        /// Drain new samples from the microphone ring buffer into the
        /// accumulation list. Call this from EditorApplication.update.
        /// </summary>
        public void DrainSamples()
        {
            if (!IsCapturing || _micClip == null) return;

            int currentPosition = Microphone.GetPosition(_micDeviceName);
            if (currentPosition < 0) return;          // mic not ready
            if (currentPosition == _lastSamplePosition) return; // no new data

            // Ring-buffer-aware sample count
            int totalSamples = _micClip.samples;
            int samplesToRead = currentPosition > _lastSamplePosition
                ? currentPosition - _lastSamplePosition
                : (totalSamples - _lastSamplePosition) + currentPosition;

            if (samplesToRead <= 0) return;

            // Resize read buffer if needed (e.g., first call after a stall)
            if (_readBuffer.Length < samplesToRead)
                _readBuffer = new float[samplesToRead];

            // Handle ring-buffer wrap-around
            if (currentPosition > _lastSamplePosition)
            {
                // Simple case — no wrap
                _micClip.GetData(_readBuffer, _lastSamplePosition);
                for (int i = 0; i < samplesToRead; i++)
                    _accumulatedSamples.Add(_readBuffer[i]);
            }
            else
            {
                // Wrapped around — read in two parts
                int firstPart = totalSamples - _lastSamplePosition;
                int secondPart = currentPosition;

                if (firstPart > 0)
                {
                    float[] buf1 = new float[firstPart];
                    _micClip.GetData(buf1, _lastSamplePosition);
                    for (int i = 0; i < firstPart; i++)
                        _accumulatedSamples.Add(buf1[i]);
                }

                if (secondPart > 0)
                {
                    float[] buf2 = new float[secondPart];
                    _micClip.GetData(buf2, 0);
                    for (int i = 0; i < secondPart; i++)
                        _accumulatedSamples.Add(buf2[i]);
                }
            }

            _lastSamplePosition = currentPosition;
        }

        /// <summary>
        /// Stop capture and return all accumulated samples.
        /// The caller owns the returned list.
        /// </summary>
        public List<float> StopCaptureAndGetSamples()
        {
            if (!IsCapturing)
                return _accumulatedSamples ?? new List<float>();

            // One final drain
            DrainSamples();

            // Stop microphone
            Microphone.End(_micDeviceName);
            if (_micClip != null)
            {
                UnityEngine.Object.DestroyImmediate(_micClip);
                _micClip = null;
            }

            IsCapturing = false;

            var result = _accumulatedSamples;
            _accumulatedSamples = null;
            return result ?? new List<float>();
        }

        // ─── IDisposable ───────────────────────────────────────────

        public void Dispose()
        {
            if (IsCapturing)
            {
                Microphone.End(_micDeviceName);
                IsCapturing = false;
            }

            if (_micClip != null)
            {
                UnityEngine.Object.DestroyImmediate(_micClip);
                _micClip = null;
            }

            _accumulatedSamples = null;
        }
    }
}
