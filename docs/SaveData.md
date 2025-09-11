# SaveData（PlayerPrefs キー）

StageSelectUI
- lastWorldIndex: 最後に選択したワールド（int）
- lastStageIndex: 最後に選択したステージ（int）
- enteredViaStageSelect: ステージ選択から遷移したフラグ（1/0）

備考
- テスト時は PlayerPrefs.DeleteAll() で初期化
- 追加キーを導入する場合は文書に追記（使用箇所/既定値/移行の扱い）