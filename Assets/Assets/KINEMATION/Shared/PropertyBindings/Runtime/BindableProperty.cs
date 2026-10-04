// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;

namespace KINEMATION.Shared.PropertyBindings.Runtime
{
    public class BindableUtility
    {
        public static T GetMember<T>(Type type, string memberName, BindingFlags flags) where T : MemberInfo
        {
            T member = type.GetMember(memberName, flags).OfType<T>().FirstOrDefault();
            if (member != null) return member;

            var allMembers = type.GetMembers(flags);
            foreach (var candidate in allMembers)
            {
                var attribute = candidate.GetCustomAttribute<FormerlySerializedAsAttribute>();
                if (attribute != null && attribute.oldName == memberName) return candidate as T;
                
                var bindableAttribute = candidate.GetCustomAttribute<FormerlyBoundAsAttribute>();
                if (bindableAttribute != null && bindableAttribute.oldName == memberName) return candidate as T;
            }

            return null;
        }
    }
    
    [MovedFrom("KINEMATION.PropertyBindings.Runtime")]
    [Serializable]
    public class BindableProperty<T>
    {
        public string PropertyPath => propertyPath;
        public int AnimatorHash => _animatorHash;
        public bool IsBound => isBound;

        [SerializeField] private string objectType;
        [SerializeField] private string propertyPath;
        
        [SerializeField] private T defaultValue = default(T);
        [SerializeField] private bool isBound;

        [SerializeField] private string friendlyObjectType;
        private Func<object, T> _getter;
        private Component _context;

        private Type _contextType;

        private Animator _animator;
        private int _animatorHash;

        private static readonly Dictionary<(Type, string), Func<object, T>> GetterCache =
            new Dictionary<(Type, string), Func<object, T>>();

        public BindableProperty<T> GetCopy()
        {
            BindableProperty<T> copy = new BindableProperty<T>(defaultValue)
            {
                objectType = objectType,
                propertyPath = propertyPath,
                isBound = isBound,
                _getter = _getter,
                _context = _context,
                _contextType = _contextType,
                _animatorHash = _animatorHash
            };

            return copy;
        }

        public BindableProperty<T> CreateProperty(GameObject context)
        {
            if (!isBound) return this;
            
            var newProperty = GetCopy();
            
            if (_context == null)
            {
                // If current property wasn't initialized, try to do it now.
                newProperty.Initialize(context);
            }
            else
            {
                // Update the context otherwise for proper binding.
                newProperty.UpdateContext(context);
            }

            return newProperty;
        }
        
        public BindableProperty(T defaultValue)
        {
            this.defaultValue = defaultValue;
        }
        
        public void SetDefaultValue(T value)
        {
            defaultValue = value;
        }
        
        public Type GetPropertyType()
        {
            return typeof(T);
        }

        public Type GetContextType()
        {
            return Type.GetType(objectType);
        }
        
        public bool IsValid() => _getter != null && _context != null;

        public void Bind(MonoBehaviour target, string bindingPath)
        {
            if (target == null)
            {
                Debug.LogWarning($"Bindable Property: Failed to bind {bindingPath}, target is NULL!");
                return;
            }
            
            Bind(target.GetType(), bindingPath);
        }

        public void Bind(Type type, string bindingPath)
        {
            if (type == null || string.IsNullOrEmpty(bindingPath))
            {
                Debug.LogWarning($"Bindable Property: Failed to bind {bindingPath}.");
                return;
            }
            
            objectType = type.AssemblyQualifiedName;
            friendlyObjectType = type.Name;
            propertyPath = bindingPath;

            isBound = true;
        }

        public void UnBind()
        {
            objectType = propertyPath = string.Empty;
            isBound = false;
        }

        public void UpdateContext(GameObject context)
        {
            if (context == null || _contextType == null) return;
            
            _context = context.transform.root.GetComponentInChildren(_contextType);
            
            _animator = _context as Animator;
            if (_animator != null) _animatorHash = Animator.StringToHash(propertyPath);
        }

