using UnityEditor;
using UnityEngine;
using FingerPaint;

namespace FingerPaint.Editor
{
    /// <summary>
    /// Custom Inspector for HandAnimationPlayer.
    /// In Play mode, shows a runtime debug panel with:
    ///   - Time scrubber slider (seek animation + audio)
    ///   - Playback speed control (1x / 2x / 5x / 10x / 50x)
    ///   - Jump-to-end button (skip to 10s before loop point)
    ///   - Current time / total time display
    ///   - Pause / Resume button
    ///
    /// Useful for testing loop behavior on long recordings
    /// without waiting the full duration.
    /// </summary>
    [CustomEditor(typeof(HandAnimationPlayer))]
    public class HandAnimationPlayerEditor : UnityEditor.Editor
    {
        private float _scrubTime;
        private bool _wasDragging;

        public override void OnInspectorGUI()
        {
            // Draw the default Inspector fields
            DrawDefaultInspector();

            var player = (HandAnimationPlayer)target;

            // Only show debug controls in Play mode
            if (!Application.isPlaying)
                return;

            if (!player.IsPlaying && player.AnimationLength <= 0f)
            {
                EditorGUILayout.HelpBox("Waiting for playback to start...", MessageType.Info);
                Repaint();
                return;
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Runtime Debug Controls", EditorStyles.boldLabel);

            // ── Info ──────────────────────────────────────────────────────
            float animLen = player.AnimationLength;
            float audioLen = player.AudioLength;
            float effectiveLen = player.EffectiveLoopDuration > 0f
                ? player.EffectiveLoopDuration
                : animLen;
            float currentTime = player.CurrentTime;
            float audioTime = player.AudioTime;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Animation",
                $"{FormatTime(currentTime)} / {FormatTime(animLen)}");
            if (audioLen > 0f)
                EditorGUILayout.LabelField("Audio",
                    $"{FormatTime(audioTime)} / {FormatTime(audioLen)}");
            if (effectiveLen != animLen)
                EditorGUILayout.LabelField("Loop Point",
                    $"{FormatTime(effectiveLen)}");
            EditorGUILayout.EndVertical();

            // ── Time Scrubber ─────────────────────────────────────────────
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Scrub Timeline");
            float maxTime = Mathf.Max(animLen, 0.1f);

            // While not dragging, track the live time
            if (!_wasDragging)
                _scrubTime = currentTime;

            EditorGUI.BeginChangeCheck();
            float newTime = EditorGUILayout.Slider(_scrubTime, 0f, maxTime);
            bool changed = EditorGUI.EndChangeCheck();

            if (changed)
            {
                _scrubTime = newTime;
                _wasDragging = true;
                player.SeekTo(newTime);
            }

            // Detect mouse up to end dragging
            if (_wasDragging && Event.current.type == EventType.MouseUp)
            {
                _wasDragging = false;
            }

            // ── Speed Control ─────────────────────────────────────────────
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Playback Speed");

            float currentSpeed = player.PlaybackSpeed;

            EditorGUILayout.BeginHorizontal();
            if (SpeedButton("1x", 1f, currentSpeed)) player.SetPlaybackSpeed(1f);
            if (SpeedButton("2x", 2f, currentSpeed)) player.SetPlaybackSpeed(2f);
            if (SpeedButton("5x", 5f, currentSpeed)) player.SetPlaybackSpeed(5f);
            if (SpeedButton("10x", 10f, currentSpeed)) player.SetPlaybackSpeed(10f);
            if (SpeedButton("50x", 50f, currentSpeed)) player.SetPlaybackSpeed(50f);
            EditorGUILayout.EndHorizontal();

            // ── Quick Jump Buttons ────────────────────────────────────────
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("⏮ Start"))
            {
                player.SeekTo(0f);
            }

            if (GUILayout.Button("⏭ Near End (-10s)"))
            {
                float jumpTo = Mathf.Max(effectiveLen - 10f, 0f);
                player.SeekTo(jumpTo);
            }

            if (GUILayout.Button("⏭ Near End (-5s)"))
            {
                float jumpTo = Mathf.Max(effectiveLen - 5f, 0f);
                player.SeekTo(jumpTo);
            }
            EditorGUILayout.EndHorizontal();

            // ── Pause / Resume ────────────────────────────────────────────
            EditorGUILayout.Space(4);
            bool isPaused = player.IsPaused;
            if (GUILayout.Button(isPaused ? "▶ Resume" : "⏸ Pause"))
            {
                player.SetPaused(!isPaused);
            }

            // ── Restart ───────────────────────────────────────────────────
            if (GUILayout.Button("🔄 Restart (test loop)"))
            {
                player.Restart();
            }

            // Force continuous repaint so the slider tracks playback
            Repaint();
        }

        private static bool SpeedButton(string label, float speed, float currentSpeed)
        {
            bool isActive = Mathf.Approximately(currentSpeed, speed);
            var style = isActive
                ? new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold }
                : GUI.skin.button;

            var oldColor = GUI.backgroundColor;
            if (isActive) GUI.backgroundColor = Color.cyan;
            bool clicked = GUILayout.Button(label, style);
            GUI.backgroundColor = oldColor;
            return clicked;
        }

        private static string FormatTime(float seconds)
        {
            int m = (int)(seconds / 60f);
            float s = seconds % 60f;
            return $"{m}:{s:00.0}";
        }
    }
}
