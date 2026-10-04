// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.RetargetPro.Editor.Scripts.Bakers
{
    public readonly struct RetargetProBakerDescriptor
    {
        public RetargetProBakerDescriptor(string id, string displayName, Type bakerType)
        {
            Id = id;
            DisplayName = displayName;
            BakerType = bakerType;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public Type BakerType { get; }
    }

    public static class RetargetProBakerRegistry
    {
        private static readonly List<RetargetProBakerDescriptor> Bakers = new List<RetargetProBakerDescriptor>();
        private static readonly List<string> BakerDisplayNames = new List<string>();
        private static bool _initialized;

        public static IReadOnlyList<RetargetProBakerDescriptor> GetBakers()
        {
            EnsureInitialized();
            return Bakers;
        }

        public static string[] GetDisplayNames()
        {
            EnsureInitialized();
            return BakerDisplayNames.ToArray();
        }

        public static int GetIndexById(string id)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(id))
            {
                return -1;
            }

            for (int i = 0; i < Bakers.Count; i++)
            {
                if (string.Equals(Bakers[i].Id, id, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        public static string GetIdByIndex(int index)
        {
            EnsureInitialized();
            if (index < 0 || index >= Bakers.Count)
            {
                return string.Empty;
            }

            return Bakers[index].Id;
        }

        public static bool TryCreate(string id, out IRetargetProBaker baker, out string error)
        {
            EnsureInitialized();
            baker = null;

            int index = GetIndexById(id);
            if (index < 0)
            {
                error = string.IsNullOrEmpty(id)
                    ? "Retarget baking is unavailable."
                    : $"Baker with id `{id}` is not registered.";
                return false;
            }

            return TryCreate(Bakers[index].BakerType, out baker, out error);
        }

        public static void Refresh()
        {
            Bakers.Clear();
            BakerDisplayNames.Clear();

            var discovered = new List<RetargetProBakerDescriptor>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IRetargetProBaker>())
            {
                if (type == null || type.IsAbstract || type.IsInterface)
                {
                    continue;
                }

                if (!TryCreate(type, out IRetargetProBaker bakerInstance, out string createError))
                {
                    Debug.LogWarning($"RetargetPro baker registration skipped for `{type.FullName}`: {createError}");
                    continue;
                }

                string id = string.IsNullOrWhiteSpace(bakerInstance.Id) ? type.FullName : bakerInstance.Id.Trim();
                string displayName = string.IsNullOrWhiteSpace(bakerInstance.DisplayName)
                    ? type.Name
                    : bakerInstance.DisplayName.Trim();

                bool duplicate = false;
                for (int i = 0; i < discovered.Count; i++)
                {
                    if (!string.Equals(discovered[i].Id, id, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    duplicate = true;
                    Debug.LogWarning(
                        $"RetargetPro baker registration skipped for `{type.FullName}`: duplicate id `{id}`.");
                    break;
                }

                if (duplicate)
                {
                    continue;
                }

                discovered.Add(new RetargetProBakerDescriptor(id, displayName, type));
            }

            discovered.Sort(CompareBakerDescriptors);
            Bakers.AddRange(discovered);

            for (int i = 0; i < Bakers.Count; i++)
            {
                BakerDisplayNames.Add(Bakers[i].DisplayName);
            }

            if (Bakers.Count == 0)
            {
                Debug.LogError("RetargetPro baker registry found no IRetargetProBaker implementations.");
            }

            _initialized = true;
        }

        private static int CompareBakerDescriptors(RetargetProBakerDescriptor left, RetargetProBakerDescriptor right)
        {
            int leftPriority = GetPriority(left.BakerType);
            int rightPriority = GetPriority(right.BakerType);
            if (leftPriority != rightPriority)
            {
                return leftPriority.CompareTo(rightPriority);
            }

            return string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetPriority(Type bakerType)
        {
            if (bakerType == typeof(GenericAnimationBaker))
            {
                return 0;
            }

            if (bakerType == typeof(HumanoidAnimationBaker))
            {
                return 1;
            }

            return 10;
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            Refresh();
        }

        private static bool TryCreate(Type bakerType, out IRetargetProBaker baker, out string error)
        {
            baker = null;

            try
            {
                object instance = Activator.CreateInstance(bakerType);
                baker = instance as IRetargetProBaker;
                if (baker == null)
                {
                    error = "Type does not implement IRetargetProBaker.";
                    return false;
                }

                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}