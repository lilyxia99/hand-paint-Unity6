using System;
using System.Collections.Generic;
using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Parses standard SRT subtitle files into a flat list of timed entries.
    /// Feed it the .text of a TextAsset (rename your .srt to .srt.txt so Unity imports it).
    /// </summary>
    public static class SrtParser
    {
        public struct SrtEntry
        {
            public float StartTime;
            public float EndTime;
            public string Text;
        }

        /// <summary>
        /// Parse raw SRT text content into a sorted list of entries.
        /// </summary>
        public static List<SrtEntry> Parse(string srtContent)
        {
            var entries = new List<SrtEntry>();
            if (string.IsNullOrEmpty(srtContent)) return entries;

            // Normalise line endings
            srtContent = srtContent.Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = srtContent.Split('\n');

            int i = 0;
            while (i < lines.Length)
            {
                // Skip blank lines and find the sequence number
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) { i++; continue; }

                // Sequence number (integer) — skip it
                if (!int.TryParse(line, out _)) { i++; continue; }
                i++;

                // Timestamp line: 00:00:01,500 --> 00:00:04,000
                if (i >= lines.Length) break;
                string tsLine = lines[i].Trim();
                i++;

                int arrowIdx = tsLine.IndexOf("-->", StringComparison.Ordinal);
                if (arrowIdx < 0) continue;

                string startStr = tsLine.Substring(0, arrowIdx).Trim();
                string endStr   = tsLine.Substring(arrowIdx + 3).Trim();

                if (!TryParseTimestamp(startStr, out float startTime)) continue;
                if (!TryParseTimestamp(endStr, out float endTime)) continue;

                // Collect text lines until blank line or end of file
                var textLines = new List<string>();
                while (i < lines.Length && !string.IsNullOrEmpty(lines[i].Trim()))
                {
                    textLines.Add(lines[i].Trim());
                    i++;
                }

                entries.Add(new SrtEntry
                {
                    StartTime = startTime,
                    EndTime   = endTime,
                    Text      = string.Join("\n", textLines)
                });
            }

            return entries;
        }

        /// <summary>
        /// Find the subtitle text for a given time. Returns null if no subtitle is active.
        /// Uses a simple linear scan — fast enough for typical subtitle files (< 500 entries).
        /// </summary>
        public static string GetTextAtTime(List<SrtEntry> entries, float time)
        {
            if (entries == null) return null;

            for (int i = 0; i < entries.Count; i++)
            {
                if (time >= entries[i].StartTime && time <= entries[i].EndTime)
                    return entries[i].Text;

                // Entries are sorted — if we're past the start and haven't matched, keep going.
                // If we haven't reached the start yet, no point continuing past this entry.
                if (entries[i].StartTime > time)
                    return null;
            }

            return null;
        }

        /// <summary>
        /// Parse "HH:MM:SS,mmm" or "HH:MM:SS.mmm" to seconds.
        /// </summary>
        private static bool TryParseTimestamp(string s, out float seconds)
        {
            seconds = 0f;

            // Accept both comma and period as ms separator
            s = s.Replace(',', '.');

            // Expected: HH:MM:SS.mmm
            string[] parts = s.Split(':');
            if (parts.Length != 3) return false;

            if (!int.TryParse(parts[0], out int hours)) return false;
            if (!int.TryParse(parts[1], out int minutes)) return false;
            if (!float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float secs)) return false;

            seconds = hours * 3600f + minutes * 60f + secs;
            return true;
        }
    }
}
