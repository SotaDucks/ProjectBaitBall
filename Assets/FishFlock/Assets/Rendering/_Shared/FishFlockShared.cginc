#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
    struct FishData
    {
	    float3 position;
	    float3 velocity;
	    float speed;
	    float rot_speed;
	    float speed_offset;
	    float scale;
    };
    StructuredBuffer<FishData> fishBuffer;
    //StructuredBuffer<float4> vertexAnimation; 

    float3 offsetPosition;
    float4x4 lookAtMatrix;
    float3 fishPosition;
    float fishScale;
#endif

//For built-in
float3 AnimateVertex(float3 vertexPos, float _AnimationSpeed, float _Yaw, float _Roll, float _Scale)
{
    return vertexPos + ((sin( ((_Time.w * _AnimationSpeed) + (vertexPos.z * _Yaw) + (vertexPos.y * _Roll)) ) * _Scale) * float3(1, 0, 0));
}

float4x4 lookAt(float3 at, float3 eye, float3 up)
{
    float3 zaxis = normalize(at - eye);
    float3 xaxis = normalize(cross(up, zaxis));
    float3 yaxis = cross(zaxis, xaxis);
    return float4x4
        (
            xaxis.x, yaxis.x, zaxis.x, 0,
            xaxis.y, yaxis.y, zaxis.y, 0,
            xaxis.z, yaxis.z, zaxis.z, 0,
            0, 0, 0, 1
            );
}

float4x4 my_inverse(float4x4 input)
{
#define minor(a,b,c) determinant(float3x3(input.a, input.b, input.c))

    float4x4 cofactors = float4x4(
        minor(_22_23_24, _32_33_34, _42_43_44),
        -minor(_21_23_24, _31_33_34, _41_43_44),
        minor(_21_22_24, _31_32_34, _41_42_44),
        -minor(_21_22_23, _31_32_33, _41_42_43),

        -minor(_12_13_14, _32_33_34, _42_43_44),
        minor(_11_13_14, _31_33_34, _41_43_44),
        -minor(_11_12_14, _31_32_34, _41_42_44),
        minor(_11_12_13, _31_32_33, _41_42_43),

        minor(_12_13_14, _22_23_24, _42_43_44),
        -minor(_11_13_14, _21_23_24, _41_43_44),
        minor(_11_12_14, _21_22_24, _41_42_44),
        -minor(_11_12_13, _21_22_23, _41_42_43),

        -minor(_12_13_14, _22_23_24, _32_33_34),
        minor(_11_13_14, _21_23_24, _31_33_34),
        -minor(_11_12_14, _21_22_24, _31_32_34),
        minor(_11_12_13, _21_22_23, _31_32_33)
        );
#undef minor
    return transpose(cofactors) / determinant(input);
}


void fishProceduralSetup()
{
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
    FishData fish = fishBuffer[unity_InstanceID];

    fishPosition = fish.position;
    lookAtMatrix = lookAt(fishPosition, fishPosition + (fish.velocity * -1), float3(0.0, 1.0, 0.0));
    fishScale = fish.scale;


    unity_ObjectToWorld._11_21_31_41 = float4(fishScale, 0, 0, 0);
    unity_ObjectToWorld._12_22_32_42 = float4(0, fishScale, 0, 0);
    unity_ObjectToWorld._13_23_33_43 = float4(0, 0, fishScale, 0);
    unity_ObjectToWorld._14_24_34_44 = float4(fishPosition, 1);

    unity_ObjectToWorld = mul(unity_ObjectToWorld, lookAtMatrix); // this line
    //unity_WorldToObject = unity_ObjectToWorld;

    unity_WorldToObject = my_inverse(unity_ObjectToWorld);
#endif
}