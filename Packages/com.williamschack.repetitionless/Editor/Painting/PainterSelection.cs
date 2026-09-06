#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

using Repetitionless.Runtime.Variables;

namespace Repetitionless.Editor.Painter
{
    using Data;
    using Materials;
    using Utilities.Texture;

    public class PainterSelection
    {
        private const string HOLES_TEXTURE_FILE_NAME = "Holes.asset";

        private const string FILL_POSITION_SEAMS_COMPUTE_RESOURCES_PATH = "repetitionless_FillPositionMapSeams";
        private const int COMPUTE_THREADS_X = 8;
        private const int COMPUTE_THREADS_Y = 8;

        private static readonly Color SELECTION_OUTLINE_COLOUR = Color.blue;

        public int TextureResolution = 512;
        public int HolesTextureResolution = 512;

        private Shader _bakePositionShader = null;
        private ComputeShader _fillPositionSeamsComputeShader = null;

        private List<GameObject> _selectedPaintableObjects = new List<GameObject>();
        private Dictionary<GameObject, PaintableObjectData> _paintableObjectData = new Dictionary<GameObject, PaintableObjectData>();
        
        public List<GameObject> SelectedPaintableObjects => _selectedPaintableObjects;
        public Dictionary<GameObject, PaintableObjectData> PaintableObjectData => _paintableObjectData;

        public void Setup()
        {
            ObjectChangeEvents.changesPublished -= ChangesPublished;
            ObjectChangeEvents.changesPublished += ChangesPublished;
        }

        public void Cleanup()
        {
            ObjectChangeEvents.changesPublished -= ChangesPublished;
        }

        // Must be called in DuringSceneGUI
        public void DuringSceneGUI(RaycastHit mouseHit, SceneView sceneView)
        {
            Event currentEvent = Event.current;

            // Draw outline to fake selection
#if UNITY_2022_1_OR_NEWER
            Handles.DrawOutline(_selectedPaintableObjects, SELECTION_OUTLINE_COLOUR, 0);
#else
            // Cannot draw object outlines below 2022.1, draw a box around the object bounds instead
            Color prevHandlesColour = Handles.color;
            Handles.color = SELECTION_OUTLINE_COLOUR;
            foreach (GameObject gameObject in _selectedPaintableObjects) {
                Renderer renderer = gameObject.GetComponent<Renderer>();
                if (renderer == null) return;

                Handles.DrawWireCube(renderer.bounds.center, renderer.bounds.size);
            }

            Handles.color = prevHandlesColour;
#endif

            // On click decide if it will be selected
            if (currentEvent.button == 0 && currentEvent.type == EventType.MouseDown) {
                // Clear selection if clicked nothing
                if (mouseHit.collider == null) {
                    RemoveAll();
                    sceneView.Repaint();
                    return;
                }

                GameObject hitObject = mouseHit.collider.gameObject;

                // If holding shift and the object is selected, remove it
                if (currentEvent.shift && _selectedPaintableObjects.Contains(hitObject)) {
                    Remove(hitObject);
                    currentEvent.Use();
                }
                // Check if object is valid and add to selected
                else if (ObjectCanBeSelected(mouseHit.collider))
                    Add(hitObject);
            }
        }

        private void ChangesPublished(ref ObjectChangeEventStream stream)
        {
            // Listen for when an object is deleted in the scene
            for (int i = 0; i < stream.length; i++) {
                switch (stream.GetEventType(i)) {
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                        stream.GetDestroyGameObjectHierarchyEvent(i, out DestroyGameObjectHierarchyEventArgs destroyGameObjectHierarchyEvent);
#if UNITY_6000_3_OR_NEWER
                        Object destroyedObject = EditorUtility.EntityIdToObject(destroyGameObjectHierarchyEvent.instanceId);
#else
                        Object destroyedObject = EditorUtility.InstanceIDToObject(destroyGameObjectHierarchyEvent.instanceId);
#endif

                        // There is a scene object that was deleted
                        // We dont know exactly which one but make sure none of the painting objects were deleted
                        RemoveNull();

                        break;
                }
            }
        }

