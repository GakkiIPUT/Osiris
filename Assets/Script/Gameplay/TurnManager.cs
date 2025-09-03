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

    // 旧フラグ（互換）
    public bool itemCollected = false;
    public bool goalReached = false;

    // 宝箱（コレクション）
    public bool treasurePicked { get; private set; } = false;

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

    // 追加: 歩数カウンタ
    public int walkCount { get; private set; }
    [Header("Debug")]
    public bool debugLogGuardStepping = false;
    bool wasAnimating = false;
    float lastGuardTickT = 0f;

    //SE
    public AudioClip goalAudio;
    public AudioClip gameoverAudio;
    private AudioSource audioSource;
    public void ResetScoreCounters()
    {
        rotCount = 0;
        retryCount = 0;
        walkCount = 0;
        totalRotate = 0;
        totalAP = 0;
        Debug.Log("[Score] ResetScoreCounters: rot=0, walk=0, totalAP=0, retries=0");
    }
    public void RegisterRotation()
    {
        rotCount++;
        totalRotate++;
        totalAP++; // AP方式では回転も1AP
        Debug.Log($"回転成功: rotCount={rotCount}, totalRotate={totalRotate}, totalAP={totalAP}");
    }

    public void RegisterRetry()
    {
        retryCount++;
        Debug.Log($"リトライ: retryCount={retryCount}");
    }

    // 歩行成功時（PlayerController等から呼ばれる想定）
    public void RegisterActionPoint()
    {
        walkCount++; // 歩数は歩行成功のみ
        totalAP++;   // AP=歩行1、回転1
        Debug.Log($"AP加算(歩行): walkCount={walkCount}, totalAP={totalAP}");
    }

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
        public int rot;     // 回転数
        public int parRot;  // 旧仕様: parRot / AP仕様時は parAP を流用
        public int overRot; // 旧仕様: overRot / AP仕様時は overAP を流用
        public int retries;
        public int steps;   // 追加: 歩数（歩行成功数）
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

        // スムーズ移動中の待ち時間と一致させてカクツキ減
        if (board != null && board.smoothGuardMove)
        {
            guardStepInterval = 1f / Mathf.Max(0.1f, board.guardMoveCellsPerSec);
        }
    }

    void Update()
    {
        if (gameOver || cleared) return;

        if (realtimeGuards)
        {
            if (board == null) board = UnityCompat.FindFirst<BoardManager>();

            // Board の全体アニメ中は停止（ON/OFFをログ）
            if (board != null && board.IsAnimating)
            {
                if (!wasAnimating)
                {
                    wasAnimating = true;
                    if (debugLogGuardStepping) Debug.Log($"[Turn] GlobalPause ON (Board.IsAnimating) t={Time.time:F3}");
                }
                return;
            }
            else if (wasAnimating)
            {
                wasAnimating = false;
                if (debugLogGuardStepping) Debug.Log($"[Turn] GlobalPause OFF t={Time.time:F3}");
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

    // TurnManager.cs 内（クラス直下の任意の場所）に追加
    public void ResetForRestart(bool keepRetryCount = true)
    {
        // ゲーム状態を生き返らせる
        gameOver = false;
        cleared = false;
        playerTurn = true;
        runningGuards = false;

        // リトライ時に回数系はゼロに戻す（リトライ回数だけ維持）
        rotCount = 0;
        walkCount = 0;
        totalRotate = 0;
        totalAP = 0;

        // 取得フラグ系リセット
        treasurePicked = false;
        itemCollected = false;
        goalReached = false;

        if (!keepRetryCount) retryCount = 0;
        Debug.Log($"[Score] ResetForRestart: rot=0, walk=0, totalAP=0, retries={retryCount}");
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
        int stepped = 0;
        for (int i = 0; i < guards.Count; i++)
        {
            if (gameOver || cleared) break;
            var g = guards[i];
            if (g == null) continue;
            g.StepAI();
            stepped++;
        }

        //if (debugLogGuardStepping)
          //  Debug.Log($"[Turn] StepAllGuards done guards={stepped} t={Time.time:F3}");
    }

    // ======= アイテム関連 =======
    public void ResetGoalState()
    {
        required.Clear();
        NotifyRequired();
        treasurePicked = false;
    }

    // BoardManager.Build() から初期化（必須: 鍵 i のみが来る想定）
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

    // プレイヤーが必須アイテム記号 sym（= 'i'）を取得したとき
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

    // 宝箱（コレクション）を取得
    public void OnTreasurePicked()
    {
        treasurePicked = true;
        Debug.Log("[Treasure] Picked in this run.");
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

    // Exit 上で呼ぶ。必須（鍵）全回収でクリア確定
    public void TryClearAtExit()
    {
        if (gameOver || cleared) return;

        // クリア直前の視界チェック（無敵はスキップ）
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

        // goalReachedがtrueなら即クリア
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

        // 必須アイテムが全て揃っていないならクリア不可
        if (!AllRequiredCollected()) return;

        // ここで見られていたら死亡を優先
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
    public void TriggerGameOver()
    {
        if (gameOver || cleared) return;
        gameOver = true;
        playerTurn = false;

        // ゲームオーバー時点でスコア系は完全リセット（引き継がない）
        //ResetScoreCounters();

        //PlaySound(gameoverAudio);
        StartCoroutine(GameOverSequence());
    }

    // 追加：ゲームオーバー直前に視界を可視化してからUIを出す
    private IEnumerator GameOverSequence()
    {
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();
        if (board != null)
        {
            // 視界ON → 即時再生成 → 1フレーム待機（描画確保）
            board.SetAllGuardVision(true);
            board.RefreshAllGuardVision();
            yield return null; // または: yield return new WaitForEndOfFrame();
        }

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
            retries = retryCount,
            steps = walkCount
        };
    }



    public enum ScoreMode
    {
        Legacy,   // 旧仕様（回転・リトライ減点、回転パー値超過で減点）
        ActionPoint // 新仕様（AP方式）
    }

    public ScoreMode scoreMode = ScoreMode.Legacy;

    public int totalAP = 0;
    public int parAP = 0;
    public int totalRotate = 0;
    public int parRotate = 0;
    public int baseScore = 100;
    public int retryPenalty = 10;
    public int rotatePenalty = 5;

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

    // 追加: AP方式のスコア（移動＋回転成功のみカウント、超過AP＋リトライで減点）
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
            rot = rotCount,               // 回転数はそのまま
            parRot = Mathf.Max(0, parAP), // 表示互換のため parAP を流用
            overRot = overAP,             // 表示互換のため overAP を流用
            retries = retryCount,
            steps = walkCount             // 歩数
        };
    }

    // 追加: 現在のモードに応じてスコアを計算するユーティリティ
    public ScoreResult ComputeScoreForCurrentMode(int parRotFromGF)
    {
        if (scoreMode == ScoreMode.ActionPoint)
            return ComputeScoreAP();
        return ComputeScore(parRotFromGF);
    }
    private void PlaySound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }
    private void Awake()
    {
        // AudioSource を追加
        audioSource = gameObject.AddComponent<AudioSource>();

        // Resources からロード
        goalAudio = Resources.Load<AudioClip>("Audio/goal");
        gameoverAudio = Resources.Load<AudioClip>("Audio/gameover");
    }
}
