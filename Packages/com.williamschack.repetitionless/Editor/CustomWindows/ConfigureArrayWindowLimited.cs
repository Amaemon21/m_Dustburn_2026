// ------------------------------------------------------------------
// | A limited version of the configure script from my other asset: |
// | Texture Array Essentials (https://u3d.as/3s4d)                 |
// ------------------------------------------------------------------

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

namespace Repetitionless.Editor.CustomWindows
{
    using System;
    using Utilities.GUI;
    using Utilities.Texture;

    /// <summary>
    /// A window used to configure a texture array
    /// </summary>
    public class ConfigureArrayWindowLimited : Texture2DArrayWindowBase
    {
        private Texture2DArray _array;

        private List<Texture2D> _oriArrayTextures;
        private List<bool> _texturesChanged;
        private int _actualTexCount;

        private string HeaderString = "Array";
        private Action<Texture2DArray> OnTextureRecreatedCallback = null;

        /// <summary>
        /// Opens the window
        /// </summary>
        /// <param name="array">
        /// The array to modify
        /// </param>
        /// <param name="header">
        /// The header text
        /// </param>
        /// <param name="onTextureRecreatedCallback">
        /// Callback for when the texture is recreated
        /// </param>
        public static void Open(Texture2DArray array, string header = "Array", Action<Texture2DArray> onTextureRecreatedCallback = null)
        {
            ConfigureArrayWindowLimited window = Open();
            window.UpdateArrayVariables(array);
            window.HeaderString = header;
            window.OnTextureRecreatedCallback = onTextureRecreatedCallback;
        }

        private static ConfigureArrayWindowLimited Open()
        {
            ConfigureArrayWindowLimited window = GetWindow<ConfigureArrayWindowLimited>(false, "Configure Array");
            window.Show();

            return window;
        }

        private void UpdateArrayVariables(Texture2DArray array)
        {
            // Set array
            _array = array;

            if (_array == null)
                return;

            // Reset variables
            _oriArrayTextures.Clear();
            _texturesChanged.Clear();

            _textures.Clear();
            _texturesResizing.Clear();
            _textureErrors.Clear();

            // Get textures from array
            Texture2D[] arrayTextures = Texture2DArrayUtilities.GetTextures(array);
            for (int i = 0; i < arrayTextures.Length; i++)
            {
                _textures.Add(arrayTextures[i]);
                _oriArrayTextures.Add(arrayTextures[i]);
                _texturesResizing.Add(false);
                _textureErrors.Add(false);

                _texturesChanged.Add(false);
            }

            _actualTexCount = arrayTextures.Length;

            // Setup array variables
            _arrayTextureFormatIndex = System.Array.IndexOf(Texture2DArrayUtilities.SUPPORTED_TEXTURE_FORMATS, _array.format);
            _arrayMipMaps = _array.mipmapCount > 1;
            _arrayWrapMode = array.wrapMode;
            _arrayFilterMode = array.filterMode;
            _arrayAnisoLevel = array.anisoLevel;
            _arrayLinear = !array.isDataSRGB;
            _arrayResolution = new Vector2Int(array.width, array.height);

            // Mipmaps cannot be generated if array doesn't have them
            _mipmapsSettingEnabled = _arrayMipMaps;
        }

        /// <summary>
        /// Called when the GUI is first created
        /// </summary>

        protected override void CreateGUI()
        {
            _canAddRemoveTextures = false;
            base.CreateGUI();

            _oriArrayTextures = new List<Texture2D>();
            _texturesChanged = new List<bool>();

            // Get current selection, if it is an array set it here
            Object[] selectedArrays = Selection.objects.Where(x => x is Texture2DArray).ToArray();
            if (selectedArrays.Length > 0)
            {
                // Get first array selected
                UpdateArrayVariables((Texture2DArray)selectedArrays[0]);
            }
        }

