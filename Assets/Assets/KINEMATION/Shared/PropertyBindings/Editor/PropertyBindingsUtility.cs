// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using KINEMATION.Shared.PropertyBindings.Runtime;
using UnityEditor.Animations;
using UnityEngine;

namespace KINEMATION.Shared.PropertyBindings.Editor
{
    public struct ComponentBinding
    {
        public Behaviour context;
        public string path;

        public ComponentBinding(Behaviour context, string path)
        {
            this.context = context;
            this.path = path;
        }
    }
    
    public struct BindableSearchData
    {
        public MonoBehaviour context;
        public Type propertyType;
        public FieldInfo fieldInfo;
        public List<ComponentBinding> bindings;
    }
    
    public class PropertyBindingsUtility
    {
        private readonly struct CachedMember
        {
            public readonly string name;
            public readonly string oldName;
            public readonly Type type;
            public readonly FieldInfo bindableField;

            public CachedMember(string name, string oldName, Type type, FieldInfo bindableField)
            {
                this.name = name;
                this.oldName = oldName;
                this.type = type;
                this.bindableField = bindableField;
            }
        }

        private static readonly string[] BannedNamespaces =
        {
            "System",
            "UnityEngine",
            "UnityEditor",
        };

        private static readonly Dictionary<Type, CachedMember[]> MemberCache
            = new Dictionary<Type, CachedMember[]>();

        private static bool IsBannedNamespace(Type type)
        {
            string typeNamespace = type?.Namespace;
            if (string.IsNullOrEmpty(typeNamespace)) return false;

            foreach (string bannedNamespace in BannedNamespaces)
            {
                if (typeNamespace.Equals(bannedNamespace, StringComparison.Ordinal)
                    || typeNamespace.StartsWith(bannedNamespace + ".", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAllowedFrameworkType(Type type)
        {
            return type == typeof(Transform)
                   || type == typeof(Vector4)
                   || type == typeof(Vector3)
                   || type == typeof(Vector2)
                   || type == typeof(Quaternion);
        }

        private static bool CanCacheMember(MemberInfo memberInfo)
        {
            Type declaringType = memberInfo.DeclaringType;
            return declaringType == null || !IsBannedNamespace(declaringType)
                                         || IsAllowedFrameworkType(declaringType);
        }

        private static bool CanTraverseType(Type type)
        {
            if (type == null || type.IsPrimitive || type.IsEnum || type == typeof(string)
                             || type.ContainsGenericParameters)
            {
                return false;
            }

            if (IsBannedNamespace(type) && !IsAllowedFrameworkType(type)) return false;

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                return typeof(Component).IsAssignableFrom(type)
                       || typeof(GameObject).IsAssignableFrom(type)
                       || typeof(ScriptableObject).IsAssignableFrom(type);
            }

            return true;
        }

        private static CachedMember[] GetBindableMembers(Type objType)
        {
            if (MemberCache.TryGetValue(objType, out CachedMember[] cachedMembers)) return cachedMembers;

            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            var members = new List<CachedMember>();

            foreach (PropertyInfo property in objType.GetProperties(flags))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0
                                      || !CanCacheMember(property)) continue;

                string oldName = property.GetCustomAttribute<FormerlyBoundAsAttribute>()?.oldName;
                members.Add(new CachedMember(property.Name, oldName, property.PropertyType, null));
            }

            foreach (FieldInfo field in objType.GetFields(flags))
            {
                if (!CanCacheMember(field)) continue;

                bool isBindableField = field.FieldType.IsGenericType
                                       && field.FieldType.GetGenericTypeDefinition() == typeof(BindableProperty<>);
                string oldName = field.GetCustomAttribute<FormerlyBoundAsAttribute>()?.oldName;
                members.Add(new CachedMember(field.Name, oldName, field.FieldType,
                    isBindableField ? field : null));
            }

            foreach (MethodInfo method in objType.GetMethods(flags))
            {
                if (method.ReturnType == objType || method.GetParameters().Length > 0
                                                 || method.ReturnType == typeof(void)
                                                 || method.IsSpecialName
                                                 || !CanCacheMember(method))
                {
                    continue;
                }

                string oldName = method.GetCustomAttribute<FormerlyBoundAsAttribute>()?.oldName;
                members.Add(new CachedMember(method.Name, oldName, method.ReturnType, null));
            }

            cachedMembers = members.ToArray();
            MemberCache.Add(objType, cachedMembers);
            return cachedMembers;
        }

        private static bool TryGetMember(Type type, string name, out CachedMember member)
        {
            foreach (CachedMember candidate in GetBindableMembers(type))
            {
                if (candidate.name == name || candidate.oldName == name)
                {
                    member = candidate;
                    return true;
                }
            }

            member = default;
            return false;
        }

        public static bool IsPropertyResolved(Type contextType, Type propertyType, string propertyPath)
        {
            if (contextType == null || propertyType == null || string.IsNullOrEmpty(propertyPath)) return false;

            Type currentType = contextType;
            foreach (string memberName in propertyPath.Split('.'))
            {
                if (!TryGetMember(currentType, memberName, out CachedMember member)) return false;
                currentType = member.type;
            }

            return currentType == propertyType;
        }

        private static void SearchBindableMembers(BindableSearchData data, Type objType, string path,
            FieldInfo excludedField, HashSet<Type> activeTypes)
        {
            Type cycleType = objType.IsGenericType ? objType.GetGenericTypeDefinition() : objType;
            if (!activeTypes.Add(cycleType)) return;

            try
            {
                foreach (CachedMember member in GetBindableMembers(objType))
                {
                    string memberPath = string.IsNullOrEmpty(path) ? member.name : $"{path}.{member.name}";
                    FieldInfo memberExcludedField = member.bindableField ?? excludedField;

                    if (member.type == data.propertyType)
                    {
                        if (memberExcludedField != data.fieldInfo)
                        {
                            data.bindings.Add(new ComponentBinding(data.context, memberPath));
                        }

                        continue;
                    }

                    if (!CanTraverseType(member.type)) continue;

                    SearchBindableMembers(data, member.type, memberPath, memberExcludedField, activeTypes);
                }
            }
            finally
            {
                activeTypes.Remove(cycleType);
            }
        }

        public static void SearchBindableMembers(BindableSearchData data, Type objType, string path)
        {
            if (data.propertyType == null || objType == null) return;

            SearchBindableMembers(data, objType, path, null, new HashSet<Type>());
        }

        public static void SearchAnimatorParameters(BindableSearchData data, Type propertyType, 
            RuntimeAnimatorController controller = null)
        {
            Animator animator = null;
            AnimatorController animatorController = controller as AnimatorController;
            if (animatorController == null)
            {
                animator = data.context.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    Debug.LogWarning("Animator not found!");
                    return;
                }

                animatorController = animator.runtimeAnimatorController as AnimatorController;
                if (animatorController == null)
                {
                    if (animator.runtimeAnimatorController is not AnimatorOverrideController overrideController)
                    {
                        return;
                    }

                    animatorController = overrideController.runtimeAnimatorController as AnimatorController;
                    if (animatorController == null) return;
                }
            }
            
            foreach (var parameter in animatorController.parameters)
            {
                string paramName = $"Animator.{parameter.name}";

                if (propertyType == typeof(float) && parameter.type == AnimatorControllerParameterType.Float
                    || propertyType == typeof(int) && parameter.type == AnimatorControllerParameterType.Int
                    || propertyType == typeof(bool) && parameter.type == AnimatorControllerParameterType.Bool)
                {
                    data.bindings.Add(new ComponentBinding(animator, paramName));
                }
            }
        }
    }
}