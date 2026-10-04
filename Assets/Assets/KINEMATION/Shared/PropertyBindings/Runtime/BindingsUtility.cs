// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Linq.Expressions;

namespace KINEMATION.Shared.PropertyBindings.Runtime
{
    public class BindingsUtility
    {
        private static readonly Type UnityObjectType = typeof(UnityEngine.Object);
        
        public static Expression CreateNullSafeExpression(Expression instance, Expression memberAccess, 
            Type memberType)
        {
            if (instance.Type.IsValueType)
            {
                return memberAccess;
            }
            
            Expression defaultValue = Expression.Default(memberType);
            Expression nullCheck = Expression.Equal(instance, Expression.Constant(null, instance.Type));

            // If the instance type is a Unity Object, 
            if (UnityObjectType.IsAssignableFrom(instance.Type))
            {
                nullCheck = Expression.Equal(
                    Expression.Convert(instance, typeof(UnityEngine.Object)),
                    Expression.Constant(null, typeof(UnityEngine.Object))
                );
            }

            // Ensure both return types match (i.e., return either the property/field value or its default)
            return Expression.Condition(nullCheck, defaultValue, memberAccess);
        }
    }
}