        /// <summary>
        /// Called every GUI update, used due to other tasks in OnGUI
        /// </summary>
        protected override void OnGUIUpdate()
        {
            GUILayout.Label(HeaderString, _headerStyle);

            GUIUtilities.BeginBackgroundVertical();

            GUI.enabled = false;
            Texture2DArray selectedArray = GUIUtilities.DrawTexture2DArray(_array, new GUIContent("Array"));
            GUI.enabled = true;

            if (selectedArray != _array)
                UpdateArrayVariables(selectedArray);

            if (_array == null)
            {
                GUIUtilities.EndBackgroundVertical();
                return;
            }

            GUILayout.Label($"Resolution: {_array.width}x{_array.height}");
            GUILayout.Label($"Depth: {_array.depth}");
            GUILayout.Label($"Format: {_array.format}");

            GUILayout.Space(5);

            if (GUILayout.Button("Restore Array Textures & Settings"))
            {
                UpdateArrayVariables(_array);
            }

            GUIUtilities.EndBackgroundVertical();
            GUILayout.Space(10);

            base.OnGUIUpdate();

            GUILayout.Space(10);

            // Dont allow array creation if errors exist
            if (_textureErrors.Contains(true))
            {
                EditorGUILayout.HelpBox("Cannot create texture array, texture(s) still need resolving", MessageType.Error);
                GUI.enabled = false;
            }

            // Dont allow array creation if no textures are assigned
            if (!ArrayTexturesExist())
            {
                EditorGUILayout.HelpBox("Cannot create texture array, no textures are assigned", MessageType.Error);
                GUI.enabled = false;
            }

            if (GUILayout.Button("Re-Create Array", GUILayout.Height(GUIUtilities.LINE_HEIGHT * 2)))
            {
                RecreateArray();
            }

            GUI.enabled = true;
        }

        /// <summary>
        /// Called when the add button is clicked in the ReorderableList GUI
        /// </summary>
        /// <param name="list">
        /// The ReorderableList used for displaying the GUI
        /// </param>
        protected override void OnTextureAdd(ReorderableList list)
        {
            _actualTexCount++;

            // Only add elements when past array depth
            if (_actualTexCount > _oriArrayTextures.Count)
                base.OnTextureAdd(list);
        }

        /// <summary>
        /// Called when the remove button or delete key is pressed in the ReorderableList GUI
        /// </summary>
        /// <param name="list">
        /// The ReorderableList used for displaying the GUI
        /// </param>
        protected override void OnTextureRemove(ReorderableList list)
        {
            _actualTexCount--;

            // Only remove elements when past array depth
            if (_actualTexCount >= _oriArrayTextures.Count)
                base.OnTextureRemove(list);
        }

        /// <summary>
        /// Called when the textures are reordered in the ReorderableList GUI
        /// </summary>
        /// <param name="list">
        /// The ReorderableList used for displaying the GUI
        /// </param>
        /// <param name="oldActiveElement">
        /// The previous index of the reordered element
        /// </param>
        /// <param name="newActiveElement">
        /// The new index of the reordered element
        /// </param>
        protected override void OnTexturesReorder(ReorderableList list, int oldActiveElement, int newActiveElement)
        {
            base.OnTexturesReorder(list, oldActiveElement, newActiveElement);

            // Check if textures are the same as original and if so, set changed back to false
            // Check all textures between changed textures as they will all shift

            int smallerIndex = Mathf.Min(oldActiveElement, newActiveElement);
            int largerIndex = Mathf.Max(oldActiveElement, newActiveElement);
            for (int i = smallerIndex; i <= largerIndex; i++)
            {
                if (_textures[i] == _oriArrayTextures[i])
                    _texturesChanged[i] = false;
            }
        }

        /// <summary>
        /// Called every update while anything is dragged over the window
        /// </summary>
        /// <param name="objectReferences">
        /// The items that are being dragged
        /// </param>
        protected override void OnDragUpdate(UnityEngine.Object[] objectReferences)
        {
            base.OnDragUpdate(objectReferences);

            if (objectReferences.Any(x => x is Texture2DArray))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            }
        }

