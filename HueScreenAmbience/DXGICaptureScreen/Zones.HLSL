//Texture2D shaderTexture;
//SamplerState SampleType;
//
//float4 main(float2 locationInSource : TEXCOORD) : SV_TARGET
//{
//	float4 color;
//	color = shaderTexture.Sample(SampleType, locationInSource);
//	return color;
//}


struct PS_INPUT
{
    float4 inPosition : SV_POSITION;
    float2 inTexCoord : TEXCOORD;
};

cbuffer cbInData : register(b0)
{
    int4 values;
}

Texture2D objTexture : TEXTURE : register(t0);
SamplerState objSamplerState : SAMPLER : register(s0);

float4 main(PS_INPUT input) : SV_TARGET
{
    float2 iResolution = float2(2560.0f, 1440.0f);
    float2 size = float2(16.0f / iResolution.x, 9.0f / iResolution.y);
    float2 coordinates = input.inTexCoord.xy / iResolution.xy;
    float2 position = floor(coordinates / size) * size;
    float3 pixelColor = objTexture.Sample(objSamplerState, position);
    //float3 pixelColor = objTexture.Sample(objSamplerState, input.inTexCoord);
    //return float4(values);
    return float4(pixelColor, 1.0f);
    //return float4(values.x, values.x, values.x, 1.0f);
    //return float4(1.0f, 0.0f, 1.0f, 1.0f);
}