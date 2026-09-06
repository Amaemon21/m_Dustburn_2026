#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

namespace Repetitionless.Editor.Painter
{
    using Materials;
    using Utilities.Texture;

    public class Painter
    {
        private const string PAINT_TEXTURE_COMPUTE_RESOURCES_PATH = "repetitionless_PaintControlTexture";
        private const string PAINT_HOLES_COMPUTE_RESOURCES_PATH = "repetitionless_PaintHolesTexture";
        private const int COMPUTE_THREADS_X = 8;
        private const int COMPUTE_THREADS_Y = 8;

        private const double POPUP_HOLD_DURATION = 1.0f;
        private const double POPUP_FADE_DURATION = 0.4f;

        private const float BRUSH_RADIUS_SENSITIVITY = 0.2f;
        private const float BRUSH_OPACITY_SENSITIVITY = 0.008f;
        private const float BRUSH_SMOOTHNESS_SENSITIVITY = 0.008f;
        private const float BRUSH_ROTATION_SENSITIVITY = 2.0f;
        private const float BRUSH_CUTOFF_SENSITIVITY = 0.008f;
        private const float BRUSH_SENSITIVITY_SHIFT_MULTIPLIER = 0.1f;

        private const string UNDO_STROKE_NAME = "Repetitionless Paint Brush Stroke";

        public Action OnPropertyChanged;

        private PainterSceneInteraction _sceneInteraction = new PainterSceneInteraction();
        private PainterBrushPreview _brushPreview = new PainterBrushPreview();
        private PainterSelection _selection = new PainterSelection();

        private ComputeShader _controlComputeShader = null;
        private ComputeShader _holesComputeShader = null;

        public int TextureResolution {
            get { return _selection.TextureResolution; }
            set { _selection.TextureResolution = value; }
        }

        public int HolesTextureResolution {
            get { return _selection.HolesTextureResolution; }
            set { _selection.HolesTextureResolution = value; }
        }
        
        public int PaintingLayer = 1;

        // Global Brush Settings
        public Texture2D BrushTexture = null;
        public TexturePacker.TextureChannel BrushTextureChannel = TexturePacker.TextureChannel.R;
        public float BrushRadiusReal = 15;
        public float BrushRadius => BrushRadiusReal * 0.01f;
        public float BrushRotationDegrees = 0.0f;
        public bool InvertBrush = false;

        // Control Brush Settings
        public float BrushOpacity = 1.0f;
        public float BrushSmoothness = 0.5f;

        // Holes Brush Settings
        public float BrushCutoff = 0.01f;
        public bool ErasingHoles = false;

        private float _brushResizeLastMousePosX;

        private int _strokeUndoGroup = -1;


        private List<GameObject> _paintingObjects = new List<GameObject>();
        private GameObject _currentlyPaintingObject = null;
        private GameObject _lastPaintedObject = null;

        public GameObject CurrentlyPaintingObject => _currentlyPaintingObject;
        public GameObject LastPaintedObject => _lastPaintedObject;

        private bool _painting = false;
        public bool Painting => _painting;

        private bool _paintingHoles = false;
        public bool PaintingHoles => _paintingHoles;

        public void StartPainting()
        {
            SceneView.duringSceneGui -= DuringSceneGUI;
            SceneView.duringSceneGui += DuringSceneGUI;
            Undo.undoRedoPerformed -= UndoRedoPerformed;
            Undo.undoRedoPerformed += UndoRedoPerformed;

            _sceneInteraction.OnResizePressed           -= ResizePressed;
            _sceneInteraction.OnResizeHeld              -= ResizeHeld;
            _sceneInteraction.OnInvertPressed           -= InvertPressed;
            _sceneInteraction.OnInvertReleased          -= InvertReleased;
            _sceneInteraction.OnZoomPressed             -= ZoomPressed;
            _sceneInteraction.OnToggleEraseHolesPressed -= ToggleEraseHolesPressed;
            _sceneInteraction.OnLayerDecreased          -= LayerDecreased;
            _sceneInteraction.OnLayerIncreased          -= LayerIncreased;
            _sceneInteraction.OnResizePressed           += ResizePressed;
            _sceneInteraction.OnResizeHeld              += ResizeHeld;
            _sceneInteraction.OnInvertPressed           += InvertPressed;
            _sceneInteraction.OnInvertReleased          += InvertReleased;
            _sceneInteraction.OnZoomPressed             += ZoomPressed;
            _sceneInteraction.OnToggleEraseHolesPressed += ToggleEraseHolesPressed;
            _sceneInteraction.OnLayerDecreased          += LayerDecreased;
            _sceneInteraction.OnLayerIncreased          += LayerIncreased;

            if (_controlComputeShader == null) {
                _controlComputeShader = Resources.Load<ComputeShader>(PAINT_TEXTURE_COMPUTE_RESOURCES_PATH);
                if (_controlComputeShader == null)
                    Debug.LogError("No texture paint compute shader found...");
            }
            
            _selection.Setup();
            _selection.AddSelected();

            _painting = true;
            SceneView.RepaintAll();
        }