         /// <summary>
        /// Called when the drag is complete over the window
        /// </summary>
        /// <param name="objectReferences">
        /// The items that are being dragged
        /// </param>
        protected override void OnDragPerform(UnityEngine.Object[] objectReferences)
        {
            int prevTexCount = _textures.Count;
            base.OnDragPerform(objectReferences);

            // If textures were added set actual count to new count
            if (_textures.Count != prevTexCount)
                _actualTexCount = _textures.Count;

            if (objectReferences.Any(x => x is Texture2DArray))
            {
                UpdateArrayVariables((Texture2DArray)objectReferences.First(x => x is Texture2DArray));
            }
        }

        /// <summary>
        /// Calculates the height of an element for the ReorderableList GUI where the textures are assigned
        /// </summary>
        /// <param name="index">
        /// Index of the texture list which the height is being calculated for the ReorderableList GUI
        /// </param>
        /// <returns>
        /// Height that the element will use
        /// </returns>
        protected override float CalculateElementHeight(int index)
        {
            float height = base.CalculateElementHeight(index);

            // Save texture button when texture has not been changed
            if (index < _texturesChanged.Count() && !_texturesChanged[index])
            {
                height += GUIUtilities.LINE_HEIGHT + GUIUtilities.LINE_SPACING * 3;
            }

            // _textures[index] != _oriArrayTextures[index]

            // When the texture is being removed and it is within the array texture count
            if (_actualTexCount < _oriArrayTextures.Count && index >= _actualTexCount)
            {
                height += (GUIUtilities.LINE_HEIGHT + GUIUtilities.LINE_SPACING) * 2;

                // If texture does not exist or has an error
                if (_textures[index] == null || _textureErrors[index])
                    height += GUIUtilities.LINE_SPACING;
            }

            // If texture exists and has no error
            if (index < _textures.Count && _textures[index] != null && index < _textures.Count && !_textureErrors[index])
            {
                // If index is not within the array depth
                if (index >= _oriArrayTextures.Count)
                    height += (GUIUtilities.LINE_HEIGHT + GUIUtilities.LINE_SPACING) * 2;

                // If array requires new resolution
                Vector2Int actualArrayResolution = new Vector2Int(_array.width, _array.height);
                if (_arrayResolution != actualArrayResolution && (_textures[index].width != actualArrayResolution.x || _textures[index].height != actualArrayResolution.y))
                    height += GUIUtilities.LINE_SPACING;

                // If texture is different to original and within array depth
                if (index < _actualTexCount && index < _oriArrayTextures.Count && _texturesChanged[index])
                    height += GUIUtilities.LINE_HEIGHT * 3 + GUIUtilities.LINE_SPACING * 4;
            }

            return height;
        }

