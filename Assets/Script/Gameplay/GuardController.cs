using System.Collections.Generic;
using UnityEngine;

public class GuardController : MonoBehaviour
{
    public enum PatrolMode
    {
        Static,
        AutoEdgePingPong,
        PingPong,
        Loop
    }

    public enum WatchMode
    {
        OneDir,
        TwoDirUD,
        TwoDirLR,
        Rotate4Dir
    }

    [Header("Refs")]
    private BoardManager board;
    private TurnManager turn;

    [Header("State")]
    public Vector2Int pos;
    private Vector2Int forward = Vector2Int.right;

    [Header("Vision")]
    public int viewRange = 5;
    public float fovAngle = 90f;

    [Header("Patrol")]
    public PatrolMode patrolMode = PatrolMode.AutoEdgePingPong;

    [Tooltip("例: R5 / R3,U2,L3,D2")]
    public string pattern = "";
    public bool patternIsRelative = true;
    public bool debugDrawPath = false;
    public bool useBoardDefaultViewRange = true;

    [Tooltip("Loop中に壁でブロックされたら次の角へ進まず折り返す（往復）")]
    public bool bounceOnBlockedInLoop = true;  // ← 追加

    public enum Facing { Up, Right, Down, Left }
    public Facing startFacing = Facing.Right;

    [Header("Watch (Viewing)")]
    public WatchMode watchMode = WatchMode.OneDir;
    [Tooltip("Rotate4Dir の周期（秒）")]
    public float rotatePeriod = 1.0f;
    [Tooltip("Rotate4Dir の回転方向（右回り）")]
    public bool rotateClockwise = true;

    // 経路
    private readonly List<Vector2Int> path = new();
    private int pathIndex = 0;
    private int pingDir = +1;
    private int loopDir = +1; // ← 追加：Loop用の進行向き

    // 視界
    public bool showVision = true;
    private GameObject visionRoot;

    // 監視内部状態
    private float lastRotateTime = -999f;
    private int facingIndex = 0;
    private List<Vector2Int> _tmpFwds;

    // スムーズ移動/回転
    private bool isMoving = false;
    private Vector3 moveFrom, moveTo;
    private Vector2Int gridFrom, gridTo;
    private float moveT = 0f;
    private float moveDur = 0.25f;

    // 追加: Tick取りこぼし対策（移動完了直後に即1手消化）
    private bool stepQueued = false;

    private float currentYaw = 0f;
    private float targetYaw = 0f;

    // 基本回転（常にX=90°：上向き）
    private Quaternion baseRot = Quaternion.identity;
    private float visionTimer = 0f;

    // プレイヤー側の衝突判定用に公開（読み取り専用）
    public bool IsMoving => isMoving;
    public Vector2Int CurrentPos => pos;
    public Vector2Int NextPos => isMoving ? gridTo : pos;
    // ===== 視界モード =====
    public enum VisionMode { SmoothFan, CellFan, GridAligned }

    [Header("Vision Mode")]
    [Tooltip("視界の描画・判定モード。SmoothFan=滑らか扇形, CellFan=セル扇形, GridAligned=グリッド整合")]
    public VisionMode visionMode = VisionMode.SmoothFan;

    // ===== 視界メッシュ（滑らか扇形）オプション =====
    [Header("Vision Render")]
    public bool smoothFanVision = true; // 互換のため残置（VisionModeで上書きされます)
    [Range(12, 256)] public int visionRayCount = 72;
    [Min(0.02f)] public float visionRayStep = 0.1f;

    // 視界原点オフセット（顔側にずらす）
    [Tooltip("視界原点を前方へずらす距離（セル単位）。0.0=セル中心, 1.0=前方1マス")]
    [Range(0f, 1.0f)] public float visionOriginForwardOffset = 0f;

    // 追加: アセットの「前」方向とゲーム基準のズレを補正
    [Header("Yaw Offsets (deg)")]
    [Tooltip("見た目用のYawオフセット（スプライト向き補正）。例: +90")]
    public float visualYawOffsetDeg = 90f;
    [Tooltip("視界ロジック用のYawオフセット（角度判定・扇形補正）。通常は0")]
    public float visionYawOffsetDeg = 0f;

    private MeshFilter visionMf;
    private MeshRenderer visionMr;
    private Mesh visionMesh;

    // ===== 反転時の挙動 =====
    [Header("Flip (Reverse) Control")]
    [Tooltip("反転（監視向き切替/行動反転）時に停止する秒数（視界も無効）")]
    [Min(0f)] public float flipPauseSeconds = 0.5f; // 反転後の視界OFF待機
    private float flippingUntil = 0f;

    // 追加: 反転前ホールド（移動だけ停止・視界は維持）
    [Tooltip("反転前に停止する時間（視界は維持）。デフォルト0.5s")]
    [Min(0f)] public float preFlipHoldSeconds = 0.5f;  // ← 追加: Inspector で編集可能
    private float preFlipUntil = 0f;
    private bool preFlipActive = false;
    private bool pendingTurn = false;
    private Vector2Int pendingForward;
    private float pendingTargetYaw = 0f;
    private string pendingFlipReason = "";

    // ===== Visual Aid =====
    [Header("Visual Aid")]
    public bool showFacingArrow = true;
    public Color facingArrowColor = new Color(1f, 1f, 0.25f, 0.9f);
    private GameObject facingArrow;

    // 直近に適用した見た目用の向き（反転再適用用）
    private Facing lastVisualFacing;

    // 追加: スプライト反転プロファイル
    public enum FlipProfile { Canonical, Requested, Inverted }
    [Header("Sprite Facing Calibration")]
    [Tooltip("スプライト反転の割り当てプリセット")]
    [HideInInspector] public FlipProfile flipProfile = FlipProfile.Requested; // Inspector 非表示

    // 追加: 方向別見た目補正
    [System.Serializable]
    public struct VisualCorrection
    {
        [Tooltip("この方向の見た目用Yaw加算（deg）。右基準からのズレ補正")]
        public float yawAdd;
        [Tooltip("この方向のスプライト左右反転")]
        public bool flipX;
        [Tooltip("この方向のスプライト上下反転")]
        public bool flipY;
    }

    [Header("Per Direction Visual Correction")]
    [Tooltip("方向ごとにflipを適用する（ON 推奨）。OFFにすると FlipProfile を使用")]
    [HideInInspector] public bool usePerDirectionFlip = true; // Inspector 非表示

    // デフォルトは「補正なし」。必要に応じてInspectorで設定
    [HideInInspector] public VisualCorrection visUp = new VisualCorrection { yawAdd = 90f, flipX = false, flipY = false };
    [HideInInspector] public VisualCorrection visRight = new VisualCorrection { yawAdd = 0f, flipX = false, flipY = false };
    [HideInInspector] public VisualCorrection visDown = new VisualCorrection { yawAdd = 90f, flipX = false, flipY = false };
    [HideInInspector] public VisualCorrection visLeft = new VisualCorrection { yawAdd = 90f, flipX = false, flipY = false };

    // 方向→補正取得
    private VisualCorrection VC(Facing f)
    {
        switch (f)
        {
            case Facing.Up: return visUp;
            case Facing.Right: return visRight;
            case Facing.Down: return visDown;
            case Facing.Left: return visLeft;
        }
        return visRight;
    }

