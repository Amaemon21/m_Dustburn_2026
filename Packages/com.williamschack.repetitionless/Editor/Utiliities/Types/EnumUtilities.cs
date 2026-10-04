#if UNITY_EDITOR
using System;

namespace Repetitionless.Editor.Utilities.Types
{
    public static class EnumUtilities
    {
        public static T Previous<T>(this T value) where T : Enum
        {
            T[] values = (T[])Enum.GetValues(value.GetType());
            int valueIndex = Array.IndexOf(values, value);
            return valueIndex > 0 ? values[valueIndex - 1] : value;
        }

        public static T Next<T>(this T value) where T : Enum
        {
            T[] values = (T[])Enum.GetValues(value.GetType());
            int valueIndex = Array.IndexOf(values, value);
            return valueIndex < values.Length - 1 ? values[valueIndex + 1] : value;
        }
    }
}
#endif