        public void StopPainting()
        {
            SceneView.duringSceneGui -= DuringSceneGUI;
            Undo.undoRedoPerformed -= UndoRedoPerformed;

            _selection.Cleanup();

            _painting = false;
            SceneView.RepaintAll();
        }

        public void TogglePainting()
        {
            if (_painting) StopPainting();
            else           StartPainting();
        }

        public void StartPaintingHoles(bool drawPopup = false)
        {
            if (!_painting)
                StartPainting();

            if (_holesComputeShader == null) {
                _holesComputeShader = Resources.Load<ComputeShader>(PAINT_HOLES_COMPUTE_RESOURCES_PATH);
                if (_holesComputeShader == null)
                    Debug.LogError("No holes paint compute shader found...");
            }

            // Draw popup
            if (drawPopup) {
                _brushPreview.ClearFadingPopups();
                _brushPreview.AddFadingPopup(
                    EditorApplication.timeSinceStartup + POPUP_HOLD_DURATION,
                    POPUP_FADE_DURATION,
                    $"Painting Holes",
                    new Color(0.1f, 0.1f, 0.1f), true, new Vector2(0, -25)
                );
            }

            _paintingHoles = true;
        }

        public void StopPaintingHoles(bool drawPopup = false)
        {
            // Draw popup
            if (drawPopup) {
                _brushPreview.ClearFadingPopups();
                _brushPreview.AddFadingPopup(
                    EditorApplication.timeSinceStartup + POPUP_HOLD_DURATION,
                    POPUP_FADE_DURATION,
                    $"Painting Control Textures",
                    new Color(0.1f, 0.1f, 0.1f), true, new Vector2(0, -25)
                );
            }

            _paintingHoles = false;
        }

        public void TogglePaintingHoles(bool drawPopup = false)
        {
            if (_paintingHoles) StopPaintingHoles(drawPopup);
            else                StartPaintingHoles(drawPopup);
        }

        private void DuringSceneGUI(SceneView sceneView)
        {
            if (_controlComputeShader == null)
                return;

            _sceneInteraction.DuringSceneGUI(sceneView, PaintingHoles);

            // Dont do anything when moving cam
            if (_sceneInteraction.AltHeld) return;

            if (Event.current.button == 0 && Event.current.type == EventType.MouseUp)
                FinishBrushStroke();

            if (!_sceneInteraction.ResizingBrush)
                _selection.DuringSceneGUI(_sceneInteraction.LastMouseHit, sceneView);

            if (_sceneInteraction.LastMouseHit.collider != null) {
                float innerRadius = PaintingHoles ? (ErasingHoles ? 0.9f : 0.0f) : (1 - BrushSmoothness);
                _brushPreview.DrawBrush(_sceneInteraction.LastMouseHit, sceneView, BrushRadius, innerRadius, BrushTexture == null ? -1 : BrushRotationDegrees);

                if (!_sceneInteraction.ResizingBrush && (_paintingObjects.Count != 0 || !Event.current.shift))
                    Paint(_sceneInteraction.LastMouseHit);
            }

            if (_sceneInteraction.ResizingBrush)
                DrawBrushResizeNotification();

            _brushPreview.OnSceneGUI();
        }

        private void LayerDecreased()
        {
            if (_sceneInteraction.ResizingBrush)
                return;

            UpdateLayer(Mathf.Max(0, PaintingLayer - 1));
        }

        private void LayerIncreased()
        {
            if (_sceneInteraction.ResizingBrush)
                return;

            UpdateLayer(Mathf.Min(PaintingLayer + 1, Constants.MAX_LAYERS_TERRAIN - 1));
        }

