using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TurnManager : MonoBehaviour
{
    [Header("Board Reference")]
    public BoardManager board; // BoardManager.Build() 側で代入される想定

    // ======= ターン/状態 =======
    public bool gameOver { get; private set; }
    public bool cleared { get; private set; }

    bool playerTurn = true;
    bool runningGuards = false;

    // ======= リアルタイム駆動 =======
    [Header("Realtime Guards")]
    [Tooltip("ONでガードが一定間隔で常時行動。OFFで従来のターン制（プレイヤー行動後に1回だけ）")]
    public bool realtimeGuards = true;
    [Tooltip("ガードが1歩進む間隔（秒）")]
    public float guardStepInterval = 0.35f;
    float guardTimer = 0f;

    // ======= スコア用カウンタ（サイズ差なし） =======
    [Header("Score Counters")]
    public int rotCount { get; private set; }   // 成功した回転回数のみ加算
    public int retryCount { get; private set; } // リトライボタン/キー操作で加算

    public void ResetScoreCounters() { rotCount = 0; retryCount = 0; }
    public void RegisterRotation() { rotCount++; }
    public void RegisterRetry() { retryCount++; }

    // ======= 必須アイテム（全回収でゴール可） =======
    [Serializable]
    public struct RequiredItem
    {
        public char sym;        // 記号（i/j/k/l...将来増えてもOK）
        public bool collected;  // 取得済み
    }

    // 必須アイテムのフラット配列（重複あり）
    readonly List<RequiredItem> required = new List<RequiredItem>();
    public IReadOnlyList<RequiredItem> CurrentRequired => required;

    // UIへ更新を通知
    public event Action<IReadOnlyList<RequiredItem>> onRequiredChanged;

    // クリア結果（スコア/ランク）
    public struct ScoreResult
    {
        public int score;
        public char rank;
        public int rot;
        public int parRot;
        public int overRot;
        public int retries;
    }
    // クリアイベント（GameFlow が購読してUI表示に使う）
    public event Action<ScoreResult> onStageCleared;

    // GameOverイベント（必要ならUI側で購読）
    public event Action onGameOver;

    void Start()
    {
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();
        playerTurn = true;
        gameOver = false;
        cleared = false;
        guardTimer = 0f;
    }

    void Update()
    {
        if (gameOver || cleared) return;

        if (realtimeGuards)
        {
            // 回転アニメ中は歩かせない方が破綻しにくい
            if (board == null) board = UnityCompat.FindFirst<BoardManager>();
            if (board != null && board.IsAnimating) return;

            guardTimer += Time.deltaTime;
            if (guardTimer >= guardStepInterval)
            {
                guardTimer = 0f;
                StepAllGuards();
            }
        }
    }

    // TurnManager.cs 内（クラス直下の任意の場所）に追加
    public void ResetForRestart(bool keepRetryCount = true)
    {
        // ゲーム状態を生き返らせる
        gameOver = false;
        cleared = false;
        playerTurn = true;
        runningGuards = false;

        // 回転数はリトライごとにリセットしたいはず
        rotCount = 0;

        // 必須アイテム表示は BoardManager.Build() 側で InitRequiredItems が呼ばれる想定
        // ここでは触らない
    }


    // ======= ターン制制御 =======
    public bool IsPlayerTurn()
    {
        if (gameOver || cleared) return false;
        if (board != null && board.IsAnimating) return false;

        // リアルタイム時は常時入力可（UI側の「エイム中かつゲーム中」だけでボタン活性を制御）
        if (realtimeGuards) return true;

        return playerTurn && !runningGuards;
    }

    public void EndPlayerTurn()
    {
        if (gameOver || cleared) return;
        if (realtimeGuards) return; // リアルタイム時は何もしない
        if (runningGuards) return;
        StartCoroutine(GuardsTurnCoro());
    }

    IEnumerator GuardsTurnCoro()
    {
        runningGuards = true;
        playerTurn = false;
        yield return null; // フレームまたぎで安定

        StepAllGuards();

        runningGuards = false;
        if (!gameOver && !cleared) playerTurn = true;
    }

    public void StepAllGuards()
    {
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();
        if (board == null) return;

        var guards = board.guards;
        for (int i = 0; i < guards.Count; i++)
        {
            if (gameOver || cleared) break;
            var g = guards[i];
            if (g == null) continue;
            // GuardController 側は DoTurn() を1ステップとして実装しておけばOK
            g.DoTurn();
        }
    }

    // ======= アイテム関連 =======
    public void ResetGoalState()
    {
        required.Clear();
        NotifyRequired();
    }

    // BoardManager.Build() から初期化
    public void InitRequiredItems(IReadOnlyList<char> symbolsInReadingOrder)
    {
        required.Clear();
        if (symbolsInReadingOrder != null)
        {
            for (int i = 0; i < symbolsInReadingOrder.Count; i++)
                required.Add(new RequiredItem { sym = symbolsInReadingOrder[i], collected = false });
        }
        NotifyRequired();
    }

    // プレイヤーがアイテム記号 sym を取得したときに呼ぶ
    public void OnItemPicked(char sym)
    {
        for (int i = 0; i < required.Count; i++)
        {
            if (required[i].sym == sym && !required[i].collected)
            {
                var ri = required[i];
                ri.collected = true;
                required[i] = ri;
                NotifyRequired();
                break; // 同種の未取得のうち一つだけマーク
            }
        }
    }

    void NotifyRequired() => onRequiredChanged?.Invoke(required);

    bool AllRequiredCollected()
    {
        for (int i = 0; i < required.Count; i++)
            if (!required[i].collected) return false;
        return true;
    }

    // ======= クリア/ゲームオーバー =======
    public void TriggerClear() => TryClearAtExit();

    // Exit 上で呼ぶ。全回収していればクリア確定
    public void TryClearAtExit()
    {
        if (gameOver || cleared) return;
        if (!AllRequiredCollected()) return;

        cleared = true;
        playerTurn = false;

        // parRot は GameFlow 側の設定を採用
        int par = 0;
        var gf = UnityCompat.FindFirst<GameFlow>();
        if (gf != null) par = Mathf.Max(0, gf.parRot);

        var res = ComputeScore(par);
        onStageCleared?.Invoke(res);
    }

    public void TriggerGameOver()
    {
        if (gameOver || cleared) return;
        gameOver = true;
        playerTurn = false;
        onGameOver?.Invoke();
    }

    // ======= スコア計算（回転差/リトライのみ・100点満点の減点式） =======
    public ScoreResult ComputeScore(int parRot, int ROT_PEN = 3, int RETRY_PEN = 10)
    {
        int over = Mathf.Max(0, rotCount - parRot);
        int penalty = over * ROT_PEN + retryCount * RETRY_PEN;
        int s = Mathf.Clamp(100 - penalty, 0, 100);
        char r = (s >= 95) ? 'S' :
                 (s >= 85) ? 'A' :
                 (s >= 70) ? 'B' :
                 (s >= 50) ? 'C' : 'D';

        return new ScoreResult
        {
            score = s,
            rank = r,
            rot = rotCount,
            parRot = Mathf.Max(0, parRot),
            overRot = over,
            retries = retryCount
        };
    }
}
