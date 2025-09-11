using UnityEngine;

public partial class BoardManager
{
    /// <summary>
    /// RotationEngine 等に渡すための盤面スナップショットを生成する。
    /// </summary>
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

    /// <summary>
    /// 指定エリアに Exit が含まれるか（ロジック側で判定）。
    /// </summary>
    private bool EngineAreaHasExit(Vector2Int center, int size)
        => RotationEngine.AreaHasExit(SnapshotGridModel(), center, size);

    /// <summary>
    /// 90度回転した座標を返す（dir&gt;0=CW, dir&lt;0=CCW）。
    /// </summary>
    private Vector2Int EngineRot90(Vector2Int p, Vector2Int c, int dir)
        => RotationEngine.Rot90(p, c, dir);

    /// <summary>
    /// 180度回転した座標を返す。
    /// </summary>
    private Vector2Int EngineRot180(Vector2Int p, Vector2Int c)
        => RotationEngine.Rot180(p, c);
}