        private void UpdateLayer(int newLayer)
        {
            // Update layer
            PaintingLayer = newLayer;
            OnPropertyChanged?.Invoke();

            // Draw popup
            _brushPreview.ClearFadingPopups();
            _brushPreview.AddFadingPopup(
                EditorApplication.timeSinceStartup + POPUP_HOLD_DURATION,
                POPUP_FADE_DURATION,
                $"Layer {PaintingLayer + 1}",
                new Color(0.1f, 0.1f, 0.1f), true, new Vector2(0, -25)
            );
        }

        private void ResizePressed(EResizingProperty resizingProperty)
        {
            _brushResizeLastMousePosX = Event.current.mousePosition.x;
        }

        private void ResizeHeld(EResizingProperty resizingProperty)
        {
            Event currentEvent = Event.current;
            float delta = currentEvent.mousePosition.x - _brushResizeLastMousePosX;
            _brushResizeLastMousePosX = currentEvent.mousePosition.x;

            float sensitivityMultiplier = currentEvent.shift ? BRUSH_SENSITIVITY_SHIFT_MULTIPLIER : 1.0f;

            switch (resizingProperty) {
                case EResizingProperty.Radius:
                    BrushRadiusReal = Mathf.Max(0.01f, BrushRadiusReal + delta * (BRUSH_RADIUS_SENSITIVITY * sensitivityMultiplier));
                    break;
                case EResizingProperty.Opacity:
                    BrushOpacity = Mathf.Clamp01(BrushOpacity + delta * (BRUSH_OPACITY_SENSITIVITY * sensitivityMultiplier));
                    break;
                case EResizingProperty.Smoothness:
                    BrushSmoothness = Mathf.Clamp01(BrushSmoothness + delta * (BRUSH_SMOOTHNESS_SENSITIVITY * sensitivityMultiplier));
                    break;
                case EResizingProperty.Rotation:
                    BrushRotationDegrees = Mathf.Clamp(BrushRotationDegrees + delta * (BRUSH_ROTATION_SENSITIVITY * sensitivityMultiplier), 0, 360);
                    break;
                case EResizingProperty.Cutoff:
                    BrushCutoff = Mathf.Clamp01(BrushCutoff + delta * (BRUSH_CUTOFF_SENSITIVITY * sensitivityMultiplier));
                    break;
            }
            
            OnPropertyChanged?.Invoke();
        }

        private void ZoomPressed(SceneView sceneView, Vector3 pos)
        {
            sceneView.Frame(new Bounds(pos, Vector3.one), false);
        }

        private void InvertPressed()
        {
            InvertBrush = true;
            OnPropertyChanged?.Invoke();
        }

        private void InvertReleased()
        {
            InvertBrush = false;
            OnPropertyChanged?.Invoke();
        }

        private void ToggleEraseHolesPressed()
        {
            ErasingHoles = !ErasingHoles;

            // Draw popup
            _brushPreview.ClearFadingPopups();
            _brushPreview.AddFadingPopup(
                EditorApplication.timeSinceStartup + POPUP_HOLD_DURATION,
                POPUP_FADE_DURATION,
                $"Erase Holes: {(ErasingHoles ? "Enabled" : "Disabled")}",
                new Color(0.1f, 0.1f, 0.1f), true, new Vector2(0, -25)
            );

            OnPropertyChanged?.Invoke();
        }

        private void UndoRedoPerformed()
        {
            // Blit textures back to painted objects as they may have changed
            foreach (PaintableObjectData objectData in _selection.PaintableObjectData.Values) {
                // Control
                for (int i = 0; i < objectData.ControlTextures.Count; i++) {
                    Graphics.Blit(objectData.ControlTextures[i], objectData.RenderTextures[i]);
                }

                // Holes
                Graphics.Blit(objectData.HolesTexture, objectData.HolesRenderTexture);
            }
        }

