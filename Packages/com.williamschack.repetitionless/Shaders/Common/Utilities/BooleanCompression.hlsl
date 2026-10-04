// ---------------------------------------------------
// | Script created by me taken from my other asset: |
// | Texture Array Essentials (https://u3d.as/3s4d)  |
// ---------------------------------------------------

#ifndef BOOLEANCOMPRESSION_INCLUDED
#define BOOLEANCOMPRESSION_INCLUDED

// Cannot pass any sized array in hlsl, just use a max
// 4 will be enough for most cases
#ifndef BOOLEAN_COMPRESSION_MAX_CHUNKS
#define BOOLEAN_COMPRESSION_MAX_CHUNKS 4
#endif

// Overwrites a value at a given index into the input CompressedValues
int AddCompressedValue(int CompressedValues, bool Value, int Index)
{
    if (Value) CompressedValues |=  (1 << Index);
    else       CompressedValues &= ~(1 << Index);
    return CompressedValues;
}

// Gets a compressed value at a given index from the input CompressedValues
bool GetCompressedValue(int CompressedValues, int Index)
{
    return (CompressedValues & (1 << Index)) != 0;
}

// Each element in the array is a chunk of 32 bools
// Gets a compressed value at a given index from the input CompressedValues
bool GetCompressedValue(int CompressedValues[BOOLEAN_COMPRESSION_MAX_CHUNKS], int Index)
{
    int chunkIndex = floor(Index / 32);
    int chunkCompressedValues = CompressedValues[chunkIndex];
    
    int valueIndex = Index % 32;
    return GetCompressedValue(chunkCompressedValues, valueIndex);
}

// Combines two halfs of a 32 bit integer
int Combine16BitInts(int firstHalf, int secondHalf)
{
    return (((int)firstHalf) & 0xFFFF) | ((int)secondHalf << 16);
}

// Overwrites a value at a given index into the input CompressedValues
void AddCompressedValue_float(int CompressedValues, bool Value, int Index, out int Out)
{
    Out = AddCompressedValue(CompressedValues, Value, Index);
}

// Gets a compressed value at a given index from the input CompressedValues
void GetCompressedValue_float(int CompressedValues, int Index, out bool Out)
{
    Out = GetCompressedValue(CompressedValues, Index);
}

#endif