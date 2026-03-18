using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Optional controller for the Gradient Skybox shader.
    /// Attach to any GameObject in the scene to tweak gradient colours and
    /// animation speed at runtime via the Inspector.
    /// All heavy work is done on the GPU — this script only pushes a handful
    /// of uniform values when they change.
    /// </summary>
    [ExecuteAlways]
    public class GradientSkyboxController : MonoBehaviour
    {
        [Header("Top Colors")]
        [Tooltip("First top color in the gradient cycle")]
        public Color topColorA = new Color(0.05f, 0.05f, 0.20f);

        [Tooltip("Second top color in the gradient cycle")]
        public Color topColorB = new Color(0.15f, 0.02f, 0.25f);

        [Header("Bottom Colors")]
        [Tooltip("First bottom color in the gradient cycle")]
        public Color botColorA = new Color(0.02f, 0.10f, 0.15f);

        [Tooltip("Second bottom color in the gradient cycle")]
        public Color botColorB = new Color(0.10f, 0.05f, 0.12f);

        [Header("Animation")]
        [Tooltip("How fast the colors cycle (lower = slower)")]
        [Range(0.01f, 2f)]
        public float speed = 0.15f;

        [Tooltip("Controls the gradient curve (1 = linear, >1 = top-heavy)")]
        [Range(0.5f, 4f)]
        public float gradientCurve = 1f;

        // Cached shader property IDs — avoids string lookups every frame
        static readonly int _TopColorA = Shader.PropertyToID("_TopColorA");
        static readonly int _TopColorB = Shader.PropertyToID("_TopColorB");
        static readonly int _BotColorA = Shader.PropertyToID("_BotColorA");
        static readonly int _BotColorB = Shader.PropertyToID("_BotColorB");
        static readonly int _Speed     = Shader.PropertyToID("_Speed");
        static readonly int _Exponent  = Shader.PropertyToID("_Exponent");

        Material _skyMat;

        void OnEnable()
        {
            _skyMat = RenderSettings.skybox;
            if (_skyMat == null)
            {
                Debug.LogWarning("[GradientSkyboxController] No skybox material assigned in Lighting settings.");
                return;
            }
            PushProperties();
        }

        void Update()
        {
            // Only push when values change in the Inspector
            // (effectively free when nothing is tweaked at runtime)
            if (_skyMat == null) return;
            PushProperties();
        }

        void PushProperties()
        {
            _skyMat.SetColor(_TopColorA, topColorA);
            _skyMat.SetColor(_TopColorB, topColorB);
            _skyMat.SetColor(_BotColorA, botColorA);
            _skyMat.SetColor(_BotColorB, botColorB);
            _skyMat.SetFloat(_Speed, speed);
            _skyMat.SetFloat(_Exponent, gradientCurve);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // Refresh immediately in the editor when slider values change
            if (_skyMat != null) PushProperties();
        }
#endif
    }
}
