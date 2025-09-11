using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ターン/リアルタイム進行、スコア、必須アイテム進捗、クリア/ゲームオーバー判定を管理する。
/// </summary>
public class TurnManager : MonoBehaviour
{
    [Header("Board Reference")]
    public BoardManager board; // BoardManager.Build() 側で代入される想定

    // ======= ターン/状態 =======
    /// <summary>ゲームオーバー状態</summary>
    public bool gameOver { get; private set; }

    /// <summary>クリア状態</summary>
    public bool cleared { get; private set; }

    private bool playerTurn = true;
    private bool runningGuards = false;

    // 互換用フラグ
    public bool itemCollected = false;

    public bool goalReached = false;

    /// <summary>宝箱（コレクション）を取得済みか</summary>
    public bool treasurePicked { get; private set; } = false;

    [Header("Realtime Guards")]
    [Tooltip("ONでガードが一定間隔で常時行動。OFFで従来のターン制（プレイヤー行動後に1回だけ）")]
    public bool realtimeGuards = true;

    [Tooltip("ガードが1歩進む間隔（秒）")]
    public float guardStepInterval = 0.35f;

    private float guardTimer = 0f;

    [Header("Score Counters")]
    public int rotCount { get; private set; }

    /// <summary>リトライ回数</summary>
    public int retryCount { get; private set; }

    /// <summary>歩数（成功歩行のみ）</summary>
    public int walkCount { get; private set; }

    [Header("Debug")]
    public bool debugLogGuardStepping = false;

    private bool wasAnimating = false;
    private float lastGuardTickT = 0f;

    //SE
    public AudioClip goalAudio;

    public AudioClip gameoverAudio;
    private AudioSource audioSource;

    /// <summary>スコア系カウンタをゼロにする。</summary>
    public void ResetScoreCounters()
    {
        rotCount = 0;
        retryCount = 0;
        walkCount = 0;
        totalRotate = 0;
        totalAP = 0;
        Debug.Log("[Score] ResetScoreCounters: rot=0, walk=0, totalAP=0, retries=0");
    }

    /// <summary>回転成功を登録する（回転数/AP加算）。</summary>
    public void RegisterRotation()
    {
        rotCount++;
        totalRotate++;
        totalAP++; // AP方式: 回転も1AP
        Debug.Log($"回転成功: rotCount={rotCount}, totalRotate={totalRotate}, totalAP={totalAP}");
    }

    /// <summary>リトライを登録する。</summary>
    public void RegisterRetry()
    {
        retryCount++;
        Debug.Log($"リトライ: retryCount={retryCount}");
    }

    /// <summary>歩行成功を登録する（歩数/AP加算）。</summary>
    public void RegisterActionPoint()
    {
        walkCount++;
        totalAP++;
        Debug.Log($"AP加算(歩行): walkCount={walkCount}, totalAP={totalAP}");
    }

    // ======= 必須アイテム =======
    [Serializable]
    public struct RequiredItem
    {
        public char sym;
        public bool collected;
    }

    private readonly List<RequiredItem> required = new List<RequiredItem>();

    /// <summary>必須アイテムの現在一覧（重複あり）</summary>
    public IReadOnlyList<RequiredItem> CurrentRequired => required;

    /// <summary>必須アイテム更新イベント（UI 向け）。</summary>
    public event Action<IReadOnlyList<RequiredItem>> onRequiredChanged;

    /// <summary>クリア結果（スコア/ランク/統計）。</summary>
    public struct ScoreResult
    {
        public int score;
        public char rank;
        public int rot;
        public int parRot;
        public int overRot;
        public int retries;
        public int steps;
    }

    /// <summary>クリアイベント（UI 表示側が購読）。</summary>
    public event Action<ScoreResult> onStageCleared;

    /// <summary>ゲームオーバーイベント。</summary>
    public event Action onGameOver;

    /// <summary>初期化（参照の取得とガード歩幅の初期値調整）。</summary>
    private void Start()
    {
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();
        playerTurn = true;
        gameOver = false;
        cleared = false;
        guardTimer = 0f;

        if (board != null && board.smoothGuardMove)
        {
            guardStepInterval = 1f / Mathf.Max(0.1f, board.guardMoveCellsPerSec);
        }
    }

