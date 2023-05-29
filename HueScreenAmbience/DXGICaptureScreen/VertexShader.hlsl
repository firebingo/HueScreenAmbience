struct VSQuadOut {
    float4 position : SV_POSITION;
    float2 uv: TEXCOORD;
};

VSQuadOut main(uint vertexID : SV_VertexID) {
    VSQuadOut result;
    result.uv = float2((vertexID << 1) & 2, vertexID & 2);
    result.position = float4(result.uv * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
    return result;
}