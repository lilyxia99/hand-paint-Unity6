using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Spectral analysis companion to <see cref="VoiceDetector"/>.
    /// Shares the same microphone AudioClip. Produces pitch, brightness,
    /// and noisiness each frame using lightweight FFT + autocorrelation.
    /// Suitable for Quest 3 at 72 Hz.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public class VoiceAnalyzer : MonoBehaviour
    {
        // ─── Configuration ──────────────────────────────────────────────

        [Header("References")]
        [SerializeField] private VoiceDetector _voiceDetector;

        [Header("FFT Settings")]
        [Tooltip("FFT window size (must be power of 2). 1024 at 16 kHz = 64 ms.")]
        [SerializeField] private int _fftSize = 1024;

        [Header("Pitch Range")]
        [Tooltip("Lowest detectable pitch (Hz). Typical male voice ~80 Hz.")]
        [SerializeField, Range(50f, 300f)]  private float _pitchMinHz = 80f;

        [Tooltip("Highest detectable pitch (Hz). Typical female voice ~600 Hz.")]
        [SerializeField, Range(200f, 1200f)] private float _pitchMaxHz = 600f;

        [Header("Smoothing")]
        [SerializeField, Range(0.01f, 1f)] private float _smoothingFactor = 0.2f;

        [Header("Performance")]
        [Tooltip("Run analysis every N frames (1 = every frame, 2 = every other).")]
        [SerializeField, Range(1, 4)] private int _analyzeEveryNFrames = 1;

        // ─── Public state ───────────────────────────────────────────────

        /// <summary>Latest voice analysis features, all normalized [0, 1].</summary>
        public VoiceFeatures Features { get; private set; }

        // ─── Private buffers (all pre-allocated) ────────────────────────

        private float[] _windowedSamples;  // time-domain after Hann window
        private float[] _fftReal;          // real part (in-place FFT)
        private float[] _fftImag;          // imaginary part
        private float[] _magnitude;        // |FFT|, length = fftSize/2
        private float[] _hannWindow;       // pre-computed window coefficients
        private int[]   _bitRevTable;      // bit-reversal indices
        private float[] _twiddleReal;      // pre-computed cos table
        private float[] _twiddleImag;      // pre-computed sin table

        // Smoothed outputs
        private float _smoothedPitch;
        private float _smoothedBrightness;
        private float _smoothedNoisiness;

        private int _frameCounter;

        // ─── Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            // Ensure power-of-two
            _fftSize = Mathf.ClosestPowerOfTwo(_fftSize);
            if (_fftSize < 64) _fftSize = 64;

            int halfN = _fftSize / 2;

            _windowedSamples = new float[_fftSize];
            _fftReal         = new float[_fftSize];
            _fftImag         = new float[_fftSize];
            _magnitude       = new float[halfN];
            _hannWindow      = new float[_fftSize];

            // Pre-compute Hann window
            for (int i = 0; i < _fftSize; i++)
                _hannWindow[i] = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * i / (_fftSize - 1)));

            // Pre-compute bit-reversal table
            _bitRevTable = BuildBitReversalTable(_fftSize);

            // Pre-compute twiddle factors for each FFT stage
            int logN = (int)Mathf.Log(_fftSize, 2);
            _twiddleReal = new float[halfN];
            _twiddleImag = new float[halfN];
            for (int i = 0; i < halfN; i++)
            {
                float angle = -2f * Mathf.PI * i / _fftSize;
                _twiddleReal[i] = Mathf.Cos(angle);
                _twiddleImag[i] = Mathf.Sin(angle);
            }
        }

        private void Update()
        {
            _frameCounter++;
            if (_frameCounter % _analyzeEveryNFrames != 0)
                return;

            if (_voiceDetector == null || !_voiceDetector.IsMicrophoneAvailable)
            {
                SetInactive();
                return;
            }

            if (!_voiceDetector.GetAnalysisBuffer(out float[] buffer, out int sampleCount))
            {
                SetInactive();
                return;
            }

            if (!_voiceDetector.IsActive)
            {
                // Decay smoothed values toward 0 when voice is off
                float decay = _smoothingFactor * 0.5f;
                _smoothedPitch      = Mathf.Lerp(_smoothedPitch,      0f, decay);
                _smoothedBrightness = Mathf.Lerp(_smoothedBrightness, 0f, decay);
                _smoothedNoisiness  = Mathf.Lerp(_smoothedNoisiness,  0f, decay);

                Features = new VoiceFeatures
                {
                    Loudness   = _voiceDetector.NormalizedVolume,
                    Pitch      = _smoothedPitch,
                    Brightness = _smoothedBrightness,
                    Noisiness  = _smoothedNoisiness,
                    IsActive   = false
                };
                return;
            }

            // ── Fill analysis window ────────────────────────────────────
            FillWindowedBuffer(buffer, sampleCount);

            // ── FFT ─────────────────────────────────────────────────────
            ComputeFFT();

            // ── Extract features ────────────────────────────────────────
            ComputeMagnitudeSpectrum();

            float rawPitch      = DetectPitch(buffer, sampleCount);
            float rawBrightness = ComputeSpectralCentroid();
            float rawNoisiness  = ComputeSpectralFlatness();

            // ── Smooth ──────────────────────────────────────────────────
            _smoothedPitch      = Mathf.Lerp(_smoothedPitch,      rawPitch,      _smoothingFactor);
            _smoothedBrightness = Mathf.Lerp(_smoothedBrightness, rawBrightness, _smoothingFactor);
            _smoothedNoisiness  = Mathf.Lerp(_smoothedNoisiness,  rawNoisiness,  _smoothingFactor);

            Features = new VoiceFeatures
            {
                Loudness   = _voiceDetector.NormalizedVolume,
                Pitch      = Mathf.Clamp01(_smoothedPitch),
                Brightness = Mathf.Clamp01(_smoothedBrightness),
                Noisiness  = Mathf.Clamp01(_smoothedNoisiness),
                IsActive   = true
            };
        }

        // ─── Window & FFT ───────────────────────────────────────────────

        private void FillWindowedBuffer(float[] source, int sourceCount)
        {
            // Copy source into the analysis window, zero-pad if needed
            int copyLen = Mathf.Min(sourceCount, _fftSize);
            int startOffset = sourceCount > _fftSize ? sourceCount - _fftSize : 0;

            for (int i = 0; i < _fftSize; i++)
            {
                if (i < copyLen)
                    _windowedSamples[i] = source[startOffset + i] * _hannWindow[i];
                else
                    _windowedSamples[i] = 0f;
            }
        }

        /// <summary>In-place radix-2 Cooley-Tukey FFT.</summary>
        private void ComputeFFT()
        {
            int N = _fftSize;

            // Bit-reversal permutation
            for (int i = 0; i < N; i++)
            {
                _fftReal[i] = _windowedSamples[_bitRevTable[i]];
                _fftImag[i] = 0f;
            }

            // Butterfly passes
            int logN = 0;
            for (int temp = N; temp > 1; temp >>= 1) logN++;

            for (int stage = 0; stage < logN; stage++)
            {
                int blockSize = 1 << (stage + 1);
                int halfBlock = blockSize >> 1;
                int twiddleStep = N >> (stage + 1);

                for (int block = 0; block < N; block += blockSize)
                {
                    for (int j = 0; j < halfBlock; j++)
                    {
                        int twIdx = j * twiddleStep;
                        float wr = _twiddleReal[twIdx];
                        float wi = _twiddleImag[twIdx];

                        int upper = block + j;
                        int lower = upper + halfBlock;

                        float tReal = wr * _fftReal[lower] - wi * _fftImag[lower];
                        float tImag = wr * _fftImag[lower] + wi * _fftReal[lower];

                        _fftReal[lower] = _fftReal[upper] - tReal;
                        _fftImag[lower] = _fftImag[upper] - tImag;
                        _fftReal[upper] += tReal;
                        _fftImag[upper] += tImag;
                    }
                }
            }
        }

        private void ComputeMagnitudeSpectrum()
        {
            int halfN = _fftSize / 2;
            for (int i = 0; i < halfN; i++)
            {
                float re = _fftReal[i];
                float im = _fftImag[i];
                _magnitude[i] = Mathf.Sqrt(re * re + im * im);
            }
        }

        // ─── Feature Extraction ─────────────────────────────────────────

        /// <summary>
        /// Autocorrelation-based pitch detection on time-domain signal.
        /// More robust than FFT peak-picking for monophonic voice.
        /// Returns normalized pitch [0, 1] within configured Hz range.
        /// </summary>
        private float DetectPitch(float[] samples, int sampleCount)
        {
            int sampleRate = _voiceDetector.SampleRate;

            // Lag range corresponding to pitch Hz range
            int minLag = Mathf.Max(1, sampleRate / (int)_pitchMaxHz);
            int maxLag = Mathf.Min(sampleCount - 1, sampleRate / (int)_pitchMinHz);

            if (maxLag <= minLag || sampleCount < maxLag + 1)
                return _smoothedPitch; // keep previous value

            // Compute energy for normalization
            float energy = 0f;
            int len = Mathf.Min(sampleCount, _fftSize);
            for (int i = 0; i < len; i++)
                energy += samples[i] * samples[i];

            if (energy < 1e-8f)
                return 0f; // silence

            // Find autocorrelation peak
            float bestCorr = -1f;
            int bestLag = minLag;

            for (int lag = minLag; lag <= maxLag; lag++)
            {
                float sum = 0f;
                int limit = Mathf.Min(len, sampleCount - lag);
                for (int i = 0; i < limit; i++)
                    sum += samples[i] * samples[i + lag];

                float normalized = sum / energy;

                if (normalized > bestCorr)
                {
                    bestCorr = normalized;
                    bestLag = lag;
                }
            }

            // Require a decent correlation peak to trust the pitch
            if (bestCorr < 0.2f)
                return _smoothedPitch * 0.9f; // decay toward zero

            // Parabolic interpolation around peak for sub-sample accuracy
            float refinedLag = bestLag;
            if (bestLag > minLag && bestLag < maxLag)
            {
                float corrPrev = AutocorrAtLag(samples, len, bestLag - 1, energy);
                float corrNext = AutocorrAtLag(samples, len, bestLag + 1, energy);
                float denom = 2f * (2f * bestCorr - corrPrev - corrNext);
                if (Mathf.Abs(denom) > 1e-6f)
                    refinedLag = bestLag + (corrPrev - corrNext) / denom;
            }

            float pitchHz = sampleRate / refinedLag;
            return Mathf.InverseLerp(_pitchMinHz, _pitchMaxHz, pitchHz);
        }

        private static float AutocorrAtLag(float[] samples, int len, int lag, float energy)
        {
            float sum = 0f;
            int limit = Mathf.Min(len, samples.Length - lag);
            for (int i = 0; i < limit; i++)
                sum += samples[i] * samples[i + lag];
            return energy > 0 ? sum / energy : 0f;
        }

        /// <summary>
        /// Spectral centroid: "center of mass" of the frequency spectrum.
        /// Returns [0, 1] where 0 = low/dark, 1 = high/bright.
        /// </summary>
        private float ComputeSpectralCentroid()
        {
            int halfN = _fftSize / 2;
            float sampleRate = _voiceDetector.SampleRate;
            float nyquist = sampleRate * 0.5f;

            float weightedSum = 0f;
            float magnitudeSum = 0f;

            // Skip bin 0 (DC offset)
            for (int i = 1; i < halfN; i++)
            {
                float freq = (float)i * sampleRate / _fftSize;
                float mag  = _magnitude[i];
                weightedSum  += freq * mag;
                magnitudeSum += mag;
            }

            if (magnitudeSum < 1e-8f) return 0f;

            float centroid = weightedSum / magnitudeSum;
            return Mathf.Clamp01(centroid / nyquist);
        }

        /// <summary>
        /// Spectral flatness (Wiener entropy):
        /// geometric mean / arithmetic mean of magnitude spectrum.
        /// 0 = pure tonal, 1 = flat white noise.
        /// </summary>
        private float ComputeSpectralFlatness()
        {
            int halfN = _fftSize / 2;
            float epsilon = 1e-10f;

            float logSum = 0f;
            float linearSum = 0f;
            int count = 0;

            // Skip bin 0 (DC)
            for (int i = 1; i < halfN; i++)
            {
                float mag = _magnitude[i] + epsilon;
                logSum    += Mathf.Log(mag);
                linearSum += mag;
                count++;
            }

            if (count == 0) return 0f;

            float geometricMean  = Mathf.Exp(logSum / count);
            float arithmeticMean = linearSum / count;

            if (arithmeticMean < epsilon) return 0f;

            return Mathf.Clamp01(geometricMean / arithmeticMean);
        }

        // ─── Helpers ────────────────────────────────────────────────────

        private void SetInactive()
        {
            Features = new VoiceFeatures
            {
                Loudness   = 0f,
                Pitch      = _smoothedPitch,
                Brightness = _smoothedBrightness,
                Noisiness  = _smoothedNoisiness,
                IsActive   = false
            };
        }

        private static int[] BuildBitReversalTable(int n)
        {
            var table = new int[n];
            int bits = 0;
            for (int temp = n; temp > 1; temp >>= 1) bits++;

            for (int i = 0; i < n; i++)
            {
                int reversed = 0;
                int val = i;
                for (int b = 0; b < bits; b++)
                {
                    reversed = (reversed << 1) | (val & 1);
                    val >>= 1;
                }
                table[i] = reversed;
            }
            return table;
        }
    }
}
