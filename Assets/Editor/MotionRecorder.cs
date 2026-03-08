using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using Unity.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class MotionRecorder : MonoBehaviour
{
    public AnimationClip targetClip;

#if UNITY_EDITOR
    private GameObjectRecorder recorder;
#endif

    private bool isRecording = false;

    void Start()
    {
#if UNITY_EDITOR
        recorder = new GameObjectRecorder(gameObject);
        recorder.BindComponentsOfType<Transform>(gameObject, true);
#endif
    }

    void Update()
    {
        if (UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            if (!isRecording) StartRec();
            else StopRec();
        }

        if (isRecording)
        {
#if UNITY_EDITOR
            recorder.TakeSnapshot(Time.deltaTime);
#endif
        }
    }

    void StartRec()
    {
        isRecording = true;
        Debug.Log("开始录制");
    }

    void StopRec()
    {
        isRecording = false;
#if UNITY_EDITOR
        recorder.SaveToClip(targetClip);
        AssetDatabase.SaveAssets();
#endif
        Debug.Log("录制完成");
    }
}