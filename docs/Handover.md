# 引き継ぎドキュメント（役割と配置の全体像）

この文書は「どこで何をしているか」を素早く俯瞰するためのものです。詳細実装は各ファイルの XML コメント/コードを参照。

## 実行の背骨（Core）

- TurnManager: ターン/フェーズ/ゲームオーバー/クリア制御。外部トリガ（ガード検知）から遷移。
- GameState, GameCatalog: ゲーム進行・カタログ/メタ情報。
- InputBindings, InputSchemeWatcher: 入力バインドとスキーム監視。
- CameraFollow: 追従カメラ。

## 盤面・ゲームプレイ（Board）

- BoardManager（partial）
  - .Build: 盤面生成（アクター生成含む）
  - .ModelBridge: `GridModel` との仲介
  - .Vision: ラインオブサイト/ブロッキング/視界設定
  - .Rotation.Core/.Helpers: 盤面回転のロジック/補助
  - .FreePreview/.EditorPreview: エディタ/プレビュー支援
  - .Items/.Utility: アイテム配置/汎用ユーティリティ
- GridModel: タイル/セルの保持、座標/境界チェック。
- RotationEngine: 盤面回転ルール。
- Parsing/LevelParser: `Assets/Level/Stage_txt` のテキスト → 盤面/アクターに展開。
- Controllers
  - PlayerController: プレイヤー移動/相互作用。
  - ExitController: 退出/クリア条件。
  - GuardController（partial に分割、責務別）
    - Core（GuardController.cs）: Init/Update/StepAI/コアの調停
    - .Patrol: 巡回パス構築/端点折返し/ブロック時処理
    - .Flip: pre-flip（視界ONで停止）→ flip-pause（視界OFFで停止）の状態遷移
    - .Vision: 視界ロジック（FOV/角度/LOS/検知/泥棒変換）
    - .VisionRender: 視界描画（SmoothFan/CellFan/GridAligned、Quad プール/MPB）
    - .Visual: 見た目（スプライト flip/見た目Yaw/矢印/キラー演出/ティント）

補足: Guard のログは `GuardLog`（DEVELOPMENT/Editorのみ出力）

## UI

- Screens/MainMenu, StageSelect, Game: 画面単位の UI ロジック（`GameUI`, `GameFlow`, `GlobalEscMenu`, `RequiredItemsUI`, `ControlsHelpUI` など）
- Framework: `PanelVisibility`, `UICancelBack`, `UIFocusScope`
- Widgets: 入力スキームに応じた可視制御
- Dev: `DevModeUI`

## ステージ/データ

- Assets/Level/Stage_txt: テキストマップ。記号→ゲーム要素は `LevelParser.cs` に定義（凡例・追加時はここを編集）
  - 例: `P`=Player, `E`=Exit, `G`=Guard, `#`=壁, `.`=床 …（正確な対応は LevelParser を参照）
- StageManager: ステージ読み込み/遷移。

## エディタツール

- LevelTools: ツール群のエントリ
- LevelPainter: レベルペインタ（グリッド上に記号を配置→テキスト出力）
- BoardManagerEditor, LevelAssetEditor: Board/Level アセット編集支援

## 典型フロー

- 起動 → `StageManager` がステージ読込 → `LevelParser` がテキストを解釈 → `BoardManager` がグリッドとアクターを生成
- `TurnManager` の tick/Update により `PlayerController`/`GuardController` が動作
- ガードの視界でプレイヤー検知 → `TurnManager.TriggerGameOver`

## デバッグ/運用

- 視界/移動/回転の基本値は BoardManager/GuardController の Inspector で調整
- 視界モード切替: `GuardController.SetVisionMode`
- ログ: `GuardLog.Log/Warn/Error`（本番ビルドでは無効）
- プロファイル観点: CellFan/GridAligned は Quad プール済み（GC 圧抑制）

## 変更に強いポイント

- GuardController の責務分割（partial）で読みやすさ/影響範囲を限定
- 視界描画は MPB/共有マテリアル運用、レンダーキューは必要時のみ変更
- 盤面・回転・視界は `BoardManager.*` に集約