    /// <summary>リアルタイムモード時のガード駆動とアニメ中の停止制御。</summary>
    private void Update()
    {
        if (gameOver || cleared) return;

        if (realtimeGuards)
        {
            if (board == null) board = UnityCompat.FindFirst<BoardManager>();

            if (board != null && board.IsAnimating)
            {
                if (!wasAnimating)
                {
                    wasAnimating = true;
                    //if (debugLogGuardStepping) Debug.Log($"[Turn] GlobalPause ON (Board.IsAnimating) t={Time.time:F3}");
                }
                return;
            }
            else if (wasAnimating)
            {
                wasAnimating = false;
                //if (debugLogGuardStepping) Debug.Log($"[Turn] GlobalPause OFF t={Time.time:F3}");
            }

            guardTimer += Time.deltaTime;
            if (guardTimer >= guardStepInterval)
            {
                guardTimer = 0f;
                if (debugLogGuardStepping)
                {
                    float dt = (lastGuardTickT == 0f) ? 0f : (Time.time - lastGuardTickT);
                    //Debug.Log($"[Turn] Tick StepAllGuards t={Time.time:F3} dt={dt:F3}s");
                }
                lastGuardTickT = Time.time;
                StepAllGuards();
            }
        }
    }

    /// <summary>GameOver 時のスコア引き継ぎポリシー。</summary>
    public enum GameOverRetryBehavior
    {
        ResetScoreOnGameOver,
        KeepScoreAndAddRetry
    }

    [Header("Game Over / Retry Policy")]
    public GameOverRetryBehavior gameOverRetryBehavior = GameOverRetryBehavior.ResetScoreOnGameOver;

    /// <summary>
    /// リスタート時の状態/スコアカウンタをリセットする。
    /// </summary>
    public void ResetForRestart(bool keepRetryCount = true)
    {
        gameOver = false;
        cleared = false;
        playerTurn = true;
        runningGuards = false;

        treasurePicked = false;
        itemCollected = false;
        goalReached = false;

        bool comingFromGameOver = true;
        if (!this.gameOver && !this.cleared) comingFromGameOver = false;

        bool shouldResetScoreCounters =
            (!comingFromGameOver) ||
            (comingFromGameOver && gameOverRetryBehavior == GameOverRetryBehavior.ResetScoreOnGameOver);

        if (shouldResetScoreCounters)
        {
            rotCount = 0;
            walkCount = 0;
            totalRotate = 0;
            totalAP = 0;
        }
        else
        {
            Debug.Log($"[Score] Keep counters on restart after GameOver (rot={rotCount}, walk={walkCount}, totalAP={totalAP}, retries={retryCount})");
        }

        if (!keepRetryCount) retryCount = 0;

        Debug.Log($"[Score] ResetForRestart: countersReset={shouldResetScoreCounters}, retries={retryCount}");
    }

    /// <summary>
    /// プレイヤーの行動可否を返す。リアルタイム時は常時 true（別UIで制御）。
    /// </summary>
    public bool IsPlayerTurn()
    {
        if (gameOver || cleared) return false;
        if (board != null && board.IsAnimating) return false;
        if (realtimeGuards) return true;
        return playerTurn && !runningGuards;
    }

    /// <summary>
    /// ターン制時、プレイヤーターンを終了してガードターンへ移行する。
    /// </summary>
    public void EndPlayerTurn()
    {
        if (gameOver || cleared) return;
        if (realtimeGuards) return;
        if (runningGuards) return;
        StartCoroutine(GuardsTurnCoro());
    }

    /// <summary>ガードターンの実行コルーチン。</summary>
    private IEnumerator GuardsTurnCoro()
    {
        runningGuards = true;
        playerTurn = false;
        yield return null;

        StepAllGuards();

        runningGuards = false;
        if (!gameOver && !cleared) playerTurn = true;
    }