    [Header("Vision Color")]
    [Tooltip("敵視界の色（アルファで不透明度）。各ガードごとに調整可能")]
    public Color visionColor = new Color(1f, 0.25f, 0.35f, 0.35f);

    private MaterialPropertyBlock _mpb;

    // ======== 追加: GameOver時の強調表示 ========
    [Header("Game Over Highlight")]
    [Tooltip("ゲームオーバー時にボディも赤くする（オフならボディは変えず、アウトラインのみ）")]
    public bool useBodyTintOnHighlight = false;

    [Tooltip("ゲームオーバー時に『倒した敵』として強調するボディ色（SpriteRenderer.tint）")]
    public Color killerBodyTint = new Color(1f, 0.2f, 0.2f, 1f);

    [Tooltip("ゲームオーバー時に『倒した敵』として強調する視界色")]
    public Color killerVisionColor = new Color(1f, 0.2f, 0.2f, 0.6f);

    [Tooltip("ゲームオーバー時、犯人以外の視界色（薄くする等）")]
    public Color othersVisionColorOnGameOver = new Color(1f, 1f, 1f, 0.15f);

    // ===== 追加: アウトライン（周りに色） =====
    [Header("Killer Outline (Ring)")]
    [Tooltip("アウトライン色")]
    public Color killerOutlineColor = new Color(1f, 0.2f, 0.2f, 0.8f);
    [Tooltip("半径（セル単位）")]
    public float killerOutlineRadius = 0.45f;
    [Tooltip("線幅（ワールド単位）")]
    public float killerOutlineWidth = 0.05f;
    [Tooltip("Yオフセット（床からの高さ）")]
    public float killerOutlineYOffset = 0.02f;
    [Tooltip("円のセグメント数（多いほど滑らか）")]
    [Range(12, 128)] public int killerOutlineSegments = 48;

    // ===== 追加: 『！』スプライト =====
    [Header("Killer Mark (!) / World Placement")]
    [Tooltip("ガードのワールド位置に対する相対オフセット（X/Z/高さ）。例: (0,0.7,0) で頭上")]
    public Vector3 exclamationWorldOffset = new Vector3(0f, 0.7f, 0f);

    [Tooltip("『！』を床面に寝かせる（X=90°）。OFFなら直立（X=0°）")]
    public bool exclamationLayOnFloor = true;

    [Tooltip("『！』のワールドY軸回転（度）")]
    public float exclamationWorldYaw = 0f;

    [Tooltip("SpriteRenderer の Sorting Layer 名（空なら変更しない）")]
    public string exclamationSortingLayer = "";

    [Tooltip("SpriteRenderer の Sorting Order（大きいほど手前）")]
    public int exclamationSortingOrder = 2000;

    [Tooltip("Zファイティング回避の微小浮上量（メートル）")]
    public float exclamationLiftEpsilon = 0.001f;

    // 追加: 『！』をローカル座標（Prefab基準）で配置する。OFFならワールドY=高さオフセットを使用
    public bool exclamationUseLocalOffset = true;

    // 追加: 『！』のローカル座標オフセット（Prefab基準）。注意: 本プロジェクトではガードがX=90°のため、ローカルZがワールドY(高さ)に相当
    public Vector3 exclamationLocalOffset = new Vector3(0f, 0f, 0.7f);

    // 『！』の基本設定（スプライト／色／サイズ／高さ）
    [Tooltip("『！』に使用するスプライト（必須）")]
    public Sprite exclamationSprite;

    [Tooltip("『！』の色（ティント）")]
    public Color exclamationTint = new Color(1f, 0.15f, 0.15f, 1f);

    [Tooltip("『！』の表示サイズ（ワールド単位）")]
    public Vector2 exclamationSize = new Vector2(0.35f, 0.35f);

    [Tooltip("ワールドYでの高さ（exclamationWorldOffset.y が 0 のときの後方互換用）")]
    public float exclamationHeight = 0.7f;

    // 内部
    private SpriteRenderer[] _bodySRs;
    private Color[] _bodySRsDefault;
    private Renderer[] _bodyRenderers;
    private MaterialPropertyBlock _bodyMpb;
    private Color _savedVisionColor;
    private bool _killerHighlighted = false;

    private GameObject _killerMarkGO;
    private LineRenderer _killerOutlineLR;

    // 監視向きの設定（開始時・ターン開始時）
    public void SetFacing(Facing f)
    {
        startFacing = f;
        forward = FacingToVec(f);
        targetYaw = FacingToYaw(f);
        currentYaw = targetYaw; // 即スナップ

        ApplyVisualByFacing();
        ApplyVisualYaw(); // ← 追加：初期フレームから正しい向きに回転

        // 体のRendererをキャッシュ（強調表示で使用）
        CacheBodyRenderers();

        if (showFacingArrow) EnsureFacingArrow();
        UpdateVisionOverlay();
    }

    // ===== 初期化 =====
    public void Init(BoardManager b, Vector2Int start)
    {
        board = b;
        pos = start;
        transform.position = b.GridToWorldActor(pos);
        turn = UnityCompat.FindFirst<TurnManager>();

        if (patrolMode == PatrolMode.Static)
        {
            forward = FacingToVec(startFacing);
        }
        else
        {
            BuildPatrolPath(start);

            if (path.Count == 0 && patrolMode != PatrolMode.AutoEdgePingPong)
                patrolMode = PatrolMode.AutoEdgePingPong;

            Vector2Int tgt = GetCurrentTargetOrFallback(start);
            forward = DirToStep(tgt - pos);
        }

        facingIndex = (int)startFacing;
        if (useBoardDefaultViewRange) viewRange = board.defaultGuardViewRange;
        lastRotateTime = Time.time;

        targetYaw = FacingToYaw(startFacing);
        currentYaw = targetYaw;

        // ガード見た目は常に X=90° ベース（寝姿勢を強制）
        baseRot = Quaternion.Euler(90f, 0f, 0f);
        ApplyVisualByFacing();
        ApplyVisualYaw(); // ← 追加：初期フレームから正しい向きに回転

        // 体のRendererをキャッシュ（強調表示で使用）
        CacheBodyRenderers();

        if (showFacingArrow) EnsureFacingArrow();
        UpdateVisionOverlay();
    }

