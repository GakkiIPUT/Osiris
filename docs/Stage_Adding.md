# ステージ追加の手順（GameCatalog / StageSelect 登録）

目的: 新しいテキストマップをステージとして追加し、ステージ選択画面から選べるようにする。

前提
- マップはテキスト（Assets/Level/Stage_txt/*.txt）
- ステージ集合は StageSet（ScriptableObject）
- カタログは GameCatalog（ScriptableObject）
- ステージ選択は StageSelectUI、読み込みは StageManager が担当

手順

1) テキストマップの作成
- Assets/Level/Stage_txt に新規 .txt を作成（例: 1-11.txt）
- レイアウトは LevelParser の記号に従う（記号の意味は docs/LevelPainter_Usage.md を参照）

2) StageSet へステージを追加
- 既存の StageSet アセット（該当ワールド用）を Project から選択
- stages リストに要素を追加
  - id: ステージID（例: "1-11"。StageSelect のボタン表示にも使われます）
  - mapTxt: 1) で作成した TextAsset（1-11.txt をアサイン）
  - parRot: 回転のパー（UI表示用）
  - parAP: APのパー（TurnManager.parAP に反映）
- 保存

3) GameCatalog に StageSet を紐付け
- GameCatalog アセット（Assets 直下など）を選択
- worlds リストから該当の World を選ぶ
  - id: 表示用のワールド名（例: "1面"）
  - stageSet: 2) の StageSet をアサイン
- 新規ワールドを作る場合は worlds に要素を追加して stageSet を作成/割り当て

4) StageSelect（ステージ選択）の確認
- StageSelect シーンで StageSelectUI を選択
  - StageSelectUI は GameState.catalog.worlds[worldIndex].stageSet を参照してボタンを自動生成します
  - worldIndex は前画面や保存値（PlayerPrefs）で決まります（必要なら GameState の worldIndex を既定値に設定）
- 再生し、追加した id のボタンが表示されることを確認
  - 並び順は蛇行配置（縦に 1..N/2、右列に N/2+1..N）です。列数や間隔は StageSelectUI の Inspector で調整

5) Game（ロード）の確認
- Game シーンで StageManager を選択
  - StageManager.stageSet が、選択ワールドの StageSet に設定されていること
    - シーン固定で直接アサインする運用でもOK
    - あるいは GameState/PlayerPrefs による遷移（StageSelectUI からの遷移時）は起動フローで自動反映されます
- StageSelect から新ステージをクリック → Game に遷移し、該当マップが読み込まれること
  - 画面内 Par 表示/TurnManager.parAP も想定通りか確認

補足（新しいワールドを追加する場合）
- 新規 StageSet アセットを作成し、その stages にステージを登録
- GameCatalog.worlds に要素を追加し、id と stageSet を設定
- StageSelect で worldIndex を切り替える UI がある場合はそれで切替、ない場合は GameState の既定 worldIndex を変更

トラブルシュート
- ステージボタンが出ない
  - GameState/catalog/worldIndex が未設定
  - GameCatalog.worlds[*].stageSet が未割り当て、または stages が空
  - StageSelect シーン内の StageSelectUI の参照不足（grid, stageButtonPrefab）
- クリックしても別のマップが開く
  - StageManager.stageSet が別の StageSet を参照
  - StageSet.stages の id と mapTxt の対応が誤り
- マップが壊れて見える/エラーが出る
  - LevelParser 未定義の記号が含まれている（LevelParser.cs の記号対応を確認/拡張）
- 並び順/見た目を調整したい
  - StageSelectUI の columns/spacing/padding/minCellW/H を調整

チェックリスト（運用）
- [ ] .txt を Assets/Level/Stage_txt に作った
- [ ] StageSet.stages に id/mapTxt/parRot/parAP を設定した
- [ ] GameCatalog.worlds の該当 world に StageSet を割り当てた
- [ ] StageSelect でボタンが出て遷移できる
- [ ] Game で正しいマップ/Par が反映される