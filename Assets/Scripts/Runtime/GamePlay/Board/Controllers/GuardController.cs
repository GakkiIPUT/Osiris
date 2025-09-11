using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ガードの巡回・視界・向き制御を担当するコンポーネント。
/// 巡回パターン、視界メッシュの更新、プレイヤー検知、ゲームオーバー演出に対応する。
/// </summary>
public partial class GuardController : MonoBehaviour
{
    public enum PatrolMode
    { Static, AutoEdgePingPong, PingPong, Loop }

    public enum WatchMode
    { OneDir, TwoDirUD, TwoDirLR, Rotate4Dir }

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
    public bool bounceOnBlockedInLoop = true;

    public enum Facing
    { Up, Right, Down, Left }

    public Facing startFacing = Facing.Right;

    [Header("Watch (Viewing)")]
    public WatchMode watchMode = WatchMode.OneDir;

    [Tooltip("Rotate4Dir の周期（秒）")]
    public float rotatePeriod = 1.0f;

    [Tooltip("Rotate4Dir の回転方向（右回り）")]
    public bool rotateClockwise = true;

    // 経路管理
    private readonly List<Vector2Int> path = new();

    private int pathIndex = 0;
    private int pingDir = +1;
    private int loopDir = +1;

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
    private bool stepQueued = false;
    private float currentYaw = 0f;
    private float targetYaw = 0f;
    private Quaternion baseRot = Quaternion.identity;
    private float visionTimer = 0f;

    /// <summary>現在移動中か</summary>
    public bool IsMoving => isMoving;

    /// <summary>現在のグリッド座標</summary>
    public Vector2Int CurrentPos => pos;

    /// <summary>次フレーム到達予定の座標（移動中は gridTo、停止中は pos）</summary>
    public Vector2Int NextPos => isMoving ? gridTo : pos;

    // 視界モード
    public enum VisionMode
    { SmoothFan, CellFan, GridAligned }

    [Header("Vision Mode")]
    [Tooltip("視界の描画・判定モード。SmoothFan=滑らか扇形, CellFan=セル扇形, GridAligned=グリッド整合")]
    public VisionMode visionMode = VisionMode.SmoothFan;

    [Header("Vision Render")]
    public bool smoothFanVision = true;

    [Range(12, 256)] public int visionRayCount = 72;
    [Min(0.02f)] public float visionRayStep = 0.1f;

    [Tooltip("視界原点を前方へずらす距離（セル単位）。0.0=セル中心, 1.0=前方1マス")]
    [Range(0f, 1.0f)] public float visionOriginForwardOffset = 0f;

    [Header("Yaw Offsets (deg)")]
    [Tooltip("見た目用のYawオフセット（スプライト向き補正）。例: +90")]
    public float visualYawOffsetDeg = 90f;

    [Tooltip("視界ロジック用のYawオフセット（角度判定・扇形補正）。通常は0")]
    public float visionYawOffsetDeg = 0f;

    private MeshFilter visionMf;
    private MeshRenderer visionMr;
    private Mesh visionMesh;

    [Header("Flip (Reverse) Control")]
    [Tooltip("反転（監視向き切替/行動反転）時に停止する秒数（視界も無効）")]
    [Min(0f)] public float flipPauseSeconds = 0.5f;

    private float flippingUntil = 0f;

    [Tooltip("反転前に停止する時間（視界は維持）。デフォルト0.5s")]
    [Min(0f)] public float preFlipHoldSeconds = 0.5f;

    private float preFlipUntil = 0f;
    private bool preFlipActive = false;
    private bool pendingTurn = false;
    private Vector2Int pendingForward;
    private float pendingTargetYaw = 0f;
    private string pendingFlipReason = "";

    [Header("Visual Aid")]
    public bool showFacingArrow = true;

    public Color facingArrowColor = new Color(1f, 1f, 0.25f, 0.9f);
    private GameObject facingArrow;
    private Facing lastVisualFacing;

    public enum FlipProfile
    { Canonical, Requested, Inverted }

    [Header("Sprite Facing Calibration")]
    [Tooltip("スプライト反転の割り当てプリセット")]
    [HideInInspector] public FlipProfile flipProfile = FlipProfile.Requested;

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
    [HideInInspector] public bool usePerDirectionFlip = true;

    [HideInInspector] public VisualCorrection visUp = new VisualCorrection { yawAdd = 90f, flipX = false, flipY = false };
    [HideInInspector] public VisualCorrection visRight = new VisualCorrection { yawAdd = 0f, flipX = false, flipY = false };
    [HideInInspector] public VisualCorrection visDown = new VisualCorrection { yawAdd = 90f, flipX = false, flipY = false };
    [HideInInspector] public VisualCorrection visLeft = new VisualCorrection { yawAdd = 90f, flipX = false, flipY = false };

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