        /// <summary>
        /// Draws a texture element in the textures list
        /// </summary>
        /// <param name="rect">
        /// Rect with all the available space in the element<br />
        /// Available space is calculated and can be changed in CalculateElementHeight
        /// </param>
        /// <param name="index">
        /// The index of the current element in the textures list
        /// </param>
        /// <param name="isActive">
        /// If this element is is being moved or interacted with in the GUI
        /// </param>
        /// <param name="isFocused">
        /// If this element is selected in the GUI
        /// </param>
        protected override void DrawTextureField(Rect rect, int index, bool isActive, bool isFocused)
        {
            // Setup help box rect
            Rect helpBoxRect = rect;
            helpBoxRect.y += (GUIUtilities.LINE_HEIGHT + GUIUtilities.LINE_SPACING) * 3;
            helpBoxRect.height = (GUIUtilities.LINE_HEIGHT + GUIUtilities.LINE_SPACING) * 2;

            // If texture is removed but in original array disable its element
            if (_actualTexCount < _oriArrayTextures.Count && index >= _actualTexCount)
                GUI.enabled = false;

            // Draw texture field
            base.DrawTextureField(rect, index, isActive, isFocused);

            // Save texture button when texture has not been changed
            if (index < _texturesChanged.Count() && !_texturesChanged[index])
            {
                // Update helpBoxRect for later GUI
                helpBoxRect.y += GUIUtilities.LINE_HEIGHT + GUIUtilities.LINE_SPACING * 3;

                Rect buttonRect = rect;
                buttonRect.y += GUIUtilities.LINE_HEIGHT * 3 + GUIUtilities.LINE_SPACING * 4;
                buttonRect.height = GUIUtilities.LINE_HEIGHT + GUIUtilities.LINE_SPACING;

                GUI.enabled = true;
                if (GUI.Button(buttonRect, "Save Texture"))
                {
                    // Prompt for location to save to
                    string defaultFileName = $"{_array.name}_Texure{index + 1}";
                    string filePath = EditorUtility.SaveFilePanel("Choose File Path", Application.dataPath, defaultFileName, "png");

                    // If file path was set
                    if (filePath != "")
                    {
                        Texture2D texture = _oriArrayTextures[index];

                        // Cannot save compressed textures
                        // If compressed, uncompress it, resize re-renders the texture and in turn uncompresses it
                        if (TextureUtilities.COMPRESSED_TEXTURE_FORMATS.Contains(texture.format))
                            texture = TextureUtilities.ResizeTexture(texture, texture.width, texture.height);

                        // Save texture
                        byte[] textureBytes = texture.EncodeToPNG();
                        File.WriteAllBytes(filePath, textureBytes);
                        AssetDatabase.Refresh();

                        // Select and ping the created texture, can only ping if within project
                        if (filePath.Contains(Application.dataPath))
                        {
                            string absoluteFilePath = $"Assets/{filePath.Split(Application.dataPath).Last()}";
                            Texture2D savedAsset = (Texture2D)AssetDatabase.LoadAssetAtPath(absoluteFilePath, typeof(Texture2D));

                            EditorUtility.FocusProjectWindow();
                            Selection.activeObject = savedAsset;
                            EditorGUIUtility.PingObject(savedAsset);
                        }
                    }
                }
            }

            // If texture is removed but in original array, draw help box and re-enable array
            if (_actualTexCount < _oriArrayTextures.Count && index >= _actualTexCount)
            {
                if (_textures[index] == null)
                    helpBoxRect.y -= GUIUtilities.LINE_HEIGHT * 2 + GUIUtilities.LINE_SPACING;
                if (_textureErrors[index])
                    helpBoxRect.y += GUIUtilities.LINE_HEIGHT * 3 + GUIUtilities.LINE_SPACING * 5;

                GUI.enabled = true;
                EditorGUI.HelpBox(helpBoxRect, "Target depth shorter than array depth, texture will be removed on re-creation of the array", MessageType.Info);
                Repaint(); // Update element height, otherwise will be set next update
                return;
            }

            // If texture does not exist or has an error return
            if (index >= _textures.Count || _textures[index] == null || (_textureErrors.Count > index && _textureErrors[index]))
            {
                // Update textures changed
                if (index < _oriArrayTextures.Count())
                    _texturesChanged[index] = true;

                return;
            }

            // If texture index is larger than array depth, prompt for recreation
            if (index >= _oriArrayTextures.Count)
            {
                if (_textures[index] != null)
                {
                    EditorGUI.HelpBox(helpBoxRect, "Index exeeds array depth, texture will be added on re-creation of the array", MessageType.Info);
                    Repaint(); // Update element height, otherwise will be set next update
                }

                return;
            }

            // If array requires new resolution, prompt for recreation
            Vector2Int actualArrayResolution = new Vector2Int(_array.width, _array.height);
            if (_arrayResolution != actualArrayResolution && (_textures[index].width != actualArrayResolution.x || _textures[index].height != actualArrayResolution.y))
            {
                // Update textures changed
                _texturesChanged[index] = true;

                EditorGUI.HelpBox(helpBoxRect, "Texture and target resolution are not the same size as the array, texture will be updated on re-creation of the array", MessageType.Warning);

                Rect buttonRect = rect;
                buttonRect.y += GUIUtilities.LINE_HEIGHT * 5 + GUIUtilities.LINE_SPACING * 6;
                buttonRect.height = GUIUtilities.LINE_HEIGHT + GUIUtilities.LINE_SPACING;

                if (GUI.Button(buttonRect, "Restore Original Resolution"))
                {
                    _arrayResolution = actualArrayResolution;

                    // Set all other textures to resize if they require it
                    for (int i = 0; i < _texturesResizing.Count; i++)
                    {
                        if (i == index)
                        {
                            _texturesResizing[i] = false;
                            continue;
                        }

                        bool resizing = true;
                        if (_textures[i].width == _arrayResolution.x && _textures[i].height == _arrayResolution.y)
                            resizing = false;

                        _texturesResizing[i] = resizing;
                    }
                }

                Repaint(); // Update element height, otherwise will be set next update
                return;
            }

            // If array doesn't need modification, quickly update texture
            if (_textures[index] != _oriArrayTextures[index])
            {
                // Update textures changed
                _texturesChanged[index] = true;

                EditorGUI.HelpBox(helpBoxRect, "Texture has been changed, would you like to update this texture in the array?", MessageType.Info);

                Rect leftButtonRect = rect;
                leftButtonRect.y += GUIUtilities.LINE_HEIGHT * 5 + GUIUtilities.LINE_SPACING * 6;
                leftButtonRect.width /= 2;
                leftButtonRect.width -= GUIUtilities.LINE_SPACING;
                leftButtonRect.height = GUIUtilities.LINE_HEIGHT + GUIUtilities.LINE_SPACING;

                Rect rightButtonRect = leftButtonRect;
                rightButtonRect.x += leftButtonRect.width + GUIUtilities.LINE_SPACING * 2;

                if (GUI.Button(leftButtonRect, "Update Array Texture"))
                {
                    UpdateArrayTexture(index);
                }

                if (GUI.Button(rightButtonRect, "Restore Original Texture"))
                {
                    // Restore texture changed
                    _texturesChanged[index] = false;

                    _textures[index] = _oriArrayTextures[index];

                    // Disable resizing if original texture doesnt need it
                    if (_oriArrayTextures[index].width == _array.width && _oriArrayTextures[index].height == _array.height)
                        _texturesResizing[index] = false;
                }

                Repaint(); // Update element height, otherwise will be set next update
            }
        }

