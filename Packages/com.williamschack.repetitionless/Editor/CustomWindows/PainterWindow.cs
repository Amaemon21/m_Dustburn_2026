#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Repetitionless.Editor.CustomWindows
{
    using Painter;
    using Materials;
    using Utilities.GUI;
    using Utilities.Texture;
    using Repetitionless.Editor.Config;

    public class PainterWindow : EditorWindow
    {
        protected const int CHANNEL_PICKER_WIDTH = 50;

        private Painter _painter = new Painter();

        [MenuItem("Window/Repetitionless/Open Painter", priority = 1)]
        public static void Open()
        {
            PainterWindow window = GetWindow<PainterWindow>(false, "Repetitionless Painter");
            window.Show();
        }

        private void CreateGUI()
        {
            SceneView.duringSceneGui -= DuringSceneGUI;
            SceneView.duringSceneGui += DuringSceneGUI;

            _painter.OnPropertyChanged += OnPropertyChanged;

            LoadPrefs();

            _painter.StartPainting();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DuringSceneGUI;

            if (_painter != null)
                _painter.StopPainting();
        }
        
        private void OnGUI()
        {
            GUIUtilities.BeginBackgroundVertical();

            EditorGUI.BeginChangeCheck();
            GUIUtilities.DrawMajorToggleButton(_painter.Painting, "Painting");
            if (EditorGUI.EndChangeCheck())
                _painter.TogglePainting();

            GUILayout.BeginHorizontal();

            EditorGUI.BeginChangeCheck();
            GUILayout.Toggle(_painter.PaintingHoles, new GUIContent("Paint Holes", "Keybind: H"), "ButtonLeft");
            if (EditorGUI.EndChangeCheck()) _painter.TogglePaintingHoles();
            GUI.enabled = _painter.PaintingHoles;

            EditorGUI.BeginChangeCheck();
            GUILayout.Toggle(_painter.ErasingHoles, new GUIContent("Erase Holes", "Keybind: D"), "ButtonRight");
            if (EditorGUI.EndChangeCheck()) _painter.ErasingHoles = !_painter.ErasingHoles;
            GUI.enabled = true;

            GUILayout.EndHorizontal();

            GUI.enabled = _painter.LastPaintedObject != null;
            if (GUILayout.Button("Edit Last Painted Material")) {
                // Get the material and select it
                MeshRenderer meshRenderer = _painter.LastPaintedObject.GetComponent<MeshRenderer>(); // MeshRenderer is required to paint
                Material material = RepetitionlessLayeredMaterialUtilities.GetFirstLayeredMaterial(meshRenderer);
                Selection.activeObject = material;
            }
            GUI.enabled = true;

            GUIUtilities.EndBackgroundVertical();

            // Prefs
            EditorGUI.BeginChangeCheck();

            GUIUtilities.BeginBackgroundVertical();

            EditorGUILayout.LabelField("Painting Settings", EditorStyles.boldLabel);

            if (!_painter.PaintingHoles)
                _painter.PaintingLayer = EditorGUILayout.IntSlider(new GUIContent("Painting Layer", "Keybind: Shift + Scroll\nThe layer that will be painted. This is determined per material in its layer selection"), _painter.PaintingLayer + 1, 1, Constants.MAX_LAYERS_TERRAIN) - 1;
            
            _painter.TextureResolution = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Control Resolution", "The resolution of the control textures. Existing textures will be automatically resized to this resolution"), _painter.TextureResolution));

            _painter.HolesTextureResolution = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Holes Resolution", "The resolution of the holes texture. Existing textures will be automatically resized to this resolution"), _painter.HolesTextureResolution));

            GUILayout.Space(5);

            EditorGUILayout.LabelField("Brush Settings", EditorStyles.boldLabel);

            Rect textureLineRect = GUIUtilities.GetLineRect(GUIUtilities.LINE_HEIGHT);
            Rect textureRect = textureLineRect;
            textureRect.width -= CHANNEL_PICKER_WIDTH + 5;

            EditorGUI.BeginChangeCheck();
            _painter.BrushTexture = (Texture2D)EditorGUI.ObjectField(textureRect, new GUIContent("Brush Texture", "The texture used for painting, if not set it will be a circle filling the radius. The channel is what channel to read from in the texture"), _painter.BrushTexture, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck() && PainterPrefs.Data.SaveSettings) {
                // Save texture
                PainterPrefs.UpdatePrefs((p) => {
                    p.BrushTextureGUID = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_painter.BrushTexture));
                });
            }
            
            _painter.BrushTextureChannel = DrawChannelPicker(textureLineRect, _painter.BrushTextureChannel);
            if (_painter.BrushTexture != null) {
                _painter.BrushRotationDegrees = EditorGUILayout.Slider(new GUIContent("Brush Rotation", "Keybind: C\nThe rotation of the brush texture relative to the uvs of the painted object. This does nothing with no texture set"), _painter.BrushRotationDegrees, 0, 360);
                _painter.InvertBrush = EditorGUILayout.Toggle(new GUIContent("Invert Brush", "Keybind: Control\nInverts the brush texture, sampling it as (1 - value)"), _painter.InvertBrush);
                GUILayout.Space(10);
            }

            _painter.BrushRadiusReal = Mathf.Max(0.01f, EditorGUILayout.FloatField(new GUIContent("Brush Radius", "Keybind: S\nThe size of the brush"), _painter.BrushRadiusReal));
            if (_painter.PaintingHoles) {
                _painter.BrushCutoff = EditorGUILayout.Slider(new GUIContent("Brush Cutoff", "Keybind: A\nThe cutoff of the value for the brush texture where holes will not be drawn"), _painter.BrushCutoff, 0, 1);
            } else {
                _painter.BrushOpacity = EditorGUILayout.Slider(new GUIContent("Brush Opacity", "Keybind: A\nThe strength of the brush. The brush will accumulate so if you want opacity while dragging, set this to <= 0.05"), _painter.BrushOpacity, 0, 1);
                _painter.BrushSmoothness = EditorGUILayout.Slider(new GUIContent("Brush Smoothness", "Keybind: D\nWhat radius to start fading out the brush. This is visualised as the inner circle in the scene view"), _painter.BrushSmoothness, 0, 1);
            }

            GUIUtilities.EndBackgroundVertical();

            if (EditorGUI.EndChangeCheck())
                SavePrefs();
        }

        private protected TexturePacker.TextureChannel DrawChannelPicker(Rect lineRect, TexturePacker.TextureChannel channel)
        {
            Rect rect = lineRect;
            rect.x += lineRect.width - CHANNEL_PICKER_WIDTH;
            rect.width = CHANNEL_PICKER_WIDTH;

            return (TexturePacker.TextureChannel)EditorGUI.EnumPopup(rect, channel);
        }

        private void DuringSceneGUI(SceneView sceneView)
        {
            Event currentEvent = Event.current;

            if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.G) {
                _painter.TogglePainting();
                Repaint();
            }

            if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.H) {
                _painter.TogglePaintingHoles(true);
                Repaint();
            }
        }

        private void OnPropertyChanged()
        {
            Repaint();
            SavePrefs();
        }

        private void LoadPrefs()
        {
            if (!PainterPrefs.Data.SaveSettings)
                return;

            _painter.PaintingLayer = PainterPrefs.Data.PaintingLayer;
            _painter.BrushTextureChannel = (TexturePacker.TextureChannel)PainterPrefs.Data.BrushTextureChannel;
            _painter.BrushRadiusReal = PainterPrefs.Data.BrushRadiusReal;
            _painter.BrushRotationDegrees = PainterPrefs.Data.BrushRotationDegrees;
            _painter.BrushOpacity = PainterPrefs.Data.BrushOpacity;
            _painter.BrushSmoothness = PainterPrefs.Data.BrushSmoothness;
            _painter.BrushCutoff = PainterPrefs.Data.BrushCutoff;
            _painter.TextureResolution = PainterPrefs.Data.ControlResolution;
            _painter.HolesTextureResolution = PainterPrefs.Data.HolesResolution;

            // Load texture if it exists
            if (PainterPrefs.Data.BrushTextureGUID != "") {
                string path = AssetDatabase.GUIDToAssetPath(PainterPrefs.Data.BrushTextureGUID);
                Texture2D texture = path != "" ? AssetDatabase.LoadAssetAtPath<Texture2D>(path) : null;

                if (texture != null) {
                    _painter.BrushTexture = texture;
                } else {
                    // Brush texture has been removed, update the pref
                    PainterPrefs.UpdatePrefs((p) => {
                        p.BrushTextureGUID = "";
                    });
                }
            }
        }

        private void SavePrefs()
        {
            if (!PainterPrefs.Data.SaveSettings)
                return;
            
            PainterPrefs.UpdatePrefs((p) => {
                p.PaintingLayer = _painter.PaintingLayer;
                p.BrushTextureChannel = (int)_painter.BrushTextureChannel;
                p.BrushRadiusReal = _painter.BrushRadiusReal;
                p.BrushRotationDegrees = _painter.BrushRotationDegrees;
                p.BrushOpacity = _painter.BrushOpacity;
                p.BrushSmoothness = _painter.BrushSmoothness;
                p.BrushCutoff = _painter.BrushCutoff;
                p.ControlResolution = _painter.TextureResolution;
                p.HolesResolution = _painter.HolesTextureResolution;
            });
        }
    }
}

#endif