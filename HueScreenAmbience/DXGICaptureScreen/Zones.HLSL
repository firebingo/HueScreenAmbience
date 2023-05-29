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
    float4 values;
}

Texture2D objTexture : TEXTURE : register(t0);
SamplerState objSamplerState : SAMPLER : register(s0);

float4 main(PS_INPUT input) : SV_TARGET
{
    float3 pixelColor = objTexture.Sample(objSamplerState, input.inTexCoord);
    return float4(pixelColor, 1.0f);
    //return float4(values.x, values.x, values.x, 1.0f);
    //return float4(1.0f, 0.0f, 1.0f, 1.0f);
}