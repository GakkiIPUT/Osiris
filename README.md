# プロジェクト概要
- コンセプト	回して、隠れて、潜り抜けろ。   神の力で部屋を回し、衛兵に見つからずに“オシリ様”（オシリス神）の元へと進む、ステルス×パズル
- ゲーム概要　本作は、“マップを操作する力”で衛兵を避けながら進む非戦闘パズルゲームです。  
　　　　　　　プレイヤーは戦えず、衛兵に見つかるとゲームオーバー。
　　　　　　　その代わり、特定範囲のマップ（3×3、4×4など）を自由に回転できる能力を使って進みます。

回転で動かせるのは「壁・箱・障害物・床」など。
「衛兵」は固定で動かせません。
この制約が、マップパズルの面白さを生み出します。
- グリッドベースのステルス系パズル。テキストマップを `LevelParser` が読み込み、`BoardManager` が盤面・アクターを生成/制御。
- ガードは巡回/監視/視界でプレイヤーを検知。`TurnManager` がターン/フェーズを統括。
- Unity 6 (6000.0.21f1)、.NET Framework 4.7.1。開発環境: Visual Studio 2022 推奨。

## 開発/実行

- Unity でプロジェクトを開く
- シーン: ステージ選択/ゲーム開始を含むシーンを開く（`StageManager` を含むシーン）
- プレイ手順・操作ヒント: `ControlsHelpUI`, `GameUI` を参照

## ディレクトリ/主要モジュール

- Assets/Scripts/Runtime/Core
  - System: `TurnManager`, `GameState`, `GameCatalog`
  - Input: `InputBindings`, `InputSchemeWatcher`
  - Camera: `CameraFollow`
- Assets/Scripts/Runtime/GamePlay/Board
  - BoadManager: `BoardManager.*`（盤面・座標・視界・回転などを partial 分割）
  - Controllers: `PlayerController`, `GuardController.*`（partial 分割）, `ExitController`
  - Model: `GridModel`
  - Rules: `RotationEngine`
  - Parsing: `LevelParser`（テキストマップ→盤面）
  - Overlays: `SelectionFramesOverlay`
  - FreeRotate: `FreeRotateController`
- Assets/Scripts/Runtime/UI
  - Screens: `MainMenu`, `StageSelect`, `Game`（`GameUI`, `GameFlow`, `GlobalEscMenu` など）
  - Framework: 共通 UI ユーティリティ
  - Dev: `DevModeUI`
- Assets/Scripts/Editor/LevelTools
  - `LevelPainter`（レベルエディタ）, `BoardManagerEditor`, `LevelAssetEditor`, `LevelTools`
- Assets/Level/Stage_txt
  - ステージ定義テキスト（`LevelParser` が解釈）

## よく使う実行時パラメータ

- ガード視界/巡回の基本値は `BoardManager` と `GuardController` の Inspector で調整
- ログ: `GuardLog` による条件付きログ（DEVELOPMENT/Editor のみ出力）

詳細な担当と連携は docs/Handover.md を参照。
他にもdocsに入っているので、見てください。