        private void DrawBrushResizeNotification()
        {
            if (!_sceneInteraction.ResizingBrush)
                return;

            string text = "";
            switch (_sceneInteraction.ResizingProperty) {
                case EResizingProperty.Radius:
                    text = $"Radius: {BrushRadiusReal.ToString(_sceneInteraction.ShiftHeld ? "0.00" : "0.0")}";
                    break;
                case EResizingProperty.Opacity:
                    text = $"Opacity: {BrushOpacity.ToString(_sceneInteraction.ShiftHeld ? "0.000" : "0.00")}";
                    break;
                case EResizingProperty.Smoothness:
                    text = $"Smoothness: {BrushSmoothness.ToString(_sceneInteraction.ShiftHeld ? "0.000" : "0.00")}";
                    break;
                case EResizingProperty.Rotation:
                    text = $"Rotation: {BrushRotationDegrees.ToString(_sceneInteraction.ShiftHeld ? "0.0" : "0")}\u00B0";
                    break;
                case EResizingProperty.Cutoff:
                    text = $"Cutoff: {BrushCutoff.ToString(_sceneInteraction.ShiftHeld ? "0.000" : "0.00")}";
                    break;
            }
            
            _brushPreview.DrawMousePopup(text, new Color(0.1f, 0.1f, 0.1f, 1.0f), true, new Vector2(0, -25));
        }

        private void Paint(RaycastHit mouseHit)
        {
            Event currentEvent = Event.current;

            GameObject gameObject = mouseHit.collider.gameObject;
            if (!_selection.SelectedPaintableObjects.Contains(gameObject))
                return;

            // Cannot paint if selected layer exceeds available layers, show on hover
            if (gameObject != null && PaintingLayer >= (int)_selection.PaintableObjectData[gameObject].MaxLayers && !PaintingHoles) {
                _brushPreview.DrawMousePopup(
                    $"You are painting on an invalid Layer ({PaintingLayer + 1})\nUpdate the Max Layers property on this material",
                    new Color(0.25f, 0, 0, 1)
                );

                return;
            }

            if (currentEvent.button != 0)
                return;

            if (currentEvent.type != EventType.MouseDown && currentEvent.type != EventType.MouseDrag)
                return;

            PaintableObjectData objectData = _selection.PaintableObjectData[gameObject];

            // If starting to paint initialise stroke
            if (_paintingObjects.Count == 0)
                StartBrushStroke();

            // If stroke just passed over object, initialise painting
            if (!_paintingObjects.Contains(gameObject)) {
                // If starting stroke, register undo
                if (_paintingObjects.Count == 0) {
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName(UNDO_STROKE_NAME);
                    _strokeUndoGroup = Undo.GetCurrentGroup();
                }

                InitialisePainting(gameObject);
            }

            _currentlyPaintingObject = gameObject;
            _lastPaintedObject = gameObject;

            // Dispatch paint compute shader
            if (_paintingHoles) {
                // Painting holes
                int kernel = _holesComputeShader.FindKernel("CSMain");
            
                _holesComputeShader.SetTexture(kernel, "HolesTexture", objectData.HolesRenderTexture);
                _holesComputeShader.SetTexture(kernel, "PositionMap", objectData.PositionMap);
                _holesComputeShader.SetTexture(kernel, "BrushTexture", BrushTexture == null ? Texture2D.whiteTexture : BrushTexture);
                _holesComputeShader.SetInt("BrushChannel", BrushTexture == null ? -1 : (int)BrushTextureChannel);
                _holesComputeShader.SetBool("InvertBrush", InvertBrush);
                _holesComputeShader.SetMatrix("ObjectToWorld", gameObject.transform.localToWorldMatrix);
                _holesComputeShader.SetVector("HitWorldPos", mouseHit.point);
                _holesComputeShader.SetVector("HitNormal", mouseHit.normal);
                _holesComputeShader.SetFloat("Radius", BrushRadius);
                _holesComputeShader.SetFloat("RotationRadians", -BrushRotationDegrees * Mathf.Deg2Rad);
                _holesComputeShader.SetFloat("Cutoff", BrushCutoff);
                _holesComputeShader.SetBool("EraseHoles", ErasingHoles);

                int groupsX = Mathf.CeilToInt(objectData.HolesTexture.width  / (float)COMPUTE_THREADS_X);
                int groupsY = Mathf.CeilToInt(objectData.HolesTexture.height / (float)COMPUTE_THREADS_Y);

                _holesComputeShader.Dispatch(kernel, groupsX, groupsY, 1);
            } else {
                // Painting control
                int kernel = _controlComputeShader.FindKernel("CSMain");
                for (int i = 0; i < objectData.RenderTextures.Count; i++)
                    _controlComputeShader.SetTexture(kernel, $"Control{i}", objectData.RenderTextures[i]);
            
                _controlComputeShader.SetTexture(kernel, "PositionMap", objectData.PositionMap);
                _controlComputeShader.SetTexture(kernel, "BrushTexture", BrushTexture == null ? Texture2D.whiteTexture : BrushTexture);
                _controlComputeShader.SetInt("TargetSlice", PaintingLayer / 4);
                _controlComputeShader.SetInt("TargetChannel", PaintingLayer % 4);
                _controlComputeShader.SetInt("BrushChannel", BrushTexture == null ? -1 : (int)BrushTextureChannel);
                _controlComputeShader.SetBool("InvertBrush", InvertBrush);
                _controlComputeShader.SetMatrix("ObjectToWorld", gameObject.transform.localToWorldMatrix);
                _controlComputeShader.SetVector("HitWorldPos", mouseHit.point);
                _controlComputeShader.SetVector("HitNormal", mouseHit.normal);
                _controlComputeShader.SetFloat("Radius", BrushRadius);
                _controlComputeShader.SetFloat("Opacity", BrushOpacity);
                _controlComputeShader.SetFloat("Smoothness", BrushSmoothness);
                _controlComputeShader.SetFloat("RotationRadians", -BrushRotationDegrees * Mathf.Deg2Rad);

                int groupsX = Mathf.CeilToInt(objectData.ControlTextures[0].width  / (float)COMPUTE_THREADS_X);
                int groupsY = Mathf.CeilToInt(objectData.ControlTextures[0].height / (float)COMPUTE_THREADS_Y);

                _controlComputeShader.Dispatch(kernel, groupsX, groupsY, 1);
            }
        }

