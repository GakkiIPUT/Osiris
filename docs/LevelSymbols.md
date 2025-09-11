# ステージ記号の凡例（LevelParser 連動）

目的: Assets/Level/Stage_txt/*.txt に記載する1文字記号の意味と、追加・変更の窓口。

参照元
- 変換ロジック: Assets/Scripts/Runtime/GamePlay/Board/Parsing/LevelParser.cs
- 生成先: BoardManager.Build / SetLevelFromText

代表例（正としては LevelParser.cs を参照）
- . = 床（walkable）
- # = 壁（block, BlocksVision=true）
- P = プレイヤー
- E = 退出
- G = ガード
- @ = トラップ/特殊床（用途は LevelParser を確認）

追加・変更の手順
1) LevelParser.cs に記号→生成処理を追加
2) Game 内表示・UI（RequiredItemsUI 等）が該当記号に依存していないか確認
3) サンプルマップを1枚用意して Stage_Adding.md にしたがって検証

テスト観点
- InBounds/IsWalkable/BlocksVision の整合
- 生成順（床→壁→アクター）で依存が崩れないこと