        public void Initialize(GameObject context)
        {
            if (!isBound) return;
            
            if (string.IsNullOrEmpty(objectType) || string.IsNullOrEmpty(propertyPath) || context == null)
            {
                Debug.LogWarning($"Bindable Property: Failed to initialize: {friendlyObjectType}:{propertyPath}.");
                return;
            }

            _contextType = Type.GetType(objectType);
            
            UpdateContext(context);
            if (_animator != null) return;
            
            if (_context == null)
            {
                Debug.LogWarning($"Bindable Property: No {objectType} found to bind {propertyPath}.");
                return;
            }
            
            var cacheKey = (_contextType, propertyPath);
            
            if (GetterCache.TryGetValue(cacheKey, out var cachedGetter))
            {
                _getter = cachedGetter;
            }
            else
            {
                CreateAccessors(_contextType, propertyPath, out _getter);
                if (_getter != null) GetterCache[cacheKey] = _getter;
            }
        }

        public T GetValue()
        {
            return IsValid() ? _getter(_context) : _animator != null ? GetAnimatorValue() : defaultValue;
        }

        private T GetAnimatorValue()
        {
            Type propertyType = GetPropertyType();
            object result = defaultValue;

            if (propertyType == typeof(float))
            {
                result = _animator.GetFloat(_animatorHash);
            }
            else if (propertyType == typeof(int))
            {
                result = _animator.GetInteger(_animatorHash);
            }
            else if (propertyType == typeof(bool))
            {
                result = _animator.GetBool(_animatorHash);
            }

            return (T) result;
        }
        
        private static Action<object, T> CreateSetter(ParameterExpression instance, MemberExpression member)
        {
            if (member.Member is PropertyInfo propertyInfo)
            {
                if (!propertyInfo.CanWrite || propertyInfo.GetIndexParameters().Length != 0) return null;
            }

            var valueParam = Expression.Parameter(typeof(T), "value");
            var assign = Expression.Assign(member, valueParam);
            
            return Expression.Lambda<Action<object, T>>(assign, instance, valueParam).Compile();
        }

        private static void CreateAccessors(Type type, string propertyPath, out Func<object, T> getter)
        {
            getter = null;
            
            ParameterExpression instanceParam = Expression.Parameter(typeof(object), "instance");
            Expression current = Expression.Convert(instanceParam, type);

            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            string[] pathParts = propertyPath.Split('.');
            
            foreach (var part in pathParts)
            {
                // Try property first
                PropertyInfo propInfo = BindableUtility.GetMember<PropertyInfo>(type, part, flags);
                if (propInfo != null)
                {
                    var propertyAccess = Expression.Property(current, propInfo);
                    current = BindingsUtility.CreateNullSafeExpression(current, propertyAccess, propInfo.PropertyType);
                    type = propInfo.PropertyType;
                    continue;
                }

                // Try field if property is not found
                FieldInfo fieldInfo = BindableUtility.GetMember<FieldInfo>(type, part, flags);
                if (fieldInfo != null)
                {
                    var fieldAccess = Expression.Field(current, fieldInfo);
                    current = BindingsUtility.CreateNullSafeExpression(current, fieldAccess, fieldInfo.FieldType);
                    type = fieldInfo.FieldType;
                    continue;
                }

                // Try method (must be parameterless)
                MethodInfo methodInfo = BindableUtility.GetMember<MethodInfo>(type, part, flags);
                if (methodInfo != null && methodInfo.GetParameters().Length == 0)
                {
                    var methodAccess = Expression.Call(current, methodInfo);
                    current = BindingsUtility.CreateNullSafeExpression(current, methodAccess, methodInfo.ReturnType);
                    type = methodInfo.ReturnType;
                    continue;
                }
                
                return;
            }

            // Final conversion to T
            current = Expression.Convert(current, typeof(T));

            // Compile one final function that directly accesses the value
            var lambda = Expression.Lambda<Func<object, T>>(current, instanceParam);
            getter = lambda.Compile();
        }
    }
}