        private void InitialisePainting(GameObject gameObject)
        {
            _paintingObjects.Add(gameObject);

            PaintableObjectData objectData = _selection.PaintableObjectData[gameObject];

            // Register textures for undo
            foreach (Texture2D controlTexture in objectData.ControlTextures)
                Undo.RegisterCompleteObjectUndo(controlTexture, UNDO_STROKE_NAME);
            Undo.RegisterCompleteObjectUndo(objectData.HolesTexture, UNDO_STROKE_NAME);

            Material repetitionlessMaterial = RepetitionlessLayeredMaterialUtilities.GetFirstLayeredMaterial(objectData.MeshRenderer);

            // Apply to the object material
            for (int i = 0; i < objectData.ControlTextures.Count; i++)
                repetitionlessMaterial.SetTexture($"_Control{i}", objectData.RenderTextures[i]);
            repetitionlessMaterial.SetTexture("_TerrainHolesTexture", objectData.HolesRenderTexture);
        }

        private void CopyFromRT(RenderTexture renderTexture, Texture2D texture)
        {
            RenderTexture previousRT = RenderTexture.active;
            RenderTexture.active = renderTexture;

            texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            texture.Apply();

            EditorUtility.SetDirty(texture);

            RenderTexture.active = previousRT;
        }

        private void StartBrushStroke()
        {
            // Copy list to avoid referencing
            _sceneInteraction.AllowedRaycastObjects = new List<GameObject>(_selection.SelectedPaintableObjects);
        }

        private void FinishBrushStroke()
        {
            foreach (GameObject gameObject in _paintingObjects) {
                PaintableObjectData objectData = _selection.PaintableObjectData[gameObject];
                Material repetitionlessMaterial = RepetitionlessLayeredMaterialUtilities.GetFirstLayeredMaterial(objectData.MeshRenderer);

                RenderTexture previousRT = RenderTexture.active;

                // Control
                for (int i = 0; i < objectData.ControlTextures.Count; i++) {
                    CopyFromRT(objectData.RenderTextures[i], objectData.ControlTextures[i]);
                    repetitionlessMaterial.SetTexture($"_Control{i}", objectData.ControlTextures[i]);
                }

                // Holes
                CopyFromRT(objectData.HolesRenderTexture, objectData.HolesTexture);
                repetitionlessMaterial.SetTexture("_TerrainHolesTexture", objectData.HolesTexture);
                

                RenderTexture.active = previousRT;
            }

            // Merge texture changes into one undo group
            if (_strokeUndoGroup >= 0) {
                Undo.CollapseUndoOperations(_strokeUndoGroup);
                _strokeUndoGroup = -1;
            }

            _sceneInteraction.AllowedRaycastObjects.Clear();

            _currentlyPaintingObject = null;
            _paintingObjects.Clear();
        }
    }
}

#endif