    // パターン構築
    private void BuildPatrolPath(Vector2Int start)
    {
        path.Clear();
        if (patrolMode == PatrolMode.Static) return;

        if (patrolMode == PatrolMode.AutoEdgePingPong || string.IsNullOrWhiteSpace(pattern))
        {
            var hA = FindEdge(start, Vector2Int.left);
            var hB = FindEdge(start, Vector2Int.right);
            var vA = FindEdge(start, Vector2Int.down);
            var vB = FindEdge(start, Vector2Int.up);
            int lenH = (hB - hA).sqrMagnitude;
            int lenV = (vB - vA).sqrMagnitude;

            if (lenV > lenH) { path.Add(vA); path.Add(vB); }
            else { path.Add(hA); path.Add(hB); }

            pathIndex = 0;
            pingDir = +1;
            loopDir = +1; // ← 追加：初期化
            return;
        }

        var tokens = pattern.Split(new char[] { ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        Vector2Int curr = start;

        foreach (var tk in tokens)
        {
            if (string.IsNullOrEmpty(tk)) continue;
            char c = char.ToUpperInvariant(tk[0]);
            int n = 1;
            if (tk.Length > 1) { int.TryParse(tk.Substring(1), out n); if (n <= 0) n = 1; }

            Vector2Int dir = Vector2Int.zero;
            if (c == 'R') dir = Vector2Int.right;
            else if (c == 'L') dir = Vector2Int.left;
            else if (c == 'U') dir = Vector2Int.up;
            else if (c == 'D') dir = Vector2Int.down;
            else continue;

            Vector2Int basePos = patternIsRelative ? curr : start;
            Vector2Int next = RunUntilBlocked(basePos, dir, n);

            if (next != basePos)
            {
                path.Add(next);
                curr = next;
            }
        }

        if (patrolMode == PatrolMode.PingPong && tokens.Length == 1)
        {
            char c = char.ToUpperInvariant(tokens[0][0]);
            int n = 1;
            if (tokens[0].Length > 1) { int.TryParse(tokens[0].Substring(1), out n); if (n <= 0) n = 1; }

            Vector2Int dir = Vector2Int.zero;
            if (c == 'R') dir = Vector2Int.right;
            else if (c == 'L') dir = Vector2Int.left;
            else if (c == 'U') dir = Vector2Int.up;
            else if (c == 'D') dir = Vector2Int.down;

            if (dir != Vector2Int.zero)
            {
                var a = RunUntilBlocked(start, -dir, n);
                var b = RunUntilBlocked(start, dir, n);

                path.Clear();
                path.Add(b);
                path.Add(a);

                pathIndex = 0;
                pingDir = +1;
                return;
            }
        }

        pathIndex = 0;
        pingDir = +1;
    }

    private Vector2Int RunUntilBlocked(Vector2Int from, Vector2Int dir, int maxSteps)
    {
        var p = from;
        for (int s = 0; s < maxSteps; s++)
        {
            var np = p + dir;
            if (!board.InBounds(np) || !board.IsWalkable(np)) break;
            p = np;
        }
        return p;
    }

    private Vector2Int FindEdge(Vector2Int start, Vector2Int dir)
    {
        var p = start;
        while (true)
        {
            var np = p + dir;
            if (!board.InBounds(np) || !board.IsWalkable(np)) return p;
            p = np;
        }
    }

    private void Update()
    {
        if (!Application.isPlaying) return;
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (turn == null || turn.gameOver || turn.cleared) return;

        // 反転前待機 → 反転確定 への遷移を処理
        TryApplyPendingTurn();

        // スムーズ回転
        if (board != null)
        {
            float rotSpd = Mathf.Max(30f, board.guardRotateDegPerSec);
            float delta = Mathf.DeltaAngle(currentYaw, targetYaw);
            float step = Mathf.Sign(delta) * Mathf.Min(Mathf.Abs(delta), rotSpd * Time.deltaTime);
            currentYaw = Mathf.Repeat(currentYaw + step + 360f, 360f);
            ApplyVisualYaw();
        }

        // 向きが変わっていたら反転を再適用
        {
            Facing now = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);
            if (now != lastVisualFacing) ApplyVisualByFacing();
        }

        // スムーズ移動
        if (board != null && board.smoothGuardMove && isMoving)
        {
            moveT += Time.deltaTime / Mathf.Max(0.0001f, moveDur);
            float t = Mathf.Clamp01(moveT);
            transform.position = Vector3.Lerp(moveFrom, moveTo, t);

            if (t >= 1f)
            {
                isMoving = false;
                pos = gridTo;
                transform.position = board.GridToWorldActor(pos);

                // ここで取りこぼしを即時消化（次のTickを待たない）
                if (stepQueued)
                {
                    stepQueued = false;
                    RunQueuedStepOnce();
                }
            }
        }

        // 視界更新（pre-flip中もOK。flip中は無効）
        if (board != null)
        {
            visionTimer += Time.deltaTime;
            float configured = Mathf.Max(0.01f, board.guardVisionUpdateInterval);
            float minIntervalWhileMoving = 1f / 60f;
            float interval = configured;
            if (board.smoothGuardMove && isMoving) interval = Mathf.Max(configured, minIntervalWhileMoving);
            if (visionTimer >= interval)
            {
                visionTimer = 0f;
                UpdateVisionOverlay();

                if (showVision && !IsFlipping()) // ← pre-flip中は判定OK
                {
                    RevealThievesInSight();
                }
            }

            if (showVision && !IsFlipping()) // ← pre-flip中は判定OK
            {
                var pl = board.player;
                if (pl != null && !pl.invincible && CanSeePlayer())
                    turn.TriggerGameOver(this);
            }
        }
    }

    // ===== 1ステップAI =====
    public void StepAI()
    {
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (turn == null) return;
        if (turn.gameOver || turn.cleared) return;

        // レース解消: pre-flip 終了直後のフレームで先に反転を確定させる
        if (pendingTurn && !IsPreFlipHolding())
            TryApplyPendingTurn();

        // 反転前待機 or 反転中 はこのTickの移動を止める
        if (IsPreFlipHolding() || IsFlipping()) return;

        // すでに移動中なら、このTick分をキュー
        if (board != null && board.smoothGuardMove && isMoving)
        {
            stepQueued = true;
            return;
        }

        DoOneStepCore();
    }

    // 実際の1手（watch更新＋移動/衝突処理＋視界→GO判定）
    private void DoOneStepCore()
    {
        // 監視向き更新（pre-flip/flip中は別処理で止める）
        UpdateFacingByWatchMode();

        if (patrolMode == PatrolMode.Static)
        {
            if (board.player != null && !board.player.invincible && CanSeePlayer())
                turn.TriggerGameOver(this);
            RevealThievesInSight();
            return;
        }

        Vector2Int tgt = GetCurrentTargetOrFallback(pos);

        Vector2Int step = DirToStep(tgt - pos);
        if (step == Vector2Int.zero)
        {
            // エンド到達→反転前待機（移動停止・視界維持）
            AdvanceTarget(true);
            if (IsPreFlipHolding() || IsFlipping()) return; // この手は終了
            tgt = GetCurrentTargetOrFallback(pos);
            step = DirToStep(tgt - pos);
        }

        if (step != Vector2Int.zero)
        {
            Vector2Int np = pos + step;
            if (board.IsWalkable(np))
            {
                targetYaw = FacingToYaw(StepToFacing(step));
                forward = step;

                if (board.snapGuardFacingOnMove) { currentYaw = targetYaw; }
                ApplyVisualByFacing();

                if (board.smoothGuardMove)
                {
                    isMoving = true;
                    gridFrom = pos;
                    gridTo = np;
                    moveFrom = board.GridToWorldActor(gridFrom);
                    moveTo = board.GridToWorldActor(gridTo);
                    moveT = 0f;
                    float cellsPerSec = Mathf.Max(0.1f, board.guardMoveCellsPerSec);
                    moveDur = 1f / cellsPerSec;
                }
                else
                {
                    pos = np;
                    transform.position = board.GridToWorldActor(pos);
                }
            }
            else
            {
                HandleBlocked(ref step);
            }
        }

        if (board.player != null && !board.player.invincible && CanSeePlayer())
            turn.TriggerGameOver(this);
        RevealThievesInSight();
    }
    // ブロック時の対処（反転時は移動を止めて小休止）
    private bool HandleBlocked(ref Vector2Int step)
    {
        if (patrolMode == PatrolMode.PingPong || patrolMode == PatrolMode.AutoEdgePingPong)
        {
            // 進行向きを反転 → まずは反転前待機（視界維持）
            pingDir *= -1;
            BeginPreFlipHold(-step, "blocked-pingpong");
            AdvanceTarget(); // 目標だけ進め直す（pauseOnEndpoint=false）
            return true;
        }
        else // Loop
        {
            if (bounceOnBlockedInLoop)
            {
                // 折り返し（逆走へ）。次Tickから逆側の頂点に向かう
                loopDir *= -1; // 進行向きを反転
                StepIndex(loopDir); // 目標も逆方向へ1つ戻す
                BeginPreFlipHold(-step, "blocked-loop-bounce");
                return true; // この手は停止
            }

            // 旧挙動: 次レグへローテして進める
            int tries = Mathf.Max(1, path.Count);
            for (int i = 0; i < tries; i++)
            {
                StepIndex(+1);
                Vector2Int tgt = GetCurrentTargetOrFallback(pos);
                step = DirToStep(tgt - pos);
                if (step == Vector2Int.zero) continue;
                if (board.IsWalkable(pos + step))
                {
                    pos += step;
                    transform.position = board.GridToWorldActor(pos);
                    forward = step;
                    return true;
                }
            }
            return false;
        }
    }
    // ターゲット管理
    private Vector2Int GetCurrentTargetOrFallback(Vector2Int fallback)
    {
        if (path.Count == 0)
        {
            if (patrolMode == PatrolMode.AutoEdgePingPong)
            {
                var a = FindEdge(fallback, Vector2Int.left);
                var b = FindEdge(fallback, Vector2Int.right);
                return (fallback - a).sqrMagnitude > (fallback - b).sqrMagnitude ? a : b;
            }
            return fallback;
        }
        return path[Mathf.Clamp(pathIndex, 0, path.Count - 1)];
    }

    // 折り返し時に小休止できるように拡張
    private void AdvanceTarget(bool pauseOnEndpoint = false)
    {
        if (path.Count == 0) return;

        if (patrolMode == PatrolMode.Loop)
        {
            Vector2Int prevForward = forward;
            StepIndex(loopDir);
            Vector2Int tgt = GetCurrentTargetOrFallback(pos);
            Vector2Int newStep = DirToStep(tgt - pos);

            if (newStep != Vector2Int.zero &&
                prevForward != Vector2Int.zero &&
                newStep != prevForward)
            {
                // 向きをまだ変えず、反転前待機だけ開始（視界維持）
                BeginPreFlipHold(newStep, "loop-turn");
            }
            return;
        }

        // 端点（PingPong系）
        bool atEnd = (pathIndex == 0) || (pathIndex == path.Count - 1);
        bool willTurn =
            (pathIndex == path.Count - 1 && pingDir > 0) ||
            (pathIndex == 0 && pingDir < 0);

        if (willTurn) pingDir *= -1;

        // 次の目標へ
        StepIndex(pingDir);

        if (pauseOnEndpoint && atEnd)
        {
            Vector2Int tgt = GetCurrentTargetOrFallback(pos);
            Vector2Int st = DirToStep(tgt - pos);
            if (st != Vector2Int.zero)
            {
                // ここでは向きを変えず、pre-flipのみ（視界維持）
                BeginPreFlipHold(st, "end-turn");
            }
            else
            {
                // 進行不可でも一応 flip 小休止のみ
                BeginFlipPause("end-turn");
            }
        }
    }
    private void StepIndex(int d)
    {
        if (path.Count == 0) { pathIndex = 0; return; }
        if (patrolMode == PatrolMode.Loop)
        {
            pathIndex = (pathIndex + d) % path.Count;
            if (pathIndex < 0) pathIndex += path.Count;
        }
        else
        {
            pathIndex = Mathf.Clamp(pathIndex + d, 0, path.Count - 1);
        }
    }

    private Vector2Int DirToStep(Vector2Int d)
    {
        if (d == Vector2Int.zero) return Vector2Int.zero;
        if (Mathf.Abs(d.x) >= Mathf.Abs(d.y)) return new Vector2Int((d.x > 0) ? 1 : -1, 0);
        else return new Vector2Int(0, (d.y > 0) ? 1 : -1);
    }

    // 向き/回転
    private float FacingToYaw(Facing f)
    {
        switch (f)
        {
            case Facing.Up: return 0f;
            case Facing.Right: return 90f;
            case Facing.Down: return 180f;
            case Facing.Left: return 270f;
        }
        return 0f;
    }

    // 見た目は「上下/左右」flipだけを担当（transform.rotationは触らない）
    private void ApplyVisualByFacing()
    {
        Facing curFacing = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);

        // コード優先: 方向ごとの固定マッピング（Inspector を無視）
        bool flipX = false;
        bool flipY = false;

        switch (curFacing)
        {
            // 右だけ上下反転していた件: 右は X/Y 両方反転に固定
            case Facing.Right: flipX = false; flipY = true; break;
            case Facing.Left: flipX = false; flipY = false; break;
            case Facing.Up: flipX = false; flipY = false; break;
            case Facing.Down: flipX = false; flipY = false; break;
        }

        ApplySpriteFlipAll(flipX, flipY);
        lastVisualFacing = curFacing;
    }

