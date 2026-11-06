using UnityEngine;

namespace MapClash
{
    [CreateAssetMenu(menuName = "Map Clash/Map Camera")]
    public sealed class MapCameraConfig : ScriptableObject
    {
        [Header("Navigation speeds")]
        [Tooltip("Exponential zoom per normalized scroll unit. Larger values zoom faster.")]
        [SerializeField, Min(0)] private float _zoomSensitivity = 0.144f;
        [Tooltip("Mouse drag distance multiplier. 1 follows the mouse at the current zoom.")]
        [SerializeField, Min(0)] private float _mousePanSpeed = 1;
        [Tooltip("WASD speed in camera half-heights per second.")]
        [SerializeField, Min(0)] private float _keyboardPanSpeed = 1;
        [Header("Smoothing")]
        [Tooltip("Higher values respond faster; 0 applies movement immediately.")]
        [SerializeField, Min(0)] private float _panResponse = 12;
        [Tooltip("Higher values respond faster; 0 applies zoom immediately.")]
        [SerializeField, Min(0)] private float _zoomResponse = 12;
        [Header("Zoom range (orthographic half-height)")]
        [SerializeField, Min(0.01f)] private float _minimumZoom = 1.5f;
        [SerializeField, Min(0.01f)] private float _focusZoom = 7;
        [Tooltip("Multiplier for the automatically fitted world overview / maximum zoom.")]
        [SerializeField, Min(1)] private float _overviewZoomMultiplier = 1;

        public float ZoomSensitivity => Mathf.Max(0, _zoomSensitivity);
        public float MousePanSpeed => Mathf.Max(0, _mousePanSpeed);
        public float KeyboardPanSpeed => Mathf.Max(0, _keyboardPanSpeed);
        public float PanResponse => Mathf.Max(0, _panResponse);
        public float ZoomResponse => Mathf.Max(0, _zoomResponse);
        public float MinimumZoom => Mathf.Max(0.01f, _minimumZoom);
        public float FocusZoom => Mathf.Max(MinimumZoom, _focusZoom);
        public float OverviewZoomMultiplier => Mathf.Max(1, _overviewZoomMultiplier);
    }
}
