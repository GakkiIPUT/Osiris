# Troubleshooting

StageSelect にボタンが出ない
- GameState.catalog/worldIndex 未設定
- GameCatalog.worlds[*].stageSet 未割当 or stages 空
- StageSelectUI の grid/stageButtonPrefab 未設定

クリックで別マップが開く
- StageManager.stageSet が別アセットを参照
- StageSet.stages の id と mapTxt の対応ミス

テキストマップが壊れる/エラー
- LevelParser 未定義記号が含まれる → LevelSymbols.md/LevelParser.cs を確認
- 行長が不揃い → パース前に整形するか、Parser 側の行長処理を確認

ガード視界が期待と違う
- GridAligned: 前方のみ/階段扇形。背面や斜めは不可視
- SmoothFan/CellFan: visionOriginForwardOffset/visualYawOffsetDeg の影響を確認
- flip 中は視界OFF、pre-flip 中は視界ON（止まるのみ）

パフォーマンスが重い
- CellFan はセルQuad数に比例。VisionRender はQuadプール済みだが範囲/角度/更新周期を調整
- SmoothFan: visionRayCount/visionRayStep を縮小