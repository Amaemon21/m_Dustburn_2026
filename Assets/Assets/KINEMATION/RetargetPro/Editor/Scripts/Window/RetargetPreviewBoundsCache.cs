// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEngine;

namespace KINEMATION.RetargetPro.Editor.Scripts.Window
{
    internal sealed class RetargetPreviewBoundsCache
    {
        private GameObject _sourceRoot;
        private Bounds _sourceBounds;
        private bool _hasSourceBounds;

        private GameObject _targetRoot;
        private Transform _targetExcludedRoot;
        private Bounds _targetBounds;
        private bool _hasTargetBounds;

        private GameObject _combinedSourceRoot;
        private GameObject _combinedTargetRoot;
        private Transform _combinedExcludedRoot;
        private Bounds _combinedBounds;
        private bool _hasCombinedBounds;

        public void Invalidate()
        {
            _sourceRoot = null;
            _targetRoot = null;
            _targetExcludedRoot = null;
            _combinedSourceRoot = null;
            _combinedTargetRoot = null;
            _combinedExcludedRoot = null;
            _hasSourceBounds = false;
            _hasTargetBounds = false;
            _hasCombinedBounds = false;
        }

        public Bounds GetSourceBounds(GameObject root)
        {
            if (!_hasSourceBounds || _sourceRoot != root)
            {
                _sourceRoot = root;
                _sourceBounds = CalculateModelBounds(root);
                _hasSourceBounds = true;
            }

            return _sourceBounds;
        }

        public Bounds GetTargetBounds(GameObject root, Transform excludedRoot)
        {
            if (!_hasTargetBounds || _targetRoot != root || _targetExcludedRoot != excludedRoot)
            {
                _targetRoot = root;
                _targetExcludedRoot = excludedRoot;
                _targetBounds = CalculateModelBounds(root, excludedRoot);
                _hasTargetBounds = true;
            }

            return _targetBounds;
        }

        public bool TryGetCombinedBounds(GameObject sourceRoot, GameObject targetRoot, Transform excludedRoot,
            out Bounds bounds)
        {
            if (sourceRoot == null && targetRoot == null)
            {
                bounds = default;
                return false;
            }

            if (!_hasCombinedBounds || _combinedSourceRoot != sourceRoot || _combinedTargetRoot != targetRoot ||
                _combinedExcludedRoot != excludedRoot)
            {
                bool hasBounds = false;
                Bounds combined = default;

                if (sourceRoot != null)
                {
                    combined = GetSourceBounds(sourceRoot);
                    hasBounds = true;
                }

                if (targetRoot != null)
                {
                    Bounds targetBounds = GetTargetBounds(targetRoot, excludedRoot);
                    if (hasBounds)
                    {
                        combined.Encapsulate(targetBounds);
                    }
                    else
                    {
                        combined = targetBounds;
                        hasBounds = true;
                    }
                }

                if (!hasBounds)
                {
                    bounds = default;
                    return false;
                }

                _combinedSourceRoot = sourceRoot;
                _combinedTargetRoot = targetRoot;
                _combinedExcludedRoot = excludedRoot;
                _combinedBounds = combined;
                _hasCombinedBounds = true;
            }

            bounds = _combinedBounds;
            return true;
        }

        private static Bounds CalculateModelBounds(GameObject root, Transform excludedRoot = null)
        {
            if (root == null)
            {
                return new Bounds(Vector3.zero, Vector3.one);
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds bounds = default;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Transform rendererTransform = renderer.transform;
                if (excludedRoot != null &&
                    (rendererTransform == excludedRoot || rendererTransform.IsChildOf(excludedRoot)))
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
            }

            if (hasBounds)
            {
                return bounds;
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform child = transforms[i];
                if (child == null || excludedRoot != null &&
                    (child == excludedRoot || child.IsChildOf(excludedRoot)))
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = new Bounds(child.position, Vector3.zero);
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(child.position);
            }

            return hasBounds ? bounds : new Bounds(root.transform.position, Vector3.one);
        }
    }
}
