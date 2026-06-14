/*
 * Part 27: Hex metrics shared across shaders.
 * TILING_SCALE ensures that texture coordinates align with the chunk size,
 * so water/road noise tiles seamlessly when columns are wrapped around.
 */

#define OUTER_TO_INNER 0.866025404
#define OUTER_RADIUS 10.0
#define CHUNK_SIZE_X 5.0
#define TILING_SCALE (1.0 / (CHUNK_SIZE_X * 2.0 * OUTER_RADIUS / OUTER_TO_INNER))

// Part 2.2: 从世界空间 XZ 转换到 hex space，东西相邻 cell 中心距为 1。
vec2 WorldToHexSpace(vec2 p) {
    return p * (1.0 / (2.0 * OUTER_RADIUS * OUTER_TO_INNER));
}