        private void UpdateArrayTexture(int index)
        {
            // Resize texture if requested
            if (_texturesResizing[index])
            {
                _textures[index] = TextureUtilities.ResizeTexture(_textures[index], _array.width, _array.height, _arrayFilterMode);
                _texturesResizing[index] = false;
            }

            // Updates array with new texture
            _array = Texture2DArrayUtilities.UpdateTextureAutoResize(_array, _textures[index], index);
            EditorUtility.SetDirty(_array);

            // Assign variables
            _textures[index] = Texture2DArrayUtilities.GetTexture(_array, index);
            _oriArrayTextures[index] = _textures[index];
            _texturesChanged[index] = false;
        }

        private void RecreateArray()
        {
            // Remove textures up until actual count if below array depth
            for (int i = _oriArrayTextures.Count - 1; i >= _actualTexCount; i--)
            {
                _textures.RemoveAt(i);
                _texturesResizing.RemoveAt(i);
                _textureErrors.RemoveAt(i);
            }

            // Get path of array
            string arrayPath = AssetDatabase.GetAssetPath(_array);

            // If array path cannot be found create array in current folder
            if (arrayPath == "")
            {
                Debug.LogWarning("Cannot find array path, creating in current directory");

                // Get current folder that user is viewing in project files
                MethodInfo tryGetActiveFolderPath = typeof(ProjectWindowUtil).GetMethod("TryGetActiveFolderPath", BindingFlags.Static | BindingFlags.NonPublic);

                object[] args = new object[] { null };
                bool found = (bool)tryGetActiveFolderPath.Invoke(null, args);
                arrayPath = (string)args[0];

                // If cannot get folder resort to project root
                if (!found)
                    arrayPath = "Assets";

                // Add filename to path
                arrayPath += $"/{_array.name}_RECREATED.asset";
            }

            // Delete current array at path
            AssetDatabase.DeleteAsset(arrayPath);

            // Create array and if successful close window
            if (CreateArray(arrayPath, false)) {
                Texture2DArray newArray = (Texture2DArray)AssetDatabase.LoadAssetAtPath(arrayPath, typeof(Texture2DArray));
                OnTextureRecreatedCallback.Invoke(newArray);

                Close();
            }
        }
    }
}
#endif