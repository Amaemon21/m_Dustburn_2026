using System;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

using Repetitionless.Runtime.Utilities;

// Set the control render textures to the material directly
// Better than trying to fiddle with saving the textures
// Also allows me to instance the material and have different textures for different terrains

// Updates material textures when:
// OnEnable (Runtime & Editor)
// Scene saved
// Undo/Redo

namespace Repetitionless.Runtime
{
    /// <summary>
    /// Handles Repetitionless materials interfacing with a terrain, automatically updating terrain textures and syncing the terrain layers to the material
    /// </summary>
    [ExecuteInEditMode]
    [RequireComponent(typeof(Terrain))]
    public class RepetitionlessTerrain : MonoBehaviour
    {
        private const int CONTROL_TEXTURE_COUNT = 8;

        [SerializeField] private Material _mainMaterial;

        /// <summary>
        /// The main material set in the inspector
        /// </summary>
        public Material MainMaterial => _mainMaterial;
        
        private Material _defaultTerrainMaterial;

        private Material _materialInstance;

        /// <summary>
        /// The instance of the material the terrain is using
        /// </summary>
        public Material MaterialInstance => _materialInstance;

        /// <summary>
        /// If modifying the terrain layers will automatically update the material
        /// </summary>
        public bool AutoSaveTextures = true;

        private Terrain _terrain;

        /// <summary>
        /// The terrain being used
        /// </summary>
        public Terrain Terrain { get {
                if (_terrain == null && this != null)
                    TryGetComponent<Terrain>(out _terrain);
                
                return _terrain;
            }
        }

        private TerrainData _terrainData => Terrain?.terrainData;

        /// <summary>
        /// The parent terrain that is handling this terrains material
        /// </summary>
        [SerializeField] public RepetitionlessTerrain ParentTerrain;

        /// <summary>
        /// Callback when the terrain layers are changed
        /// </summary>
        public Action<TerrainLayer[]> OnTerrainLayersChanged;

        /// <summary>
        /// Callback when the material is changed
        /// </summary>
        public Action<Material> OnMaterialAssigned;

        /// <summary>
        /// Callback when the material textures are updated
        /// </summary>
        public Action OnMaterialTexturesUpdated;

    #if UNITY_EDITOR
        // Set this as dirty so saving after doing nothing will still update the textures
        private void SetDirty()
        {
            // this != null to prevent error on end of build
            if (this != null)         EditorUtility.SetDirty(this);
            if (Terrain != null)      EditorUtility.SetDirty(Terrain);
            if (_terrainData != null) EditorUtility.SetDirty(_terrainData);
        }

        private void OnSceneSaved(Scene scene)
        {
            UpdateMaterialTerrainTextures();

    #if UNITY_EDITOR
            if (Terrain.materialTemplate != MaterialInstance || MaterialInstance.shader.name == "Hidden/InternalErrorShader")
                UpdateTerrainMaterial(MainMaterial);
            else if (_mainMaterial != null)
                _materialInstance.CopyPropertiesFromMaterial(_mainMaterial);
    #endif
        }
    #endif

        void OnEnable()
        {
            _defaultTerrainMaterial = RenderPipelineUtilities.GetDefaultTerrainMaterial();

    #if UNITY_EDITOR
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            EditorSceneManager.sceneSaved += OnSceneSaved;

            if (ParentTerrain != null) {
                ParentTerrain.OnTerrainLayersChanged    -= ParentTerrainLayersChanged;
                ParentTerrain.OnMaterialAssigned        -= ParentMaterialChanged;
                ParentTerrain.OnMaterialTexturesUpdated -= ParentMaterialTexturesUpdated;

                ParentTerrain.OnTerrainLayersChanged    += ParentTerrainLayersChanged;
                ParentTerrain.OnMaterialAssigned        += ParentMaterialChanged;
                ParentTerrain.OnMaterialTexturesUpdated += ParentMaterialTexturesUpdated;
            }

            // Set dirty and update textures on next editor frame
            // For some reason doing it immediately doesnt work
            EditorApplication.delayCall += () => {
                SetDirty();
                UpdateMaterialTerrainTextures();
                AssignMaterialInstance();

                // Update the material if required
                if (Terrain.materialTemplate != MaterialInstance || MaterialInstance.shader.name == "Hidden/InternalErrorShader")
                    UpdateTerrainMaterial(MainMaterial);
                else if (_mainMaterial != null)
                    _materialInstance.CopyPropertiesFromMaterial(_mainMaterial);
            };
    #else
            UpdateMaterialTerrainTextures();
    #endif
        }

        void OnDisable()
        {
    #if UNITY_EDITOR
            EditorSceneManager.sceneSaved -= OnSceneSaved;

            if (ParentTerrain != null) {
                ParentTerrain.OnTerrainLayersChanged    -= ParentTerrainLayersChanged;
                ParentTerrain.OnMaterialAssigned        -= ParentMaterialChanged;
                ParentTerrain.OnMaterialTexturesUpdated -= ParentMaterialTexturesUpdated;
            }

            // Switch to default terrain material
            _terrain.materialTemplate = _defaultTerrainMaterial;
    #endif
        }

