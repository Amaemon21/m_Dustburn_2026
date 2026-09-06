// ---------------------------------------------------
// | Script created by me taken from my other asset: |
// | Texture Array Essentials (https://u3d.as/3s4d)  |
// ---------------------------------------------------

using UnityEngine;

namespace Repetitionless.Runtime.Compression
{
    /// <summary>
    /// Used to compress and extract an array of boolean values in a single integer
    /// </summary>
    public static class BooleanCompression
    {
        /// <summary>
        /// The max values that can be stored in one compressed integer (32-bit integer)
        /// </summary>
        public const int MAX_VALUES = 32;

        /// <summary>
        /// Compresses the input array of bools into an int
        /// </summary>
        /// <param name="values">
        /// Bools that will be compressed
        /// </param>
        /// <returns>
        /// Compressed int of bools
        /// </returns>
        public static int CompressValues(params bool[] values)
        {
            if (values.Length > MAX_VALUES)
                Debug.LogWarning($"More than {MAX_VALUES} values detected. Only compressing the first 32");

            int valueCount = Mathf.Min(values.Length, MAX_VALUES);

            int compressedValues = 0;
            for (int i = 0; i < valueCount; i++) {
                if (values[i])
                    compressedValues |= (1 << i);
            }

            return compressedValues;
        }

        /// <summary>
        /// Retrieves all the boolean values stored in a compressed int
        /// </summary>
        /// <param name="compressedValues">
        /// Compressed int of bool values
        /// </param>
        /// <param name="valueCount">
        /// The total amount of values stored in the compressedValues
        /// </param>
        /// <returns>
        /// Array of all the values stored in the input compressedValues
        /// </returns>
        public static bool[] GetValues(int compressedValues, int valueCount)
        {
            if (valueCount > MAX_VALUES)
                Debug.LogWarning($"Trying to get more than {MAX_VALUES} values. Only returning the first 32");

            valueCount = Mathf.Min(valueCount, MAX_VALUES);
            bool[] values = new bool[valueCount];

            for (int i = 0; i < valueCount; i++) {
                values[i] = (compressedValues & (1 << i)) != 0;
            }

            return values;
        }

        /// <summary>
        /// Overwrites a value at a given index into the input compressedValues
        /// </summary>
        /// <param name="compressedValues">
        /// Compressed int of bool values
        /// </param>
        /// <param name="index">
        /// Index that the input value will overwrite
        /// </param>
        /// <param name="value">
        /// Value inserted into the compressedValue
        /// </param>
        /// <returns>
        /// New compressed integer with the added value
        /// </returns>
        public static int AddValue(int compressedValues, int index, bool value)
        {
            if (index >= MAX_VALUES) {
                Debug.LogError($"Cannot add bool past {MAX_VALUES} values. Returning...");
                return compressedValues;
            }

            if (value) compressedValues |=  (1 << index);
            else       compressedValues &= ~(1 << index);
            return compressedValues;
        }

        /// <summary>
        /// Gets a compressed value at a given index from the input compressedValues
        /// </summary>
        /// <param name="compressedValues">
        /// Compressed int of bool values
        /// </param>
        /// <param name="index">
        /// Index of which bool value will be returned
        /// </param>
        /// <returns>
        /// Value at the given index
        /// </returns>
        public static bool GetValue(int compressedValues, int index)
        {
            if (index >= MAX_VALUES) {
                Debug.LogError($"Cannot get bool past {MAX_VALUES} values. Returning false...");
                return false;
            }

            return (compressedValues & (1 << index)) != 0;
        }

        /// <summary>
        /// Splits a 32 bit integer into two 16 bit integers
        /// </summary>
        /// <param name="value">
        /// The integer to split
        /// </param>
        /// <returns>
        /// Item1: First half<br />
        /// Item2: Second half
        /// </returns>
        public static (ushort, ushort) Split32BitInt(int value)
        {
            return (
                (ushort)(value & 0xFFFF),
                (ushort)((value >> 16) & 0xFFFF)
            );
        }

        /// <summary>
        /// Combines two 16 bit integers into a 32 bit integer
        /// </summary>
        /// <param name="firstHalf">
        /// The first integer
        /// </param>
        /// <param name="secondHalf">
        /// The second integer
        /// </param>
        /// <returns>
        /// The combined 32 bit integer
        /// </returns>
        public static int Combine16BitInts(ushort firstHalf, ushort secondHalf)
        {
            return (((int)firstHalf) & 0xFFFF) | ((int)secondHalf << 16);
        }
    }
}