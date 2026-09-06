#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

namespace Repetitionless.Editor.Painter
{
    public class PainterSceneInteraction
    {
        // Callbacks
        public Action<EResizingProperty> OnResizePressed;
        public Action<EResizingProperty> OnResizeHeld;
        public Action OnResizeReleased;

        public Action OnInvertPressed;
        public Action OnInvertReleased;

        public Action<SceneView, Vector3> OnZoomPressed;

        public Action OnToggleEraseHolesPressed;

        public Action OnLayerDecreased;
        public Action OnLayerIncreased;

        // Variables
        private bool _rightClickHeld = false;
        private bool _altHeld = false;
        private bool _shiftHeld = false;
        private bool _controlHeld = false;
        public bool RightClickHeld => _rightClickHeld;
        public bool AltHeld => _altHeld;
        public bool ShiftHeld => _shiftHeld;
        public bool ControlHeld => _controlHeld;

        private EResizingProperty _resizingProperty = EResizingProperty.None;
        public EResizingProperty ResizingProperty => _resizingProperty;

        private bool _resizingBrush = false;
        public bool ResizingBrush => _resizingBrush;

        private RaycastHit _lastMouseHit;
        public RaycastHit LastMouseHit => _lastMouseHit;

        public List<GameObject> AllowedRaycastObjects = new List<GameObject>();

        // Must be called in DuringSceneGUI
        public void DuringSceneGUI(SceneView sceneView, bool paintingHoles)
        {
            Event currentEvent = Event.current;

            // Disable default left click events
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            UpdateMouseHit();

            TrackHeldKeys(currentEvent);
            TrackResize(currentEvent, paintingHoles);
            TrackInvertBrush(currentEvent);
            TrackZoom(currentEvent, sceneView);
            if (!paintingHoles) TrackLayerChange(currentEvent);
            if (paintingHoles) TrackHolesKeybinds(currentEvent);
        }

        // Mouse Hit
        private void UpdateMouseHit()
        {
            if (_resizingBrush)
                return;

            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            
            // If an object mask is set, get all objects in raycast
            if (AllowedRaycastObjects.Count != 0) {
                // Get all objects, use first hit unless its not allowed
                RaycastHit[] hits = Physics.RaycastAll(ray);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance)); // Sort from closest to furthest

                foreach (RaycastHit hit in hits) {
                    // Avoid if masking hits
                    if (AllowedRaycastObjects.Count != 0 && !AllowedRaycastObjects.Contains(hit.collider.gameObject))
                        continue;

                    _lastMouseHit = hit;
                    return;
                }
            }
            // Otherwise just to a simple raycast
            else if (Physics.Raycast(ray, out RaycastHit hit)) {
                _lastMouseHit = hit;
                return;
            }

