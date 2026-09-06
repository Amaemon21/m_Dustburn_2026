// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.RetargetPro.Runtime.Features;

using System.Collections.Generic;
using UnityEngine;

namespace KINEMATION.RetargetPro.Runtime
{
    public class RetargetProComponent
    {
        private GameObject _sourceCharacter;
        private GameObject _targetCharacter;

        private RetargetProfile _profile;
        private List<RetargetFeatureState> _retargetFeatureStates = new List<RetargetFeatureState>();
        private List<RetargetFeature> _retargetFeatures = new List<RetargetFeature>();
        
        public bool drawBoneGizmos = true;
        public bool drawTransformHandleGizmos = true;
#if UNITY_EDITOR
        private readonly List<RetargetFeatureInitializationMessage> _initializationMessages =
            new List<RetargetFeatureInitializationMessage>();

        public IReadOnlyList<RetargetFeatureInitializationMessage> InitializationMessages => _initializationMessages;
#endif

        private bool _initialized;

        public void Initialize(GameObject source, GameObject target, RetargetProfile profile, KRigComponent sourceComponent,
            KRigComponent targetComponent)
        {
            if (_initialized)
            {
                foreach (RetargetFeatureState state in _retargetFeatureStates) state.OnDestroy();
            }

            _initialized = false;
            _sourceCharacter = source;
            _targetCharacter = target;
            _profile = profile;
            _retargetFeatureStates.Clear();
            _retargetFeatures.Clear();
#if UNITY_EDITOR
            _initializationMessages.Clear();
#endif

            if (source == null || target == null || profile == null)
            {
                _initialized = false;
                return;
            }

            if (sourceComponent == null || targetComponent == null)
            {
                _initialized = false;
                return;
            }
            
            if (_profile.sourcePose != null)
            {
                _profile.sourcePose.SampleAnimation(_sourceCharacter, 0f);
            }
            
            if (_profile.targetPose != null)
            {
                _profile.targetPose.SampleAnimation(_targetCharacter, 0f);
            }
            
            sourceComponent.Initialize();
            targetComponent.Initialize();

            if (_profile.retargetFeatures == null)
            {
                _initialized = true;
                return;
            }

            foreach (var feature in _profile.retargetFeatures)
            {
                if (feature == null) continue;
                RetargetFeatureState retargetFeatureState = feature.CreateFeatureState();
                if(retargetFeatureState == null) continue;
                retargetFeatureState.InitializeComponents(sourceComponent, targetComponent, feature);
#if UNITY_EDITOR
                feature.CollectInitializationMessages(retargetFeatureState, _initializationMessages);
#endif
                _retargetFeatureStates.Add(retargetFeatureState);
                _retargetFeatures.Add(feature);
            }

            _initialized = true;
        }

        public void Initialize(GameObject source, GameObject target, RetargetProfile profile)
        {
            KRigComponent sourceComponent = source != null ? source.GetComponentInChildren<KRigComponent>() : null;
            KRigComponent targetComponent = target != null ? target.GetComponentInChildren<KRigComponent>() : null;
            
            Initialize(source, target, profile, sourceComponent, targetComponent);
        }

        public void RetargetTransforms(float time = 0f)
        {
            if (!_initialized) return;
            foreach (var retargetFeatureState in _retargetFeatureStates)
            {
                if(retargetFeatureState.IsValid()) retargetFeatureState.Retarget(time);
            }
        }

        public void DestroyRetargetFeatures()
        {
            if (!_initialized) return;
            foreach (var retargetFeatureState in _retargetFeatureStates) retargetFeatureState.OnDestroy();
            _retargetFeatureStates.Clear();
            _retargetFeatures.Clear();
            _initialized = false;
        }
        
#if UNITY_EDITOR
        public void OnSceneGUI()
        {
            if (!_initialized) return;
            
            for (int i = 0; i < _retargetFeatureStates.Count; i++)
            {
                var state = _retargetFeatureStates[i];
                RetargetFeature feature = _retargetFeatures[i];
                if (feature == null) continue;
                if (!feature.drawGizmos || !state.IsValid()) continue;
                
                bool drawBoneRendering = drawBoneGizmos;
                bool drawTransformHandles = drawTransformHandleGizmos;
                if (!drawBoneRendering && !drawTransformHandles) continue;

                if (drawBoneRendering && drawTransformHandles)
                {
                    state.OnSceneGUI();
                }
                else
                {
                    state.OnSceneGUI(drawBoneRendering, drawTransformHandles);
                }
            }
        }
#endif
    }
}