    // 追加: すべての SpriteRenderer に反転を適用（なければ localScale フォールバック）
    private void ApplySpriteFlipAll(bool flipX, bool flipY)
    {
        var srs = GetComponentsInChildren<SpriteRenderer>(true);
        if (srs != null && srs.Length > 0)
        {
            foreach (var sr in srs) { sr.flipX = flipX; sr.flipY = flipY; }
        }
        else
        {
            var ls = transform.localScale;
            ls.x = Mathf.Abs(ls.x) * (flipX ? -1f : 1f);
            ls.z = Mathf.Abs(ls.z) * (flipY ? -1f : 1f); // X=90°のため上下=Z
            transform.localScale = ls;
        }
    }

    private void ApplyVisualYaw()
    {
        // 見た目の回転だけを担当（flipは触らない）
        float yawVisual = currentYaw + visualYawOffsetDeg;
        transform.rotation = Quaternion.AngleAxis(yawVisual, Vector3.up) * baseRot;
    }

    // 反転小休止
    private bool IsFlipping() => Time.time < flippingUntil;
    private void BeginFlipPause() => BeginFlipPause("default");

    // 反転小休止
    private void BeginFlipPause(string reason)
    {
        // flipPauseSeconds をそのまま使用（pre-flipは別で1セル時間待つ）
        float pause = Mathf.Max(0f, flipPauseSeconds);

        flippingUntil = Time.time + pause;
        lastFlipReason = reason;

        // 反転確定直後は視界を消す（更新間隔を待たずに即時クリア）
        if (showVision) ClearVision();

        Debug.Log($"[GuardPause] {name} reason={reason} dur={pause:F3} until={flippingUntil:F3} t={Time.time:F3}");
    }

    // 前方(2D)単位ベクトル（0°=Up(+Z)）
    private static Vector2 YawToDir2D(float yawDeg)
    {
        float rad = yawDeg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
    }

    // 別名（互換用）：既存呼び出しを満たす
    private static Vector2 GetForward2DFromYaw(float yawDeg)
    {
        return YawToDir2D(yawDeg);
    }

