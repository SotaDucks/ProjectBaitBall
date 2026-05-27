#include "../../_Shared/FishFlockShared.cginc"

void ProceduralSetup()
{
    fishProceduralSetup();
}

void Empty_float(in float3 In, out float3 Out) {
    Out = In;
}

void Empty_half(in half3 In, out half3 Out) {
    Out = In;
}