            _lastMouseHit = new RaycastHit();
        }

        // Keybinds
        private void TrackHeldKeys(Event currentEvent)
        {
            // Right click
            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 1) _rightClickHeld = true;
            if (currentEvent.type == EventType.MouseUp && currentEvent.button == 1) _rightClickHeld = false;

            // Alt
            if (currentEvent.type == EventType.KeyDown && (currentEvent.keyCode == KeyCode.LeftAlt || currentEvent.keyCode == KeyCode.RightAlt)) _altHeld = true;
            if (currentEvent.type == EventType.KeyUp && (currentEvent.keyCode == KeyCode.LeftAlt || currentEvent.keyCode == KeyCode.RightAlt)) _altHeld = false;

            // Shift
            if (currentEvent.type == EventType.KeyDown && (currentEvent.keyCode == KeyCode.LeftShift || currentEvent.keyCode == KeyCode.RightShift)) _shiftHeld = true;
            if (currentEvent.type == EventType.KeyUp && (currentEvent.keyCode == KeyCode.LeftShift || currentEvent.keyCode == KeyCode.RightShift)) _shiftHeld = false;

            // Control
            if (currentEvent.type == EventType.KeyDown && (currentEvent.keyCode == KeyCode.LeftControl || currentEvent.keyCode == KeyCode.RightControl)) _controlHeld = true;
            if (currentEvent.type == EventType.KeyUp && (currentEvent.keyCode == KeyCode.LeftControl || currentEvent.keyCode == KeyCode.RightControl)) _controlHeld = false;
        }

        private void TrackResize(Event currentEvent, bool paintingHoles)
        {
            // If starting to move during resize, cancel resize
            if (_resizingBrush && _rightClickHeld) {
                _resizingBrush = false;
                _resizingProperty = EResizingProperty.None;
            }

            // Cancel on control to allow ctrl+s saving
            // Cancel on right click to avoid camera movement
            if (_controlHeld || _rightClickHeld)
                return;

            EResizingProperty prevEResizingProperty = _resizingProperty;
            bool isResizeKey = true;
            switch (currentEvent.keyCode) {
                case KeyCode.S:
                    _resizingProperty = EResizingProperty.Radius;
                    break;
                case KeyCode.A:
                    if (paintingHoles)
                        _resizingProperty = EResizingProperty.Cutoff;
                    else
                        _resizingProperty = EResizingProperty.Opacity;
                    break;
                case KeyCode.D: 
                    if (!paintingHoles)
                        _resizingProperty = EResizingProperty.Smoothness;
                    break;
                case KeyCode.C: 
                    _resizingProperty = EResizingProperty.Rotation;
                    break;
                default: isResizeKey = false; break;
            }

            // Dont allow other key presses to cancel the resize
            if (_resizingBrush && !isResizeKey && (currentEvent.type == EventType.KeyDown || currentEvent.type == EventType.KeyUp))
                return;

            // Dont allow resizing multiple properties at once
            if (_resizingBrush && prevEResizingProperty != _resizingProperty) {
                _resizingProperty = prevEResizingProperty;
                return;
            }

            if (_resizingProperty == EResizingProperty.None && !_resizingBrush)
                return;

            // Start resize
            if (currentEvent.type == EventType.KeyDown && !_resizingBrush) {
                _resizingBrush = true;
                OnResizePressed?.Invoke(_resizingProperty);
                currentEvent.Use();
                return;
            }

            // Finish resize
            if (currentEvent.type == EventType.KeyUp && _resizingBrush) {
                _resizingBrush = false;
                _resizingProperty = EResizingProperty.None;
                OnResizeReleased?.Invoke();
                currentEvent.Use();
                return;
            }

            // Fixes tool settings popup on some machines
            if (currentEvent.type == EventType.KeyDown && _resizingBrush) {
                currentEvent.Use();
                return;
            }

            // Resizing
            if (currentEvent.type != EventType.MouseMove || !_resizingBrush)
                return;

            OnResizeHeld(_resizingProperty);
            currentEvent.Use();
        }

        private void TrackInvertBrush(Event currentEvent)
        {
            if (currentEvent.keyCode != KeyCode.LeftControl && currentEvent.keyCode != KeyCode.RightControl)
                return;

            if (currentEvent.type == EventType.KeyDown) {
                OnInvertPressed?.Invoke();
                currentEvent.Use();
            }
                
            if (currentEvent.type == EventType.KeyUp) {
                OnInvertReleased?.Invoke();
                currentEvent.Use();
            }
        }

        private void TrackZoom(Event currentEvent, SceneView sceneView)
        {
            if (_lastMouseHit.collider == null || currentEvent.keyCode != KeyCode.F || currentEvent.type != EventType.KeyDown)
                return;

            OnZoomPressed?.Invoke(sceneView, _lastMouseHit.point);
            currentEvent.Use();
        }

        private void TrackLayerChange(Event currentEvent)
        {
            // If shift + mouse wheel, change layer
            if (currentEvent.shift && currentEvent.type == EventType.ScrollWheel) {
                float scrollDelta = Mathf.Abs(currentEvent.delta.x) > Mathf.Abs(currentEvent.delta.y) ? -currentEvent.delta.x : currentEvent.delta.y;

                if (scrollDelta > 0) OnLayerDecreased?.Invoke();
                else                 OnLayerIncreased?.Invoke();

                currentEvent.Use();
            }
        }

        private void TrackHolesKeybinds(Event currentEvent)
        {
            // Toggle erase holes
            if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.D) {
                OnToggleEraseHolesPressed?.Invoke();
                currentEvent.Use();
            }
        }
    }
}

#endif