#ifndef GETARRAYASSIGNEDTEXTURES_INCLUDED
#define GETARRAYASSIGNEDTEXTURES_INCLUDED

#include "../Utilities/BooleanCompression.hlsl"

void GetArrayAssignedTextures(
    Texture2D tex,
    
    out int AssignedAVTextures[3],
    out int AssignedNSOTextures[3],
    out int AssignedEMTextures[3],
    out int AssignedBMTextures
){
    // Assigned array textures stored in two pixels, 16bits each from 0-1
    // Reconstruct into 32bit integers

    // * 65535 for 0-1 colour to 0-16bit int
    // + 0.5 for float precision errors
    int3 assignedAV0  = (int3)(tex.Load(int3(0, 0, 0)).rgb * 65535 + 0.5);
    int3 assignedAV1  = (int3)(tex.Load(int3(1, 0, 0)).rgb * 65535 + 0.5);
    int3 assignedNSO0 = (int3)(tex.Load(int3(0, 1, 0)).rgb * 65535 + 0.5);
    int3 assignedNSO1 = (int3)(tex.Load(int3(1, 1, 0)).rgb * 65535 + 0.5);
    int3 assignedEM0  = (int3)(tex.Load(int3(0, 2, 0)).rgb * 65535 + 0.5);
    int3 assignedEM1  = (int3)(tex.Load(int3(1, 2, 0)).rgb * 65535 + 0.5);
    int3 assignedBM   = (int3)(tex.Load(int3(0, 3, 0)).rgb * 65535 + 0.5);

    AssignedAVTextures[0] = Combine16BitInts(assignedAV0.r, assignedAV1.r);
    AssignedAVTextures[1] = Combine16BitInts(assignedAV0.g, assignedAV1.g);
    AssignedAVTextures[2] = Combine16BitInts(assignedAV0.b, assignedAV1.b);

    AssignedNSOTextures[0] = Combine16BitInts(assignedNSO0.r, assignedNSO1.r);
    AssignedNSOTextures[1] = Combine16BitInts(assignedNSO0.g, assignedNSO1.g);
    AssignedNSOTextures[2] = Combine16BitInts(assignedNSO0.b, assignedNSO1.b);

    AssignedEMTextures[0] = Combine16BitInts(assignedEM0.r, assignedEM1.r);
    AssignedEMTextures[1] = Combine16BitInts(assignedEM0.g, assignedEM1.g);
    AssignedEMTextures[2] = Combine16BitInts(assignedEM0.b, assignedEM1.b);

    // Only needs 16 bits
    AssignedBMTextures = assignedBM.r;
}

#endif