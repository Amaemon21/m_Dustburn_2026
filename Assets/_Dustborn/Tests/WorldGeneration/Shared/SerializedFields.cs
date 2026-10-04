using System;
using System.Reflection;

namespace Dustborn.WorldGen.Testing
{
    public static class SerializedFields
    {
        public static void Set(object target, string property, object value)
        {
            FieldInfo field = target.GetType().GetField($"<{property}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

            if (field == null)
                throw new MissingFieldException(target.GetType().Name, property);

            field.SetValue(target, value);
        }
    }
}