    /// <summary>全ガードに1歩行動させる。</summary>
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
            g.StepAI();
        }
    }

    /// <summary>必須アイテム進捗を初期化する。</summary>
    public void ResetGoalState()
    {
        required.Clear();
        NotifyRequired();
        treasurePicked = false;
    }

    /// <summary>必須アイテム一覧を初期化（読み順で重複含む）。</summary>
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

    /// <summary>指定の必須アイテム（例: 'i'）取得を反映する。</summary>
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
                break;
            }
        }
    }

    /// <summary>宝箱（コレクション）を取得したときの通知。</summary>
    public void OnTreasurePicked()
    {
        treasurePicked = true;
        Debug.Log("[Treasure] Picked in this run.");
    }

    private void NotifyRequired() => onRequiredChanged?.Invoke(required);

    private bool AllRequiredCollected()
    {
        for (int i = 0; i < required.Count; i++)
            if (!required[i].collected) return false;
        return true;
    }

    /// <summary>即時クリア試行（Exit 上相当）。</summary>
    public void TriggerClear() => TryClearAtExit();

    /// <summary>
    /// Exit 上でクリア判定する（見られていない・必須全取得が条件）。
    /// </summary>
    public void TryClearAtExit()
    {
        if (gameOver || cleared) return;

        bool IsPlayerSeenNow()
        {
            if (board == null || board.player == null || board.player.invincible) return false;
            var gs = board.guards;
            for (int i = 0; i < gs.Count; i++)
            {
                var g = gs[i];
                if (g != null && g.CanSeePlayer()) return true;
            }
            return false;
        }

        if (goalReached)
        {
            if (IsPlayerSeenNow()) { TriggerGameOver(); return; }
            cleared = true;
            playerTurn = false;
            var gf = UnityCompat.FindFirst<GameFlow>();
            int parRotValue1 = gf != null ? Mathf.Max(0, gf.parRot) : 0;

            var res = ComputeScoreForCurrentMode(parRotValue1);

            if (scoreMode == ScoreMode.ActionPoint)
            {
                int overAP = Mathf.Max(0, totalAP - parAP);
                Debug.Log($"[Result/AP] score:{res.score} rank:{res.rank} steps:{res.steps} " +
                          $"rot:{res.rot} totalAP:{totalAP} parAP:{parAP} overAP:{overAP} retries:{retryCount}");
            }
            else
            {
                int overRot = Mathf.Max(0, rotCount - parRotValue1);
                Debug.Log($"[Result/Legacy] score:{res.score} rank:{res.rank} steps:{res.steps} " +
                          $"rot:{rotCount} parRot:{parRotValue1} overRot:{overRot} retries:{retryCount}");
            }

            onStageCleared?.Invoke(res);
            return;
        }

        if (!AllRequiredCollected()) return;
        if (IsPlayerSeenNow()) { TriggerGameOver(); return; }

        cleared = true;
        playerTurn = false;
        PlaySound(goalAudio);
        int parRotValue2 = 0;
        var gf2 = UnityCompat.FindFirst<GameFlow>();
        if (gf2 != null) parRotValue2 = Mathf.Max(0, gf2.parRot);
        var res2 = ComputeScoreForCurrentMode(parRotValue2);

        if (scoreMode == ScoreMode.ActionPoint)
        {
            int overAP2 = Mathf.Max(0, totalAP - parAP);
            Debug.Log($"[Result/AP] score:{res2.score} rank:{res2.rank} steps:{res2.steps} " +
                      $"rot:{res2.rot} totalAP:{totalAP} parAP:{parAP} overAP:{overAP2} retries:{retryCount}");
        }
        else
        {
            int overRot2 = Mathf.Max(0, rotCount - parRotValue2);
            Debug.Log($"[Result/Legacy] score:{res2.score} rank:{res2.rank} steps:{res2.steps} " +
                      $"rot:{rotCount} parRot:{parRotValue2} overRot:{overRot2} retries:{retryCount}");
        }

        onStageCleared?.Invoke(res2);
    }

    /// <summary>最後にプレイヤーを倒したガード（GameOver演出用）。</summary>
    public GuardController lastKiller { get; private set; }

    /// <summary>ゲームオーバー（犯人付き）を発火する。</summary>
    public void TriggerGameOver(GuardController killer)
    {
        if (gameOver || cleared) return;
        lastKiller = killer;
        TriggerGameOver();
    }

    /// <summary>ゲームオーバーを発火する（スコア処理/演出を含む）。</summary>
    public void TriggerGameOver()
    {
        if (gameOver || cleared) return;
        gameOver = true;
        playerTurn = false;

        if (gameOverRetryBehavior == GameOverRetryBehavior.ResetScoreOnGameOver)
        {
            ResetScoreCounters();
            Debug.Log("[GameOver] ResetScoreOnGameOver: counters reset.");
        }
        else
        {
            RegisterRetry();
            Debug.Log($"[GameOver] KeepScoreAndAddRetry: keep counters (rot={rotCount}, walk={walkCount}, totalAP={totalAP}), retries={retryCount}");
        }

        StartCoroutine(GameOverSequence());
    }

    /// <summary>ゲームオーバー演出（視界ハイライト等）を行う。</summary>
    private IEnumerator GameOverSequence()
    {
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();
        if (board != null)
        {
            board.SetAllGuardVision(true);
            board.RefreshAllGuardVision();
            yield return null;
        }

        if (board != null)
        {
            if (lastKiller != null)
            {
                lastKiller.SetKillerHighlight(true);
                lastKiller.ShowKillerMarkPersistent();
                lastKiller.ShowKillerOutline(true);
            }
            var gs = board.guards;
            for (int i = 0; i < gs.Count; i++)
            {
                var g = gs[i];
                if (g == null) continue;
                if (g == lastKiller) continue;
                g.SetVisionColorAndRefresh(g.othersVisionColorOnGameOver);
            }
        }

        onGameOver?.Invoke();
    }

    /// <summary>
    /// 旧方式のスコア計算（回転差/リトライ減点、100点満点の減点式）。
    /// </summary>
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
            retries = retryCount,
            steps = walkCount
        };
    }

    /// <summary>スコアの算出方式。</summary>
    public enum ScoreMode
    {
        Legacy,
        ActionPoint
    }

    public ScoreMode scoreMode = ScoreMode.Legacy;

    public int totalAP = 0;
    public int parAP = 0;
    public int totalRotate = 0;
    public int parRotate = 0;
    public int baseScore = 100;
    public int retryPenalty = 10;
    public int rotatePenalty = 5;

    /// <summary>
    /// 現在設定に基づく数値スコアを返す（ログ出力付き）。
    /// </summary>
    public int CalcScore()
    {
        switch (scoreMode)
        {
            case ScoreMode.ActionPoint:
                int penaltyAP = Mathf.Max(0, totalAP - parAP);
                int scoreAP = Mathf.Max(0, baseScore - penaltyAP - retryCount * retryPenalty);
                Debug.Log($"[Score/AP] base:{baseScore} - (AP超過:{penaltyAP}) - (リトライ:{retryCount}×{retryPenalty}) = {scoreAP}");
                return scoreAP;

            case ScoreMode.Legacy:
            default:
                int penaltyRot = Mathf.Max(0, totalRotate - parRotate);
                int scoreRot = Mathf.Max(0, baseScore - penaltyRot * rotatePenalty - retryCount * retryPenalty);
                Debug.Log($"[Score/Legacy] base:{baseScore} - (回転超過:{penaltyRot}×{rotatePenalty}) - (リトライ:{retryCount}×{retryPenalty}) = {scoreRot}");
                return scoreRot;
        }
    }

    /// <summary>
    /// AP方式のスコア詳細を計算する（超過AP/リトライで減点）。
    /// </summary>
    public ScoreResult ComputeScoreAP()
    {
        int overAP = Mathf.Max(0, totalAP - parAP);
        int s = Mathf.Clamp(baseScore - (overAP + retryCount * retryPenalty), 0, baseScore);
        char r = (s >= 95) ? 'S' :
                 (s >= 85) ? 'A' :
                 (s >= 70) ? 'B' :
                 (s >= 50) ? 'C' : 'D';

        return new ScoreResult
        {
            score = s,
            rank = r,
            rot = rotCount,
            parRot = Mathf.Max(0, parAP),
            overRot = overAP,
            retries = retryCount,
            steps = walkCount
        };
    }

    /// <summary>
    /// 現在のモードに応じてスコア（詳細）を計算する。
    /// </summary>
    public ScoreResult ComputeScoreForCurrentMode(int parRotFromGF)
    {
        if (scoreMode == ScoreMode.ActionPoint)
            return ComputeScoreAP();
        return ComputeScore(parRotFromGF);
    }

    /// <summary>単発 SE を再生する。</summary>
    private void PlaySound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }

    /// <summary>AudioSource の用意と効果音のロード。</summary>
    private void Awake()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        goalAudio = Resources.Load<AudioClip>("Audio/goal");
        gameoverAudio = Resources.Load<AudioClip>("Audio/gameover");
    }
}