    // 視界の子オブジェクト/メッシュをクリア
    // 視界の子オブジェクト/メッシュをクリア
    private void ClearVision()
    {
        if (visionRoot == null) return;

        if (visionMode == VisionMode.SmoothFan)
        {
            // Mesh を空にしつつ Renderer も明示的に無効化して完全に非表示化
            if (visionMesh != null) visionMesh.Clear();
            if (visionMr != null) visionMr.enabled = false; // ← 追加
            return;
        }

        for (int i = visionRoot.transform.childCount - 1; i >= 0; --i)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(visionRoot.transform.GetChild(i).gameObject);
            else
#endif
                Destroy(visionRoot.transform.GetChild(i).gameObject);
        }
    }
    // グリッド上で前方に進み、遮蔽セルで停止する簡易レイ
    private Vector2 CastVisionRay(Vector2Int start, float rad, int maxRange, float step, float startOffset = 0f)
    {
        Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)); // 0°=+Z
        Vector2 p = new Vector2(start.x + 0.5f, start.y + 0.5f) + dir * Mathf.Max(0f, startOffset);
        float remain = Mathf.Max(0f, maxRange);

        while (remain > 0f)
        {
            float adv = Mathf.Min(step, remain);
            Vector2 np = p + dir * adv;

            var gp = new Vector2Int(Mathf.FloorToInt(np.x), Mathf.FloorToInt(np.y));
            if (!board.InBounds(gp) || board.BlocksVision(gp))
                break;

            p = np;
            remain -= adv;
        }
        return p; // グリッド座標（中心基準）
    }

    // 視界：プレイヤーを見ているか
    public bool CanSeePlayer()
    {
        if (board == null || board.player == null) return false;
        if (IsFlipping()) return false;

        Vector2Int p = board.player.pos;
        if ((p - pos).sqrMagnitude > viewRange * viewRange) return false;

        if (visionMode == VisionMode.GridAligned)
        {
            Facing curF = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);
            float half = fovAngle * 0.5f;
            return IsCellVisibleGridAligned(p, curF, half);
        }

        // 非GridAlignedは見た目と同じサブセル原点補正を適用
        Facing curFacing = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);
        float yaw = FacingToYaw(curFacing) + visionYawOffsetDeg;
        Vector2 fwd = YawToDir2D(yaw).normalized;

        Vector3 wc = WorldCenter(pos, board.visionY);
        Vector3 offW = new Vector3(transform.position.x - wc.x, 0f, transform.position.z - wc.z);
        Vector2 origin = new Vector2(pos.x + 0.5f, pos.y + 0.5f)
                       + new Vector2(offW.x, offW.z)
                       + fwd * Mathf.Max(0f, visionOriginForwardOffset);

        Vector2 to = new Vector2(p.x + 0.5f, p.y + 0.5f);
        Vector2 dir = to - origin;
        if (dir.sqrMagnitude < 1e-6f) return false;
        dir.Normalize();

        if (Vector2.Angle(fwd, dir) > fovAngle * 0.5f) return false;
        return board.HasLineOfSight(pos, p);
    }

    // 追加: 汎用セル視認判定（泥棒用）
    public bool CanSeeCell(Vector2Int gp)
    {
        if (board == null) return false;
        if ((gp - pos).sqrMagnitude > viewRange * viewRange) return false;

        if (visionMode == VisionMode.GridAligned)
        {
            Facing curF = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);
            float half = fovAngle * 0.5f;
            return IsCellVisibleGridAligned(gp, curF, half);
        }

        Facing curFacing = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);
        float yaw = FacingToYaw(curFacing) + visionYawOffsetDeg;
        Vector2 fwd = YawToDir2D(yaw).normalized;

        Vector3 wc = WorldCenter(pos, board.visionY);
        Vector3 offW = new Vector3(transform.position.x - wc.x, 0f, transform.position.z - wc.z);
        Vector2 origin = new Vector2(pos.x + 0.5f, pos.y + 0.5f)
                       + new Vector2(offW.x, offW.z)
                       + fwd * Mathf.Max(0f, visionOriginForwardOffset);

        Vector2 to = new Vector2(gp.x + 0.5f, gp.y + 0.5f);
        Vector2 dir = to - origin;
        if (dir.sqrMagnitude < 1e-6f) return false;
        dir.Normalize();

        if (Vector2.Angle(fwd, dir) > fovAngle * 0.5f) return false;
        return board.HasLineOfSight(pos, gp);
    }

    // 追加: 視界内の泥棒を宝箱へ変える
    // 追加: 視界内の泥棒を変換（d=宝箱, e=鍵）
    private void RevealThievesInSight()
    {
        if (board == null || board.itemAt == null || board.itemAt.Count == 0) return;

        var toTreasure = new List<Vector2Int>();
        var toKey = new List<Vector2Int>();

        foreach (var kv in board.itemAt)
        {
            char sym = kv.Value.sym;
            if (sym != 'd' && sym != 'e') continue; // 対象は泥棒系のみ
            var p = kv.Key;
            if (CanSeeCell(p))
            {
                if (sym == 'd') toTreasure.Add(p);
                else if (sym == 'e') toKey.Add(p);
            }
        }

        for (int i = 0; i < toTreasure.Count; i++)
            board.TransformThiefToTreasureAt(toTreasure[i]);

        for (int i = 0; i < toKey.Count; i++)
            board.TransformThiefToKeyAt(toKey[i]);
    }
    // セル中心（BoardManager.CellCenter に統一）
    private Vector3 WorldCenter(Vector2Int p, float y)
    {
        return board.CellCenter(p, y);
    }

    // 追加: グリッド「中心座標系」の連続値(Vector2)をワールド座標に変換
    // center は (i+0.5, j+0.5) をセル中心とする座標系。
    // → CellCenter に (center - 0.5) のローカル差分を加えてワールドへ。
    private Vector3 WorldFromGridCenterCoords(Vector2 center, float y)
    {
        int cx = Mathf.FloorToInt(center.x);
        int cy = Mathf.FloorToInt(center.y);
        Vector3 c = board.CellCenter(new Vector2Int(cx, cy), y);
        float fx = center.x - cx - 0.5f; // [-0.5 .. +0.5)
        float fz = center.y - cy - 0.5f; // [-0.5 .. +0.5)
        return c + new Vector3(fx, 0f, fz);
    }

    // 旧スタイル（セル毎Quad）/ 新グリッド整合の描画
    public void UpdateVisionOverlay()
    {
        if (!showVision || IsFlipping()) { ClearVision(); return; }

        Vector3 worldCenter = WorldCenter(pos, board.visionY);
        Vector3 offsetWorld = new Vector3(transform.position.x - worldCenter.x, 0f, transform.position.z - worldCenter.z);
        Vector2 offset2D = new Vector2(offsetWorld.x, offsetWorld.z);

        // SmoothFan（ビジョン用Rendererは1つ）
        if (visionMode == VisionMode.SmoothFan)
        {
            BuildSmoothVisionMeshWithOffset(offsetWorld, offset2D);
            if (visionMr) ApplyVisionColor(visionMr);
            return;
        }

        // CellFan / GridAligned（セルごとQuad）
        var root = GetVisionRoot();
        ClearVision();

        int r = viewRange;
        float half = fovAngle * 0.5f;
        var mat = (board.guardVisionMat != null) ? board.guardVisionMat
                 : (board.ghostOkMat != null ? board.ghostOkMat : board.ghostNgMat);

        Facing curFacing = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);
        float yaw = FacingToYaw(curFacing) + visionYawOffsetDeg;
        Vector2 fwd = GetForward2DFromYaw(yaw).normalized;

        // CellFan は起点をサブセルまでずらす。GridAligned は判定がグリッド基準なので未使用
        Vector2 origin2D = new Vector2(pos.x + 0.5f, pos.y + 0.5f) + offset2D + fwd * Mathf.Max(0f, visionOriginForwardOffset);

        for (int yCell = pos.y - r; yCell <= pos.y + r; yCell++)
            for (int xCell = pos.x - r; xCell <= pos.x + r; xCell++)
            {
                var gp = new Vector2Int(xCell, yCell);
                if (!board.InBounds(gp)) continue;
                if ((gp - pos).sqrMagnitude > r * r) continue;

                bool visible = (visionMode == VisionMode.CellFan)
                    ? (board.HasLineOfSight(pos, gp) &&
                       Vector2.Angle(fwd, (new Vector2(xCell + 0.5f, yCell + 0.5f) - origin2D).normalized) <= half)
                    : IsCellVisibleGridAligned(gp, curFacing, half);

                if (!visible) continue;

                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = $"Vision_{xCell}_{yCell}";
                q.transform.SetParent(root.transform, false);
                q.transform.rotation = Quaternion.Euler(90, 0, 0);
                q.transform.localScale = Vector3.one;

                // ここがポイント：
                // - CellFan は見た目もサブセル追従（offsetWorld を加算）
                // - GridAligned はグリッド基準に固定（offsetWorld を加算しない）
                Vector3 drawPos = WorldCenter(gp, board.visionY);
                if (visionMode == VisionMode.CellFan)
                    drawPos += offsetWorld;
                q.transform.position = drawPos;

                var mr = q.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                if (mat != null) mr.material = mat;
                ApplyVisionColor(mr);
                Destroy(q.GetComponent<MeshCollider>());
            }
    }
    // GridAligned 用: マス単位の視界判定（階段状の前方扇形）
    private bool IsCellVisibleGridAligned(Vector2Int gp, Facing curFacing, float halfDeg)
    {
        // LoS が必要（両立）
        if (!board.HasLineOfSight(pos, gp)) return false;

        Vector2Int d = gp - pos;

        int forwardDist, lateral;
        switch (curFacing)
        {
            case Facing.Right:
                forwardDist = d.x;
                lateral = Mathf.Abs(d.y);
                break;
            case Facing.Left:
                forwardDist = -d.x;
                lateral = Mathf.Abs(d.y);
                break;
            case Facing.Up:
                forwardDist = d.y;
                lateral = Mathf.Abs(d.x);
                break;
            default: // Facing.Down
                forwardDist = -d.y;
                lateral = Mathf.Abs(d.x);
                break;
        }

        if (forwardDist <= 0) return false;           // 真後ろ/同位置は除外
        if (forwardDist > viewRange) return false;    // 射程外

        // 角度→グリッド幅へ離散化
        // 先頭列(forwardDist=1)は1マスだけ可視にするため (forwardDist - 1) を使用
        float slope = Mathf.Tan(halfDeg * Mathf.Deg2Rad);
        int lateralMax = Mathf.FloorToInt(Mathf.Max(0f, slope * (forwardDist - 1)) + 1e-4f);

        // fovAngle=90 の場合、幅は 1,3,5,...（きれいな三角形）
        return lateral <= lateralMax;
    }

    private void BuildSmoothVisionMesh()
    {
        // 互換: 旧呼び出しからはオフセット(0)で呼ぶ
        BuildSmoothVisionMeshWithOffset(Vector3.zero, Vector2.zero);
    }

    private void BuildSmoothVisionMeshWithOffset(Vector3 offsetWorld, Vector2 offset2D)
    {
        var root = GetVisionRoot();

        var mf = root.GetComponent<MeshFilter>();
        if (mf == null) mf = root.AddComponent<MeshFilter>();
        var mr = root.GetComponent<MeshRenderer>();
        if (mr == null) mr = root.AddComponent<MeshRenderer>();
        visionMf = mf;
        visionMr = mr;

        if (visionMesh == null)
        {
            visionMesh = new Mesh { name = "VisionFan" };
            visionMesh.MarkDynamic();
        }
        visionMf.sharedMesh = visionMesh;

        var mat = (board.guardVisionMat != null) ? board.guardVisionMat
                 : (board.ghostOkMat != null ? board.ghostOkMat : board.ghostNgMat);
        if (visionMr.sharedMaterial != mat) visionMr.sharedMaterial = mat;

        // flipPause 中は UpdateVisionOverlay が先に return するため通常ここは呼ばれませんが、
        // 念のため描画再開時に有効化
        if (!visionMr.enabled) visionMr.enabled = true; // ← 追加

        int rays = Mathf.Clamp(visionRayCount, 12, 256);
        float half = fovAngle * 0.5f;

        Facing curFacing = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);
        float yaw = FacingToYaw(curFacing) + visionYawOffsetDeg;
        Vector2 fwd = GetForward2DFromYaw(yaw).normalized;

        Vector3 origin = WorldCenter(pos, board.visionY) + offsetWorld + new Vector3(fwd.x, 0f, fwd.y) * Mathf.Max(0f, visionOriginForwardOffset);

        _visionVerts ??= new List<Vector3>(rays + 2);
        _visionTris ??= new List<int>(rays * 3);

        if (_visionVerts.Capacity < rays + 2) _visionVerts.Capacity = rays + 2;
        if (_visionTris.Capacity < rays * 3) _visionTris.Capacity = rays * 3;

        _visionVerts.Clear();
        _visionTris.Clear();

        _visionVerts.Add(origin);

        for (int i = 0; i <= rays; i++)
        {
            float t = (rays == 0) ? 0f : (i / (float)rays);
            float ang = (yaw - half) + (fovAngle * t);

            Vector2 end = CastVisionRay(pos, ang * Mathf.Deg2Rad, viewRange, visionRayStep, visionOriginForwardOffset);
            Vector3 v = WorldFromGridCenterCoords(end, board.visionY) + offsetWorld;
            _visionVerts.Add(v);
        }

        for (int i = 1; i < _visionVerts.Count - 1; i++)
        {
            _visionTris.Add(0); _visionTris.Add(i); _visionTris.Add(i + 1);
        }

        visionMesh.Clear();
        visionMesh.SetVertices(_visionVerts);
        visionMesh.SetTriangles(_visionTris, 0);
        visionMesh.RecalculateBounds();

        root.transform.rotation = Quaternion.identity;
    }

    private GameObject GetVisionRoot()
    {
        if (visionRoot == null)
        {
            visionRoot = new GameObject($"{name}_Vision");
            if (board != null && board.actorsRoot != null)
                visionRoot.transform.SetParent(board.actorsRoot, false);
            else
                visionRoot.transform.SetParent(null, false);

            visionRoot.transform.position = Vector3.zero;
            visionRoot.transform.rotation = Quaternion.identity;
            visionRoot.transform.localScale = Vector3.one;
        }
        return visionRoot;
    }

    private void EnsureFacingArrow()
    {
        if (facingArrow != null) return;
        facingArrow = GameObject.CreatePrimitive(PrimitiveType.Quad);
        facingArrow.name = "FacingArrow";
        facingArrow.transform.SetParent(transform, false);
        facingArrow.transform.localRotation = Quaternion.Euler(90, 0, 0);
        facingArrow.transform.localScale = new Vector3(0.25f, 0.25f, 1f);
        facingArrow.transform.localPosition = new Vector3(0f, 0.02f, 0.35f);
        var mr = facingArrow.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        if (board != null && board.ghostOkMat != null) mr.material = board.ghostOkMat;
        mr.material.color = facingArrowColor;
        Destroy(facingArrow.GetComponent<MeshCollider>());
    }

    private Facing StepToFacing(Vector2Int st)
    {
        if (st.x > 0) return Facing.Right;
        if (st.x < 0) return Facing.Left;
        if (st.y > 0) return Facing.Up;
        return Facing.Down;
    }

    private Vector2Int FacingToVec(Facing f)
    {
        switch (f)
        {
            case Facing.Up: return Vector2Int.up;
            case Facing.Down: return Vector2Int.down;
            case Facing.Left: return Vector2Int.left;
            default: return Vector2Int.right;
        }
    }

    // 追加: 視界メッシュ用ワークバッファ（毎フレームの割当削減）
    private List<Vector3> _visionVerts = null;
    private List<int> _visionTris = null;

    private void UpdateFacingByWatchMode()
    {
        if (watchMode == WatchMode.OneDir) return;
        if (IsPreFlipHolding() || IsFlipping()) return; // ← 追加: 待機中は回さない
        if (Time.time - lastRotateTime < rotatePeriod) return;
        lastRotateTime = Time.time;

        switch (watchMode)
        {
            case WatchMode.TwoDirUD:
                startFacing = (startFacing == Facing.Up) ? Facing.Down : Facing.Up;
                break;
            case WatchMode.TwoDirLR:
                startFacing = (startFacing == Facing.Left) ? Facing.Right : Facing.Left;
                break;
            case WatchMode.Rotate4Dir:
                facingIndex = (facingIndex + (rotateClockwise ? 1 : 3)) & 3;
                startFacing = (Facing)facingIndex;
                break;
        }

        forward = FacingToVec(startFacing);
        targetYaw = FacingToYaw(startFacing);

        if (board != null && board.snapGuardFacingOnMove)
        {
            currentYaw = targetYaw;
            ApplyVisualYaw();
        }

        BeginFlipPause("watch-rotate");
        ApplyVisualByFacing();
    }
    // 外部から切替したい場合に呼べるトグル
    public void SetVisionMode(VisionMode mode)
    {
        visionMode = mode;
        UpdateVisionOverlay();
    }

    // ======== 追加: GameOver時の強調表示 ========
    // 強調表示のON/OFF
    public void SetKillerHighlight(bool on)
    {
        if (on == _killerHighlighted) return;
        _killerHighlighted = on;

        if (on)
        {
            if (_bodyRenderers == null) CacheBodyRenderers();

            _savedVisionColor = visionColor;
            if (useBodyTintOnHighlight) ApplyBodyTint(killerBodyTint);
            visionColor = killerVisionColor;
            UpdateVisionOverlay();
            ShowKillerOutline(true);
        }
        else
        {
            if (useBodyTintOnHighlight) RestoreBodyTint();
            visionColor = _savedVisionColor;
            UpdateVisionOverlay();
            ShowKillerOutline(false);
        }
    }

    public void SetVisionColorAndRefresh(Color c)
    {
        visionColor = c;
        UpdateVisionOverlay();
    }

    // ===== 追加: アウトライン（周りに色） =====
    public void ShowKillerOutline(bool on)
    {
        if (on)
        {
            if (_killerOutlineLR == null) BuildKillerOutline();
            if (_killerOutlineLR != null) _killerOutlineLR.enabled = true;
        }
        else
        {
            if (_killerOutlineLR != null) _killerOutlineLR.enabled = false;
        }
    }

    private void BuildKillerOutline()
    {
        if (board == null) return;

        var go = new GameObject("KillerOutline");
        if (board.actorsRoot != null) go.transform.SetParent(board.actorsRoot, false);
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true; // ワールド空間で直接描く
        lr.loop = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.textureMode = LineTextureMode.Stretch;
        lr.alignment = LineAlignment.View; // 水平リングで問題なし
        lr.startWidth = killerOutlineWidth;
        lr.endWidth = killerOutlineWidth;

        var mat = new Material(Shader.Find("Sprites/Default"));
        lr.material = mat;
        lr.startColor = killerOutlineColor;
        lr.endColor = killerOutlineColor;
        lr.numCornerVertices = 2;
        lr.numCapVertices = 2;

        int seg = Mathf.Clamp(killerOutlineSegments, 12, 128);
        lr.positionCount = seg;
        float r = Mathf.Max(0.01f, killerOutlineRadius);

        Vector3 center = transform.position + new Vector3(0f, killerOutlineYOffset, 0f);
        for (int i = 0; i < seg; i++)
        {
            float t = (i / (float)seg) * Mathf.PI * 2f;
            float x = Mathf.Cos(t) * r;
            float z = Mathf.Sin(t) * r; // XZ平面に配置
            lr.SetPosition(i, new Vector3(center.x + x, center.y, center.z + z));
        }

        _killerOutlineLR = lr;
    }
    private void ClearKillerOutline()
    {
        if (_killerOutlineLR != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(_killerOutlineLR.gameObject);
            else
#endif
                Destroy(_killerOutlineLR.gameObject);
            _killerOutlineLR = null;
        }
    }

    // ===== 追加: 体のRendererキャッシュ/ティント適用/復元 =====
    private void CacheBodyRenderers()
    {
        _bodySRs = GetComponentsInChildren<SpriteRenderer>(true);
        if (_bodySRs != null && _bodySRs.Length > 0)
        {
            _bodySRsDefault = new Color[_bodySRs.Length];
            for (int i = 0; i < _bodySRs.Length; i++)
                _bodySRsDefault[i] = _bodySRs[i].color;
        }
        var allRends = GetComponentsInChildren<Renderer>(true);
        var list = new List<Renderer>(allRends.Length);
        for (int i = 0; i < allRends.Length; i++)
        {
            var r = allRends[i];
            if (r == null) continue;
            if (facingArrow != null && r.gameObject == facingArrow) continue; // 矢印は除外
            list.Add(r);
        }
        _bodyRenderers = list.ToArray();
        _bodyMpb = new MaterialPropertyBlock();
    }

    private void ApplyBodyTint(Color tint)
    {
        if (_bodySRs != null)
        {
            for (int i = 0; i < _bodySRs.Length; i++)
                if (_bodySRs[i]) _bodySRs[i].color = tint;
        }
        if (_bodyRenderers != null)
        {
            for (int i = 0; i < _bodyRenderers.Length; i++)
            {
                var r = _bodyRenderers[i];
                if (!r) continue;
                var mat = r.sharedMaterial;
                if (!mat) continue;

                _bodyMpb.Clear();
                bool setAny = false;
                if (mat.HasProperty("_Color")) { _bodyMpb.SetColor("_Color", tint); setAny = true; }
                if (mat.HasProperty("_BaseColor")) { _bodyMpb.SetColor("_BaseColor", tint); setAny = true; }
                if (mat.HasProperty("_TintColor")) { _bodyMpb.SetColor("_TintColor", tint); setAny = true; }
                if (setAny) r.SetPropertyBlock(_bodyMpb);
            }
        }
    }

    private void RestoreBodyTint()
    {
        if (_bodySRs != null && _bodySRsDefault != null && _bodySRsDefault.Length == _bodySRs.Length)
        {
            for (int i = 0; i < _bodySRs.Length; i++)
                if (_bodySRs[i]) _bodySRs[i].color = _bodySRsDefault[i];
        }
        if (_bodyRenderers != null)
        {
            for (int i = 0; i < _bodyRenderers.Length; i++)
                if (_bodyRenderers[i]) _bodyRenderers[i].SetPropertyBlock(null);
        }
    }

    // ===== 追加: 視界色の適用 =====
    private void ApplyVisionColor(Renderer r)
    {
        if (!r) return;
        var mat = r.sharedMaterial;
        if (!mat) return;

        _mpb ??= new MaterialPropertyBlock();
        _mpb.Clear();

        bool setAny = false;
        if (mat.HasProperty("_Color")) { _mpb.SetColor("_Color", visionColor); setAny = true; }
        if (mat.HasProperty("_BaseColor")) { _mpb.SetColor("_BaseColor", visionColor); setAny = true; }
        if (mat.HasProperty("_TintColor")) { _mpb.SetColor("_TintColor", visionColor); setAny = true; }

        if (setAny) r.SetPropertyBlock(_mpb);
        else
        {
            var inst = r.material;
            if (inst.HasProperty("_Color")) inst.SetColor("_Color", visionColor);
            else if (inst.HasProperty("_BaseColor")) inst.SetColor("_BaseColor", visionColor);
            else if (inst.HasProperty("_TintColor")) inst.SetColor("_TintColor", visionColor);
        }

        // 追加: 犯人は後描画にする
        ApplyVisionRenderOrder(r);
    }
    // 透明レンダーキュー（犯人を後描画にする）
    private const int VisionQueueBase = 3000;          // Transparent
    private const int VisionQueueKiller = VisionQueueBase + 20; // 犯人用に少し後ろ

    private void ApplyVisionRenderOrder(Renderer r)
    {
        if (!r) return;

        if (_killerHighlighted)
        {
            // 犯人はマテリアルインスタンス化してレンダーキューを上げる
            var inst = r.material; // インスタンス化
            if (inst != null) inst.renderQueue = VisionQueueKiller;

            // 念のためソーティングオーダーも上げておく（同一距離のタイブレーク）
            r.sortingOrder = 10;
        }
        else
        {
            // 非犯人はデフォルトのまま（共有マテリアルのキュー=3000）
            r.sortingOrder = 0;
            // ここで inst に戻す必要は特にありません（再生成時に共有マテリアルへ戻るため）
        }
    }

    // ===== 追加: 『！』表示（寿命なし常時表示/破棄） =====
    public void ShowKillerMarkPersistent()
    {
        HideKillerMark();
        if (exclamationSprite == null) return;

        _killerMarkGO = new GameObject("KillerMark_Exclamation");
        if (board != null && board.actorsRoot != null)
            _killerMarkGO.transform.SetParent(board.actorsRoot, false);

        var sr = _killerMarkGO.AddComponent<SpriteRenderer>();
        sr.sprite = exclamationSprite;
        sr.color = exclamationTint;
        if (!string.IsNullOrEmpty(exclamationSortingLayer))
            sr.sortingLayerName = exclamationSortingLayer;
        sr.sortingOrder = exclamationSortingOrder;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;

        // ワールド相対位置（exclamationWorldOffset が優先。後方互換として y=0 の場合は exclamationHeight を足す）
        Vector3 offset = exclamationWorldOffset;
        if (Mathf.Approximately(offset.y, 0f) && exclamationHeight > 0f)
            offset.y = exclamationHeight;

        Vector3 worldPos = transform.position + offset + Vector3.up * Mathf.Max(0f, exclamationLiftEpsilon);
        _killerMarkGO.transform.position = worldPos;

        // 姿勢：床に寝かせる or 直立
        float pitch = exclamationLayOnFloor ? 90f : 0f;
        _killerMarkGO.transform.rotation = Quaternion.Euler(pitch, exclamationWorldYaw, 0f);

        // 表示サイズ
        _killerMarkGO.transform.localScale = new Vector3(exclamationSize.x, exclamationSize.y, 1f);
    }
    private void HideKillerMark()
    {
        if (_killerMarkGO == null) return;
#if UNITY_EDITOR
        if (!Application.isPlaying) DestroyImmediate(_killerMarkGO);
        else
#endif
            Destroy(_killerMarkGO);
        _killerMarkGO = null;
    }

    private void OnDestroy()
    {
        // 視界オーバーレイを確実に破棄
        if (visionRoot != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(visionRoot);
            else
#endif
                Destroy(visionRoot);
            visionRoot = null;
        }

        // 向きガイドも破棄
        if (facingArrow != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(facingArrow);
            else
#endif
                Destroy(facingArrow);
            facingArrow = null;
        }

        // 追加: 『！』を確実に破棄＋アウトライン破棄
        HideKillerMark();
        ClearKillerOutline();
    }

    // 個別停止の診断用
    private string lastFlipReason = "";
    private bool flipHoldLogged = false;

    // 移動完了直後に1手だけ消化するためのラッパ
    private void RunQueuedStepOnce()
    {
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (turn == null) return;
        if (turn.gameOver || turn.cleared) return;

        // レース解消: pre-flip 終了直後は反転を先に確定
        if (pendingTurn && !IsPreFlipHolding())
            TryApplyPendingTurn();

        if (IsPreFlipHolding() || IsFlipping()) return;

        DoOneStepCore();
    }

    // 反転前待機中か
    private bool IsPreFlipHolding() => preFlipActive && Time.time < preFlipUntil;

    // 反転前待機を開始（移動だけ止める。視界はそのまま）
    private void BeginPreFlipHold(Vector2Int newForward, string reason)
    {
        // Inspector で指定した固定秒数を使用
        float hold = Mathf.Max(0f, preFlipHoldSeconds);

        preFlipActive = true;
        preFlipUntil = Time.time + hold;

        pendingTurn = true;
        pendingForward = newForward;
        pendingTargetYaw = FacingToYaw(StepToFacing(newForward));
        pendingFlipReason = reason;

        // 視界は維持（消さない）
        Debug.Log($"[GuardPause] {name} preHold={hold:F3}s reason={reason} until={preFlipUntil:F3}");
    }

    // 反転前待機が終わったら反転を適用し、flip pause へ遷移
    private void TryApplyPendingTurn()
    {
        if (!pendingTurn) return;
        if (IsPreFlipHolding()) return;

        // ここで初めて向きを反転
        forward = pendingForward;
        targetYaw = pendingTargetYaw;

        if (board != null && board.snapGuardFacingOnMove)
        {
            currentYaw = targetYaw;
            ApplyVisualYaw();
        }
        ApplyVisualByFacing();

        pendingTurn = false;
        preFlipActive = false;
        preFlipUntil = 0f;

        // 次段: 反転後は視界OFFの小休止
        BeginFlipPause(pendingFlipReason);
        pendingFlipReason = "";
    }
}