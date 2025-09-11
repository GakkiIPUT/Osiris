# Runbook（開発/実行の手順）

環境
- Unity 6 (6000.0.21f1), .NET Framework 4.7.1, Visual Studio 2022

起動
- シーン: MainMenu → StageSelect → Game の順で遷移
- 直接検証: Game シーンを開き、StageManager.currentIndex を指定 → Play

主要トグル
- GuardController（Inspector）
  - VisionMode: SmoothFan/CellFan/GridAligned
  - flipPauseSeconds / preFlipHoldSeconds
  - useBoardDefaultViewRange, viewRange, fovAngle
- BoardManager（Inspector）
  - smoothGuardMove, guardMoveCellsPerSec, guardRotateDegPerSec
  - guardVisionUpdateInterval, visionY
- DevModeUI（任意）: デバッグ補助 UI

ログ
- GuardLog（Editor/Development ビルドのみ出力）
- PlayerPrefs リセットで StageSelect の初期値が変わる点に注意

確認フロー（スモーク）
- ステージロード → プレイヤー移動 → ガード視界検知（GameOver） → クリア（Exit）
- WatchMode: OneDir/TwoDir/Rotate4Dir の切替
- VisionMode 切替時の見た目/検知が期待どおり