using UnityEngine;

public partial class BoardManager
{
    // RotationEngine/LevelParser 等へ渡すためのスナップショット
    private GridModel SnapshotGridModel()
    {
        return new GridModel(
            width: Width,
            height: Height,
            coreOffsetX: coreOffsetX,
            coreOffsetY: coreOffsetY,
            coreWidth: coreWidth,
            coreHeight: coreHeight,
            autoGenerateOuterRings: autoGenerateOuterRings,
            cells: cells,
            wallOrigin: wallOrigin,
            outerRingFloor: outerRingFloor,
            levelOriginalCore: levelOriginalCore
        );
    }

    private bool EngineAreaHasExit(Vector2Int center, int size)
        => RotationEngine.AreaHasExit(SnapshotGridModel(), center, size);

    private Vector2Int EngineRot90(Vector2Int p, Vector2Int c, int dir)
        => RotationEngine.Rot90(p, c, dir);

    private Vector2Int EngineRot180(Vector2Int p, Vector2Int c)
        => RotationEngine.Rot180(p, c);
}
