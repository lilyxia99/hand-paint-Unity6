using UnityEngine;
using UnityEngine.XR;

public class XRTrackingBinder : MonoBehaviour
{
    public Transform targetHead;
    public Transform targetLeftHand;
    public Transform targetRightHand;

    private InputDevice headDevice;
    private InputDevice leftHandDevice;
    private InputDevice rightHandDevice;

    void Start()
    {
        headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        leftHandDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        rightHandDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    void Update()
    {
        UpdateTransform(headDevice, targetHead);
        UpdateTransform(leftHandDevice, targetLeftHand);
        UpdateTransform(rightHandDevice, targetRightHand);
    }

    void UpdateTransform(InputDevice device, Transform target)
    {
        if (device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos))
            target.position = pos;
        if (device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot))
            target.rotation = rot;
    }
}