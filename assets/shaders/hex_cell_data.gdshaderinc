/* Part 2.2: U 方向 repeat，使环绕接缝处 cell data 采样正确；V 方向实际值始终在 [0,1] 内 */

#include "hex_metrics.gdshaderinc"

global uniform sampler2D hex_cell_data : filter_nearest, repeat_enable;
global uniform vec4 hex_cell_data_texel_size;

/* Part 2.2: 笔刷高亮数据（hex space）
   x = 高亮中心 X
   y = 高亮中心 Z
   z = 高亮半径平方 + 0.5（<0 表示无高亮）
   w = hex grid 环绕尺寸（无环绕时为 0） */
global uniform vec4 _CellHighlighting;

/* 通过一维 cell index 采样（terrain/river/road/water 使用） */
vec4 GetCellData(float cellIndex) {
    vec2 uv;
    uv.x = (cellIndex + 0.5) * hex_cell_data_texel_size.x;
    float row = floor(uv.x);
    uv.x -= row;
    uv.y = (row + 0.5) * hex_cell_data_texel_size.y;
    vec4 data = textureLod(hex_cell_data, uv, 0.0);
    data.a *= 255.0; /* A 通道存储 terrainTypeIndex (0-255)，采样后需还原 */
    return data;
}

/* 通过二维 cell 坐标采样（features / analytical grid 使用） */
vec4 GetCellData(vec2 cellDataCoordinates) {
    vec2 uv = cellDataCoordinates + vec2(0.5, 0.5);
    uv.x *= hex_cell_data_texel_size.x;
    uv.y *= hex_cell_data_texel_size.y;
    return textureLod(hex_cell_data, uv, 0.0);
}

/* Part 2.2: 六角网格数据，从世界坐标 XZ 解析得到。
   注意：distanceSmoothing 不在此处计算，因为 fwidth 只能在 fragment 阶段使用。 */
struct HexGridData {
    // cell 中心（hex space）
    vec2 cellCenter;
    // 近似 offset 坐标，足够用于点采样
    vec2 cellOffsetCoordinates;
    // cell 内部 UV，预留
    vec2 cellUV;
    // 六边形中心到边缘的距离，中心为 0，边缘为 1
    float distanceToCenter;
};

#define HEX_ANGLED_EDGE_VECTOR vec2(1.0, 1.7320508075688772)

float HexagonalCenterToEdgeDistance(vec2 p) {
    p = abs(p);
    float d = dot(p, normalize(HEX_ANGLED_EDGE_VECTOR));
    d = max(d, p.x);
    return 2.0 * d;
}

vec2 HexModulo(vec2 p) {
    return p - HEX_ANGLED_EDGE_VECTOR * floor(p / HEX_ANGLED_EDGE_VECTOR);
}

HexGridData GetHexGridData(vec2 worldPositionXZ) {
    vec2 p = WorldToHexSpace(worldPositionXZ);

    vec2 gridOffset = HEX_ANGLED_EDGE_VECTOR * 0.5;
    vec2 a = HexModulo(p) - gridOffset;
    vec2 b = HexModulo(p - gridOffset) - gridOffset;
    bool aIsNearest = dot(a, a) < dot(b, b);

    vec2 vectorFromCenterToPosition = aIsNearest ? a : b;

    HexGridData d;
    d.cellCenter = p - vectorFromCenterToPosition;
    d.cellOffsetCoordinates.x = d.cellCenter.x - (aIsNearest ? 0.5 : 0.0);
    d.cellOffsetCoordinates.y = d.cellCenter.y / OUTER_TO_INNER;
    d.cellUV = vectorFromCenterToPosition + 0.5;
    d.distanceToCenter = HexagonalCenterToEdgeDistance(vectorFromCenterToPosition);
    return d;
}

/* Part 2.2: HexGridData 辅助函数（GLSL 不支持 struct 成员函数） */
bool IsHighlighted(HexGridData d) {
    vec2 cellToHighlight = abs(_CellHighlighting.xy - d.cellCenter);
    if (cellToHighlight.x > _CellHighlighting.w * 0.5) {
        cellToHighlight.x -= _CellHighlighting.w;
    }
    return dot(cellToHighlight, cellToHighlight) < _CellHighlighting.z;
}

float Smoothstep01(HexGridData d, float threshold) {
    float distanceSmoothing = fwidth(d.distanceToCenter);
    return smoothstep(threshold - distanceSmoothing, threshold + distanceSmoothing, d.distanceToCenter);
}

float Smoothstep10(HexGridData d, float threshold) {
    float distanceSmoothing = fwidth(d.distanceToCenter);
    return smoothstep(threshold + distanceSmoothing, threshold - distanceSmoothing, d.distanceToCenter);
}

float SmoothstepRange(HexGridData d, float innerThreshold, float outerThreshold) {
    return Smoothstep01(d, innerThreshold) * Smoothstep10(d, outerThreshold);
}
