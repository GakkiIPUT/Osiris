# レベルペインター（LevelPainter）の使い方

対象: `Assets/Scripts/Editor/LevelTools/LevelPainter.cs`

## 起動

- Unity メニュー（MenuItem）から LevelPainter ウィンドウを開く
  - 正確なメニュー階層は `LevelPainter.cs` の `[MenuItem]` 属性を参照

## 基本操作

- パレットから記号（タイル/アクター種）を選択し、グリッドへペイント
- 消しゴム/置換などの編集ツールはウィンドウ内の UI に準拠
- 保存
  - テキストとして出力（`.txt`）し、`Assets/Level/Stage_txt/` 配下へ配置
  - ファイル名は任意（例: `1-11.txt`）。既存と重複しないように

## 記号の意味

- 記号→ゲーム要素の厳密な対応は `LevelParser.cs` を参照
  - よく使う例: `.`=床, `#`=壁, `P`=プレイヤー, `E`=出口, `G`=ガード
  - 泥棒系など記号の追加/変更が必要な場合も `LevelParser.cs` を修正

## プレビュー/検証

- エディタ上で対象シーンを開き、Play
- `StageManager` / `GameCatalog` の設定に応じて新規ステージが読み込まれる
  - 読み込み先の参照が必要な場合は `StageManager` と `GameCatalog` の設定を確認
- 実機テスト
  - 視界/巡回/回転の挙動は `BoardManager`/`GuardController` の Inspector で即時反映

## 運用上の注意

- 既存ステージの編集は VCS で差分が見やすい（テキスト）ため推奨
- 記号の新設は `LevelParser` と UI（`RequiredItemsUI` など）が参照するため、連鎖影響の確認を
- 大きいマップや CellFan 表示は描画負荷に留意（Quad プールで GC は抑制済み）