    [Header("Game Over Highlight")]
    [Tooltip("ゲームオーバー時にボディも赤くする（オフならボディは変えず、アウトラインのみ）")]
    public bool useBodyTintOnHighlight = false;

    [Tooltip("ゲームオーバー時に『倒した敵』として強調するボディ色（SpriteRenderer.tint）")]
    public Color killerBodyTint = new Color(1f, 0.2f, 0.2f, 1f);

    [Tooltip("ゲームオーバー時に『倒した敵』として強調する視界色")]
    public Color killerVisionColor = new Color(1f, 0.2f, 0.2f, 0.6f);

    [Tooltip("ゲームオーバー時、犯人以外の視界色（薄くする等）")]
    public Color othersVisionColorOnGameOver = new Color(1f, 1f, 1f, 0.15f);

    [Header("Killer Outline (Ring)")]
    [Tooltip("アウトライン色")]
    public Color killerOutlineColor = new Color(1f, 0.2f, 0.2f, 0.8f);

    [Tooltip("半径（セル単位）")] public float killerOutlineRadius = 0.45f;
    [Tooltip("線幅（ワールド単位）")] public float killerOutlineWidth = 0.05f;
    [Tooltip("Yオフセット（床からの高さ）")] public float killerOutlineYOffset = 0.02f;
    [Tooltip("円のセグメント数（多いほど滑らか）")][Range(12, 128)] public int killerOutlineSegments = 48;

    [Header("Killer Mark (!) / World Placement")]
    [Tooltip("ガードのワールド位置に対する相対オフセット（X/Z/高さ）。例: (0,0.7,0) で頭上")]
    public Vector3 exclamationWorldOffset = new Vector3(0f, 0.7f, 0f);

    [Tooltip("『！』を床面に寝かせる（X=90°）。OFFなら直立（X=0°）")]
    public bool exclamationLayOnFloor = true;

    [Tooltip("『！』のワールドY軸回転（度）")] public float exclamationWorldYaw = 0f;

    [Tooltip("SpriteRenderer の Sorting Layer 名（空なら変更しない）")]
    public string exclamationSortingLayer = "";

    [Tooltip("SpriteRenderer の Sorting Order（大きいほど手前）")]
    public int exclamationSortingOrder = 2000;

    [Tooltip("Zファイティング回避の微小浮上量（メートル）")]
    public float exclamationLiftEpsilon = 0.001f;

    public float exclamationHeight = 0.7f;
    public bool exclamationUseLocalOffset = true;
    public Vector3 exclamationLocalOffset = new Vector3(0f, 0f, 0.7f);

    [Tooltip("『！』に使用するスプライト（必須）")]
    public Sprite exclamationSprite;

    [Tooltip("『！』の色（ティント）")]
    public Color exclamationTint = new Color(1f, 0.15f, 0.15f, 1f);

    [Tooltip("『！』の表示サイズ（ワールド単位）")]
    public Vector2 exclamationSize = new Vector2(0.35f, 0.35f);

    private SpriteRenderer[] _bodySRs;
    private Color[] _bodySRsDefault;
    private Renderer[] _bodyRenderers;
    private MaterialPropertyBlock _bodyMpb;
    private Color _savedVisionColor;
    private bool _killerHighlighted = false;
    private GameObject _killerMarkGO;
    private LineRenderer _killerOutlineLR;

    /// <summary>
    /// 初期の向きを設定し、見た目と視界を同期する。
    /// </summary>
    public void SetFacing(Facing f)
    {
        startFacing = f;
        forward = FacingToVec(f);
        targetYaw = FacingToYaw(f);
        currentYaw = targetYaw;

        ApplyVisualByFacing();
        ApplyVisualYaw();
        CacheBodyRenderers();

        if (showFacingArrow) EnsureFacingArrow();
        UpdateVisionOverlay();
    }

    /// <summary>
    /// ガードの初期化。位置・巡回パス・向き・視界の準備を行う。
    /// </summary>
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

        baseRot = Quaternion.Euler(90f, 0f, 0f);
        ApplyVisualByFacing();
        ApplyVisualYaw();
        CacheBodyRenderers();

        if (showFacingArrow) EnsureFacingArrow();
        UpdateVisionOverlay();
    }

    /// <summary>
    /// ガードの毎フレーム更新。回転/移動の補間、視界更新、プレイヤー検知を実施する。
    /// </summary>
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

    /// <summary>
    /// 1ステップ分のAIを実行する（巡回/向き更新/移動/検知）。
    /// </summary>
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
}
