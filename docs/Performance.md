# パフォーマンスガイド

視界
- SmoothFan: 三角メッシュ（rayCount/step 依存）。輪郭が滑らか、描画コストは中。
- CellFan: セル単位 Quad 描画（Quad プール済み）。セル数に比例、GCは抑制済み。
- GridAligned: 判定は軽い（描画は CellFan 同等）。（推奨）

調整パラメータ
- GuardController: visionRayCount, visionRayStep, viewRange, fovAngle, visionOriginForwardOffset
- BoardManager: guardVisionUpdateInterval, smoothGuardMove, guardMoveCellsPerSec

プロファイル
- Window > Analysis > Profiler
- GC Alloc が増えていないか（CellFan はプール化により 0 付近が目標）
- CPU Timeline: UpdateVisionOverlay の頻度に注意