        /// <summary>
        /// Creates a new material instance and updates the terrain
        /// </summary>
        /// <param name="material">
        /// The material that will be instanced
        /// </param>
        /// <param name="assignMaterial">
        /// If the material instance should be assigned to the terrain
        /// </param>
        public void UpdateTerrainMaterial(Material material, bool assignMaterial = true)
        {
            _mainMaterial = material;

            // Use an instance to support multiple terrain objects
            if (_materialInstance != null)
                DestroyImmediate(_materialInstance);

            if (material != null) {
                _materialInstance = new Material(_mainMaterial);
                _materialInstance.name += " (Instance)";
                _materialInstance.CopyPropertiesFromMaterial(_mainMaterial);
            } else {
                _materialInstance = null;
            }

            if (assignMaterial)
                AssignMaterialInstance();
        }

        /// <summary>
        /// Assigns the currently used material instance to the terrain
        /// </summary>
        public void AssignMaterialInstance()
        {
            Material assigning = _materialInstance == null ? _defaultTerrainMaterial : _materialInstance;
            Terrain.materialTemplate = assigning;
            OnMaterialAssigned?.Invoke(_mainMaterial);
        }

        /// <summary>
        /// Updates the terrain textures on the material instance
        /// </summary>
        public void UpdateMaterialTerrainTextures()
        {
    #if UNITY_EDITOR
            // Delay call so new properties can apply
            EditorApplication.delayCall += () => {
    #endif

            // this == null to prevent error on end of build
            if (_mainMaterial == null || _terrain == null || _terrainData == null || this == null)
                return;

            if (_materialInstance == null || _materialInstance.shader.name == "Hidden/InternalErrorShader" || Terrain.materialTemplate == null)
                UpdateTerrainMaterial(_mainMaterial);

            // Copy any changed properties from the main material
            _materialInstance.CopyPropertiesFromMaterial(_mainMaterial);

            // Control textures 2-8 are not exposed in the shader graph
            UpdateLayersCount();
            UpdateControlTextures();

    #if UNITY_EDITOR
            SetDirty();
    #endif

            OnMaterialTexturesUpdated?.Invoke();
    #if UNITY_EDITOR
            };
    #endif
        }

        /// <summary>
        /// Updates the layer cound on the material instance
        /// </summary>
        public void UpdateLayersCount()
        {
            _materialInstance.SetFloat("_LayersCount", _terrainData.alphamapLayers);
        }

        /// <summary>
        /// Updates the control textures on the material instance
        /// </summary>
        public void UpdateControlTextures()
        {
            int controlTextureCount = Mathf.CeilToInt(_terrainData.alphamapLayers / 4.0f);
            for (int i = 0; i < CONTROL_TEXTURE_COUNT; i++) {
                Texture2D controlTexture = controlTextureCount > i ? _terrainData.alphamapTextures[i] : null;
                _materialInstance.SetTexture($"_Control{i}", controlTexture);
            }
        }

    #if UNITY_EDITOR
        /// <summary>
        /// Updates the parent callbacks<br />
        /// Assumes this is called from the editor before the current parent is updated
        /// </summary>
        /// <param name="newParent">
        /// The new parent being subscribed to
        /// </param>
        public void UpdateParentCallback(RepetitionlessTerrain newParent)
        {
            ParentTerrain.OnTerrainLayersChanged    -= ParentTerrainLayersChanged;
            ParentTerrain.OnMaterialAssigned        -= ParentMaterialChanged;
            ParentTerrain.OnMaterialTexturesUpdated -= ParentMaterialTexturesUpdated;

            if (newParent != null) {
                newParent.OnTerrainLayersChanged    += ParentTerrainLayersChanged;
                newParent.OnMaterialAssigned        += ParentMaterialChanged;
                newParent.OnMaterialTexturesUpdated += ParentMaterialTexturesUpdated;
            }
        }

        private void ParentTerrainLayersChanged(TerrainLayer[] newTerrainLayers)
        {
            _terrainData.terrainLayers = newTerrainLayers;
        }

        /// <summary>
        /// Updates the terrain material and its textures<br />
        /// Called by the parent when its material has changed
        /// </summary>
        /// <param name="material">
        /// The new material to use
        /// </param>
        public void ParentMaterialChanged(Material material)
        {
            UpdateTerrainMaterial(material);
            UpdateMaterialTerrainTextures();
        }

        /// <summary>
        /// Updates the material terrain textures<br />
        /// Called by the parent when its material has changed
        /// </summary>
        public void ParentMaterialTexturesUpdated()
        {
            UpdateMaterialTerrainTextures();
        }

        /// <summary>
        /// Sets up a terrain neighbour with a RepetitionlessTerrain component with this terrain as its parent<br />
        /// Called via the editor when a terrain is created beside this one
        /// </summary>
        /// <param name="newNeighbour">
        /// The new terrain to setup
        /// </param>
        public void SetupNewTerrainNeighbour(Terrain newNeighbour)
        {
            RepetitionlessTerrain repetitionlessTerrain;
            newNeighbour.TryGetComponent<RepetitionlessTerrain>(out repetitionlessTerrain);

            // Already setup
            if (repetitionlessTerrain != null && repetitionlessTerrain.ParentTerrain == this)
                return;

            // Create terrain component
            if (repetitionlessTerrain == null)
                repetitionlessTerrain = newNeighbour.gameObject.AddComponent<RepetitionlessTerrain>();

            // Set parent
            RepetitionlessTerrain newParent = this;
            if (ParentTerrain != null)
                newParent = ParentTerrain;
            
            repetitionlessTerrain.ParentTerrain = newParent;

            // Set terrain layers and material
            repetitionlessTerrain.Terrain.terrainData.terrainLayers = _terrainData.terrainLayers;
            repetitionlessTerrain.AutoSaveTextures = false;
            repetitionlessTerrain.UpdateTerrainMaterial(_mainMaterial);
        }
    #endif
    }
}