        public void Add(GameObject gameObject)
        {
            if (_selectedPaintableObjects.Contains(gameObject))
                return;

            PaintableObjectData objectData = new PaintableObjectData {
                MeshRenderer = gameObject.GetComponent<MeshRenderer>()
            };

            // Need to test if:
            // Repetitionless material is removed
            Material repetitionlessMaterial = RepetitionlessLayeredMaterialUtilities.GetFirstLayeredMaterial(objectData.MeshRenderer);
            objectData.DataManager = new MaterialDataManager(repetitionlessMaterial);

            RepetitionlessMaterialDataSO materialPropertiesSO = objectData.DataManager.LoadAsset<RepetitionlessMaterialDataSO>(Constants.PROPERTIES_FILE_NAME);
            objectData.DataChangedAction = () => { MaterialExternalDataChanged(gameObject); };
            materialPropertiesSO.OnExternalDataChanged += objectData.DataChangedAction;

            // Assign texture to layered data
            RepetitionlessLayeredDataSO layeredDataSO = objectData.DataManager.LoadAsset<RepetitionlessLayeredDataSO>(Constants.LAYERED_DATA_FILE_NAME);

            objectData.MaxLayers = layeredDataSO.MaxLayers;

            // Make sure its mode is set to control textures
            RepetitionlessLayeredMaterialUtilities.UpdateLayerModeShader(objectData.DataManager, ELayerMode.ControlTextures);
            layeredDataSO.LayerMode = ELayerMode.ControlTextures;

            // Setup Control Textures
            objectData.ControlTextures = new List<Texture2D>();
            objectData.RenderTextures = new List<RenderTexture>();

            int controlTextureCount = Constants.MAX_LAYERS_TERRAIN / 4;
            for (int i = 0; i < controlTextureCount; i++) {
                // Get/Create control texture
                Texture2D texture = objectData.DataManager.LoadAsset<Texture2D>($"{Constants.CONTROL_TEXTURE_FILE_NAME_PREFIX}{i}.asset");
                
                // Resize texture to target
                if (texture.width != TextureResolution || texture.height != TextureResolution) {
                    TextureUtilities.ResizeTexture(texture, TextureResolution, TextureResolution, modifyOriginal: true);
                    EditorUtility.SetDirty(texture);
                    AssetDatabase.SaveAssetIfDirty(texture);
                }

                objectData.ControlTextures.Add(texture);

                // Setup layered data
                layeredDataSO.ControlTextures[i].ChannelTextures[0].Texture = texture;
                layeredDataSO.ControlTextures[i].ChannelTextures[1].Texture = texture;
                layeredDataSO.ControlTextures[i].ChannelTextures[2].Texture = texture;
                layeredDataSO.ControlTextures[i].ChannelTextures[3].Texture = texture;
                layeredDataSO.ControlTextures[i].ChannelTextures[0].FromToChannels[0] = new TexturePacker.FromToChannel(TexturePacker.TextureChannel.R, TexturePacker.TextureChannel.R);
                layeredDataSO.ControlTextures[i].ChannelTextures[1].FromToChannels[0] = new TexturePacker.FromToChannel(TexturePacker.TextureChannel.G, TexturePacker.TextureChannel.G);
                layeredDataSO.ControlTextures[i].ChannelTextures[2].FromToChannels[0] = new TexturePacker.FromToChannel(TexturePacker.TextureChannel.B, TexturePacker.TextureChannel.B);
                layeredDataSO.ControlTextures[i].ChannelTextures[3].FromToChannels[0] = new TexturePacker.FromToChannel(TexturePacker.TextureChannel.A, TexturePacker.TextureChannel.A);

                // Create render texture
                RenderTexture renderTexture = new RenderTexture(TextureResolution, TextureResolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) {
                    enableRandomWrite = true,
                    filterMode = FilterMode.Point
                };
                renderTexture.Create();

                // Copy control texture to the rt
                Graphics.Blit(texture, renderTexture);

                objectData.RenderTextures.Add(renderTexture);
            }

            // Setup Holes Texture
            if (layeredDataSO.HolesTexture.Texture != null) {
                // Use set holes texture
                objectData.HolesTexture = layeredDataSO.HolesTexture.Texture;
            } else {
                if (objectData.DataManager.AssetExists(HOLES_TEXTURE_FILE_NAME)) {
                    // If holes asset exists use that
                    objectData.HolesTexture = objectData.DataManager.LoadAsset<Texture2D>(HOLES_TEXTURE_FILE_NAME);

                    layeredDataSO.HolesTexture.Texture = objectData.HolesTexture;
                } else {
                    // Create holes texture
                    objectData.HolesTexture = new Texture2D(HolesTextureResolution, HolesTextureResolution, TextureFormat.R8, false);
                    Color[] pixels = new Color[HolesTextureResolution * HolesTextureResolution];
                    for (int i = 0; i < pixels.Length; i++)
                        pixels[i] = Color.white;

                    objectData.HolesTexture.SetPixels(pixels);
                    objectData.HolesTexture.Apply();

                    objectData.DataManager.CreateAsset(objectData.HolesTexture, HOLES_TEXTURE_FILE_NAME);

                    layeredDataSO.HolesTexture.Texture = objectData.HolesTexture;
                }
            }

            layeredDataSO.AssignHolesTexture();

            // Setup position map
            MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
            objectData.PositionMap = BakePositionMap(meshFilter.sharedMesh);

            // Resize holes texture to target
            if (objectData.HolesTexture.width != HolesTextureResolution || objectData.HolesTexture.height != HolesTextureResolution) {
                TextureUtilities.ResizeTexture(objectData.HolesTexture, HolesTextureResolution, HolesTextureResolution, modifyOriginal: true);
                EditorUtility.SetDirty(objectData.HolesTexture);
                AssetDatabase.SaveAssetIfDirty(objectData.HolesTexture);
            }

            // Create render texture
            objectData.HolesRenderTexture = new RenderTexture(HolesTextureResolution, HolesTextureResolution, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear) {
                enableRandomWrite = true,
                filterMode = FilterMode.Point
            };
            objectData.HolesRenderTexture.Create();

            // Copy holes texture to the rt
            Graphics.Blit(objectData.HolesTexture, objectData.HolesRenderTexture);

            // Finalise changes
            layeredDataSO.Save();

            if (!_selectedPaintableObjects.Contains(gameObject))
                _selectedPaintableObjects.Add(gameObject);
            
            if (_paintableObjectData.ContainsKey(gameObject))
                _paintableObjectData.Remove(gameObject);
            _paintableObjectData.Add(gameObject, objectData);
        }

