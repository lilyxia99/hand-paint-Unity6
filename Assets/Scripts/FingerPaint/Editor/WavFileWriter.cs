/*
 * WavFileWriter — writes a standard 16-bit PCM WAV file from float samples.
 *
 * Usage:
 *   WavFileWriter.Write("path/to/file.wav", samples, 44100);
 */

using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FingerPaint.Editor
{
    public static class WavFileWriter
    {
        /// <summary>
        /// Writes accumulated float samples (range -1..1) as a 16-bit PCM WAV file.
        /// </summary>
        /// <param name="filePath">Absolute or relative path to write the .wav file.</param>
        /// <param name="samples">Normalized audio samples (-1 to 1).</param>
        /// <param name="sampleRate">Sample rate in Hz (e.g. 44100).</param>
        /// <param name="channels">Number of audio channels (1 = mono).</param>
        public static void Write(string filePath, List<float> samples,
                                 int sampleRate, int channels = 1)
        {
            if (samples == null || samples.Count == 0)
            {
                Debug.LogWarning("[WavFileWriter] No samples to write.");
                return;
            }

            // Ensure directory exists
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (var stream = new FileStream(filePath, FileMode.Create))
            using (var writer = new BinaryWriter(stream))
            {
                int sampleCount = samples.Count;
                int bitsPerSample = 16;
                int bytesPerSample = bitsPerSample / 8;
                int dataSize = sampleCount * bytesPerSample;
                int byteRate = sampleRate * channels * bytesPerSample;
                short blockAlign = (short)(channels * bytesPerSample);

                // ── RIFF header ──────────────────────────────────────
                writer.Write(new char[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataSize);              // ChunkSize
                writer.Write(new char[] { 'W', 'A', 'V', 'E' });

                // ── fmt sub-chunk ────────────────────────────────────
                writer.Write(new char[] { 'f', 'm', 't', ' ' });
                writer.Write(16);                          // Subchunk1Size (PCM)
                writer.Write((short)1);                    // AudioFormat (1 = PCM)
                writer.Write((short)channels);             // NumChannels
                writer.Write(sampleRate);                  // SampleRate
                writer.Write(byteRate);                    // ByteRate
                writer.Write(blockAlign);                  // BlockAlign
                writer.Write((short)bitsPerSample);        // BitsPerSample

                // ── data sub-chunk ───────────────────────────────────
                writer.Write(new char[] { 'd', 'a', 't', 'a' });
                writer.Write(dataSize);                    // Subchunk2Size

                // Write samples as 16-bit signed integers
                for (int i = 0; i < sampleCount; i++)
                {
                    float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                    short pcm = (short)(clamped * 32767f);
                    writer.Write(pcm);
                }
            }
        }
    }
}