        // Creates and returns a map from uv to local position
        private RenderTexture BakePositionMap(Mesh mesh)
        {
            if (_bakePositionShader == null)
                _bakePositionShader = Shader.Find("Hidden/Repetitionless/PaintingPositionMap");

            Material material = new Material (_bakePositionShader);
            material.SetInteger("_FlipY", SystemInfo.graphicsUVStartsAtTop ? 1 : 0);

            RenderTexture rt = new RenderTexture(TextureResolution, TextureResolution, 0, RenderTextureFormat.ARGBFloat) {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            rt.Create();

            RenderTexture previousRT = RenderTexture.active;
            RenderTexture.active = rt;

            GL.Clear(true, true, Color.clear);

            material.SetPass(0);
            Graphics.DrawMeshNow(mesh, Matrix4x4.identity);

            RenderTexture.active = previousRT;
            Object.DestroyImmediate(material);

            // Fill seams
            rt = FillPositionMapSeams(rt, 1);

            return rt;
        }

        /// <param name="iterations">
        /// Basically the amount of pixels to fill<br />
        /// So if a seam is 3 pixels wide, an iterations of 1 will fill either side but not the middle<br />
        /// 1 is usually fine though
        /// </param>
        private RenderTexture FillPositionMapSeams(RenderTexture positionMap, int iterations)
        {
            if (_fillPositionSeamsComputeShader == null) {
                _fillPositionSeamsComputeShader = Resources.Load<ComputeShader>(FILL_POSITION_SEAMS_COMPUTE_RESOURCES_PATH);
                if (_fillPositionSeamsComputeShader == null) {
                    Debug.LogError("No fill position seams compute shader found...");
                    return positionMap;
                }
            }

            RenderTexture tempRT = new RenderTexture(positionMap.width, positionMap.height, 0, positionMap.format) {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            tempRT.Create();

            RenderTexture source = positionMap;
            RenderTexture destination = tempRT;

            int kernel = _fillPositionSeamsComputeShader.FindKernel("CSMain");
            int groupsX = Mathf.CeilToInt(positionMap.width  / (float)COMPUTE_THREADS_X);
            int groupsY = Mathf.CeilToInt(positionMap.height / (float)COMPUTE_THREADS_Y);

            for (int i = 0; i < iterations; i++) {
                _fillPositionSeamsComputeShader.SetTexture(kernel, "PositionMap", source);
                _fillPositionSeamsComputeShader.SetTexture(kernel, "Output", destination);
                _fillPositionSeamsComputeShader.Dispatch(kernel, groupsX, groupsY, 1);

                // Use output as the next source
                (source, destination) = (destination, source);
            }

            // Release and destroy the unused render texture
            if (source == positionMap) {
                tempRT.Release();
                Object.DestroyImmediate(tempRT);

                return positionMap;
            }
            
            positionMap.Release();
            Object.DestroyImmediate(positionMap);

            return source;
        }

        // Check all selected objects and add paintable ones
        public void AddSelected()
        {
            foreach (Object selectedObject in Selection.objects) {
                if (selectedObject is not GameObject) continue;

                GameObject selectedGameObject = (GameObject)selectedObject;
                if (ObjectCanBeSelected(selectedGameObject))
                    Add(selectedGameObject);
            }

            // INSTEAD OF CLEARING, CACHE SELECTION AND RESELECT ON DISABLE
            Selection.objects = new Object[] {};
        }

        public void Remove(GameObject obj)
        {
            if (!_selectedPaintableObjects.Contains(obj))
                return;
            
            _selectedPaintableObjects.Remove(obj);

            // Clear Render Textures
            PaintableObjectData objectData = _paintableObjectData[obj];
            foreach (RenderTexture rt in objectData.RenderTextures)
                rt.Release();
            objectData.HolesRenderTexture.Release();

            RepetitionlessMaterialDataSO materialPropertiesSO = objectData.DataManager.LoadAsset<RepetitionlessMaterialDataSO>(Constants.PROPERTIES_FILE_NAME);
            materialPropertiesSO.OnExternalDataChanged -= objectData.DataChangedAction;

            _paintableObjectData.Remove(obj);
        }

        // Removes all objects that have been deleted from the painted list
        public void RemoveNull()
        {
            List<GameObject> destroyedObjects = _selectedPaintableObjects.Where(obj => obj == null).ToList();
            foreach (GameObject gameObject in destroyedObjects)
                Remove(gameObject);
        }

        public void RemoveAll()
        {
            // Loop backwards to allow removing elements during loop
            for (int i = _selectedPaintableObjects.Count - 1; i >= 0; i--)
                Remove(_selectedPaintableObjects[i]);
        }

        private bool ObjectCanBeSelected(Collider hitCollider)
        {
            bool objectCanBeSelectedInner = ObjectCanBeSelectedInner(hitCollider.gameObject);
            if (!objectCanBeSelectedInner) return false;

            // Must be mesh collider to have proper uvs
            // Need to add some sort of warning
            if (hitCollider is not MeshCollider meshCollider || meshCollider.sharedMesh == null) {
                if (objectCanBeSelectedInner) {
                    EditorUtility.DisplayDialog("Object Requires Changes To Paint", "This object has a valid material, but to paint it, it needs a mesh collider so it has proper UVs. Please change the collider to a mesh collider and if you need another collider change it back after painting.", "Ok");
                }

                return false;
            }

            return true;
        }

        private bool ObjectCanBeSelected(GameObject obj)
        {
            // Must be mesh collider to have proper uvs
            // Need to add some sort of warning
            MeshCollider meshCollider = null;
            obj.TryGetComponent(out meshCollider);
            if (meshCollider == null || meshCollider.sharedMesh == null)
                return false;

            return ObjectCanBeSelectedInner(obj);
        }

        private bool ObjectCanBeSelectedInner(GameObject obj)
        {
            // Mesh must have a repetitionless material
            MeshRenderer meshRenderer;
            obj.TryGetComponent(out meshRenderer);
            if (meshRenderer == null) return false;

            Material repetitionlessMaterial = RepetitionlessLayeredMaterialUtilities.GetFirstLayeredMaterial(meshRenderer);

            // If the repetitionless material is using the terrain shader, dont allow either
            // Need to add a message to change
            if (repetitionlessMaterial == null) return false;

            return true;
        }

        private void MaterialExternalDataChanged(GameObject obj)
        {
            PaintableObjectData objectData = _paintableObjectData[obj];
            RepetitionlessLayeredDataSO layeredDataSO = objectData.DataManager.LoadAsset<RepetitionlessLayeredDataSO>(Constants.LAYERED_DATA_FILE_NAME);

            // If max layers is changed
            if (layeredDataSO.MaxLayers != objectData.MaxLayers)
                objectData.MaxLayers = layeredDataSO.MaxLayers;
                
        }
    }
}
#endif