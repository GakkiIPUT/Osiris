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
    BoardManager board;
    TurnManager turn;

    [Header("State")]
    public Vector2Int pos;
    Vector2Int forward = Vector2Int.right;

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

    public enum Facing { Up, Right, Down, Left }
    public Facing startFacing = Facing.Right;

    [Header("Watch (Viewing)")]
    public WatchMode watchMode = WatchMode.OneDir;
    [Tooltip("Rotate4Dir の周期（秒）")]
    public float rotatePeriod = 1.0f;
    [Tooltip("Rotate4Dir の回転方向（右回り）")]
    public bool rotateClockwise = true;

    // 経路
    readonly List<Vector2Int> path = new();
    int pathIndex = 0;
    int pingDir = +1;

    // 視界
    public bool showVision = true;
    GameObject visionRoot;

    // 監視内部状態
    float lastRotateTime = -999f;
    int facingIndex = 0;
    List<Vector2Int> _tmpFwds;

    // スムーズ移動/回転
    bool isMoving = false;
    Vector3 moveFrom, moveTo;
    Vector2Int gridFrom, gridTo;
    float moveT = 0f;
    float moveDur = 0.25f;

    // 追加: Tick取りこぼし対策（移動完了直後に即1手消化）
    bool stepQueued = false;

    float currentYaw = 0f;
    float targetYaw = 0f;

    // 基本回転（常にX=90°：上向き）
    Quaternion baseRot = Quaternion.identity;
    float visionTimer = 0f;

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

    MeshFilter visionMf;
    MeshRenderer visionMr;
    Mesh visionMesh;

    // ===== 反転時の挙動 =====
    [Header("Flip (Reverse) Control")]
    [Tooltip("反転（監視向き切替/行動反転）時に停止する秒数（視界も無効）")]
    [Min(0f)] public float flipPauseSeconds = 0.5f; // ← 0.5s に変更
    float flippingUntil = 0f;

    // ===== Visual Aid =====
    [Header("Visual Aid")]
    public bool showFacingArrow = true;
    public Color facingArrowColor = new Color(1f, 1f, 0.25f, 0.9f);
    GameObject facingArrow;

    // 直近に適用した見た目用の向き（反転再適用用）
    Facing lastVisualFacing;

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
    [HideInInspector] public VisualCorrection visUp    = new VisualCorrection { yawAdd = 90f, flipX = false, flipY = false };
    [HideInInspector] public VisualCorrection visRight = new VisualCorrection { yawAdd = 0f,  flipX = false, flipY = false };
    [HideInInspector] public VisualCorrection visDown  = new VisualCorrection { yawAdd = 90f, flipX = false, flipY = false };
    [HideInInspector] public VisualCorrection visLeft  = new VisualCorrection { yawAdd = 90f, flipX = false, flipY = false };

    // 方向→補正取得
    VisualCorrection VC(Facing f)
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

    MaterialPropertyBlock _mpb;

    // 色の適用（マテリアルの色プロパティ名差異に対応）
    void ApplyVisionColor(Renderer r)
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

        if (setAny)
        {
            r.SetPropertyBlock(_mpb);
        }
        else
        {
            // フォールバック（1回だけ色を差し替え）：SmoothFanは単一Renderer、CellFanは生成時のみ
            // ここはマテリアルインスタンスを生成する点に注意（毎フレームは呼ばれない）
            var inst = r.material;
            if (inst.HasProperty("_Color")) inst.SetColor("_Color", visionColor);
            else if (inst.HasProperty("_BaseColor")) inst.SetColor("_BaseColor", visionColor);
            else if (inst.HasProperty("_TintColor")) inst.SetColor("_TintColor", visionColor);
        }
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

        if (showFacingArrow) EnsureFacingArrow();
        UpdateVisionOverlay();
    }

    // パターン構築
    void BuildPatrolPath(Vector2Int start)
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

    Vector2Int RunUntilBlocked(Vector2Int from, Vector2Int dir, int maxSteps)
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

    Vector2Int FindEdge(Vector2Int start, Vector2Int dir)
    {
        var p = start;
        while (true)
        {
            var np = p + dir;
            if (!board.InBounds(np) || !board.IsWalkable(np)) return p;
            p = np;
        }
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (turn == null || turn.gameOver || turn.cleared) return;

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

        // 視界更新（更新頻度を抑制してGC/負荷を軽減）…省略（既存の60fps上限のまま）
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
            }
        }
    }

    // ===== 1ステップAI =====
    public void StepAI()
    {
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (turn == null) return;
        if (turn.gameOver || turn.cleared) return;

        // 反転中は動作・視界判定を停止
        if (IsFlipping()) return;

        // すでに移動中なら、このTick分をキューして即終了（取りこぼしを後で消化）
        if (board != null && board.smoothGuardMove && isMoving)
        {
            stepQueued = true;
            return;
        }

        DoOneStepCore(); // 通常の1手実行
    }

    // 実際の1手（watch更新＋移動/衝突処理＋視界→GO判定）
    void DoOneStepCore()
    {
        // 監視向き更新
        UpdateFacingByWatchMode();

        if (patrolMode == PatrolMode.Static)
        {
            if (board.player != null && !board.player.invincible && CanSeePlayer())
                turn.TriggerGameOver();
            // 静止監視でも泥棒を発見したら宝箱化
            RevealThievesInSight();
            return;
        }

        Vector2Int tgt = GetCurrentTargetOrFallback(pos);

        Vector2Int step = DirToStep(tgt - pos);
        if (step == Vector2Int.zero)
        {
            // エンド到達→折り返し時に小休止（視界OFF）
            AdvanceTarget(true);
            if (IsFlipping()) return; // この手は終了（停止中）
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
            turn.TriggerGameOver();
        // 移動系の処理後に泥棒の発見→宝箱化
        RevealThievesInSight();
    }

    // ブロック時の対処（反転時は移動を止めて小休止）
    bool HandleBlocked(ref Vector2Int step)
    {
        if (patrolMode == PatrolMode.PingPong || patrolMode == PatrolMode.AutoEdgePingPong)
        {
            // 反転して停止（即時移動はしない）
            pingDir *= -1;
            forward = -step;
            targetYaw = FacingToYaw(StepToFacing(forward));
            if (board.snapGuardFacingOnMove)
            {
                currentYaw = targetYaw;
                ApplyVisualYaw();
            }
            BeginFlipPause("blocked-pingpong");
            AdvanceTarget(); // 目標も進め直し
            return true;
        }
        else // Loop
        {
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
    Vector2Int GetCurrentTargetOrFallback(Vector2Int fallback)
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
    void AdvanceTarget(bool pauseOnEndpoint = false)
    {
        if (path.Count == 0) return;

        if (patrolMode == PatrolMode.Loop)
        {
            StepIndex(+1);
            return;
        }

        // 端点にいるか
        bool atEnd = (pathIndex == 0) || (pathIndex == path.Count - 1);

        // PingPong の進行向き反転が必要か（従来条件）
        bool willTurn =
            (pathIndex == path.Count - 1 && pingDir > 0) ||
            (pathIndex == 0 && pingDir < 0);

        if (willTurn) pingDir *= -1;

        // 次の目標へ
        StepIndex(pingDir);

        // 端点にいたら、初回（pingDir非反転）でも必ず小休止
        if (pauseOnEndpoint && atEnd)
        {
            Vector2Int tgt = GetCurrentTargetOrFallback(pos);
            Vector2Int st = DirToStep(tgt - pos);
            if (st != Vector2Int.zero)
            {
                forward = st;
                targetYaw = FacingToYaw(StepToFacing(st));
                if (board != null && board.snapGuardFacingOnMove)
                {
                    currentYaw = targetYaw;
                    ApplyVisualYaw();
                }
                ApplyVisualByFacing();
            }
            BeginFlipPause("end-turn");
        }
    }

    void StepIndex(int d)
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

    Vector2Int DirToStep(Vector2Int d)
    {
        if (d == Vector2Int.zero) return Vector2Int.zero;
        if (Mathf.Abs(d.x) >= Mathf.Abs(d.y)) return new Vector2Int((d.x > 0) ? 1 : -1, 0);
        else return new Vector2Int(0, (d.y > 0) ? 1 : -1);
    }

    // 向き/回転
    float FacingToYaw(Facing f)
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
    void ApplyVisualByFacing()
    {
        Facing curFacing = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);

        // コード優先: 方向ごとの固定マッピング（Inspector を無視）
        bool flipX = false;
        bool flipY = false;

        switch (curFacing)
        {
            // 右だけ上下反転していた件: 右は X/Y 両方反転に固定
            case Facing.Right: flipX = false;  flipY = true;  break;
            case Facing.Left:  flipX = false;  flipY = false; break;
            case Facing.Up:    flipX = false;  flipY = false; break;
            case Facing.Down:  flipX = false;  flipY = false; break;
        }

        ApplySpriteFlipAll(flipX, flipY);
        lastVisualFacing = curFacing;
    }

    // 追加: すべての SpriteRenderer に反転を適用（なければ localScale フォールバック）
    void ApplySpriteFlipAll(bool flipX, bool flipY)
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

    void ApplyVisualYaw()
    {
        // 見た目の回転だけを担当（flipは触らない）
        float yawVisual = currentYaw + visualYawOffsetDeg;
        transform.rotation = Quaternion.AngleAxis(yawVisual, Vector3.up) * baseRot;
    }

    // 反転小休止
    bool IsFlipping() => Time.time < flippingUntil;
    void BeginFlipPause() => BeginFlipPause("default");

    void BeginFlipPause(string reason)
    {
        flippingUntil = Time.time + Mathf.Max(0f, flipPauseSeconds);
        lastFlipReason = reason;
        Debug.Log($"[GuardPause] {name} reason={reason} dur={flipPauseSeconds:F3} until={flippingUntil:F3} t={Time.time:F3}");
    }

    // 前方(2D)単位ベクトル（0°=Up(+Z)）
    static Vector2 YawToDir2D(float yawDeg)
    {
        float rad = yawDeg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
    }

    // 別名（互換用）：既存呼び出しを満たす
    static Vector2 GetForward2DFromYaw(float yawDeg)
    {
        return YawToDir2D(yawDeg);
    }

    // 視界の子オブジェクト/メッシュをクリア
    void ClearVision()
    {
        if (visionRoot == null) return;

        if (visionMode == VisionMode.SmoothFan)
        {
            if (visionMesh != null) visionMesh.Clear();
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
    Vector2 CastVisionRay(Vector2Int start, float rad, int maxRange, float step, float startOffset = 0f)
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
    public　bool CanSeePlayer()
    {
        if (board == null || board.player == null) return false;
        if (IsFlipping()) return false;

        Vector2Int p = board.player.pos;
        if ((p - pos).sqrMagnitude > viewRange * viewRange) return false;

        // GridAligned はマス基準の専用判定
        if (visionMode == VisionMode.GridAligned)
        {
            Facing curF = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);
            float half = fovAngle * 0.5f;
            return IsCellVisibleGridAligned(p, curF, half);
        }

        // それ以外は従来の角度＋LoS
        Facing curFacing = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);
        float yaw = FacingToYaw(curFacing) + visionYawOffsetDeg;
        Vector2 fwd = YawToDir2D(yaw).normalized;

        Vector2 origin = new Vector2(pos.x + 0.5f, pos.y + 0.5f) + fwd * Mathf.Max(0f, visionOriginForwardOffset);

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

        Vector2 origin = new Vector2(pos.x + 0.5f, pos.y + 0.5f) + fwd * Mathf.Max(0f, visionOriginForwardOffset);
        Vector2 to = new Vector2(gp.x + 0.5f, gp.y + 0.5f);
        Vector2 dir = to - origin;
        if (dir.sqrMagnitude < 1e-6f) return false;
        dir.Normalize();

        if (Vector2.Angle(fwd, dir) > fovAngle * 0.5f) return false;
        return board.HasLineOfSight(pos, gp);
    }

    // 追加: 視界内の泥棒を宝箱へ変える
    void RevealThievesInSight()
    {
        if (board == null || board.itemAt == null || board.itemAt.Count == 0) return;

        // 変換中に辞書を触らないよう、まず収集
        var toReveal = new List<Vector2Int>();
        foreach (var kv in board.itemAt)
        {
            if (kv.Value.sym != 'd') continue; // 泥棒のみ対象
            var p = kv.Key;
            if (CanSeeCell(p)) toReveal.Add(p);
        }

        for (int i = 0; i < toReveal.Count; i++)
        {
            board.TransformThiefToTreasureAt(toReveal[i]);
        }
    }

    // セル中心（BoardManager.CellCenter に統一）
    Vector3 WorldCenter(Vector2Int p, float y)
    {
        return board.CellCenter(p, y);
    }

    // 追加: グリッド「中心座標系」の連続値(Vector2)をワールド座標に変換
    // center は (i+0.5, j+0.5) をセル中心とする座標系。
    // → CellCenter に (center - 0.5) のローカル差分を加えてワールドへ。
    Vector3 WorldFromGridCenterCoords(Vector2 center, float y)
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
    bool IsCellVisibleGridAligned(Vector2Int gp, Facing curFacing, float halfDeg)
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

    void BuildSmoothVisionMesh()
    {
        // 互換: 旧呼び出しからはオフセット(0)で呼ぶ
        BuildSmoothVisionMeshWithOffset(Vector3.zero, Vector2.zero);
    }

    void BuildSmoothVisionMeshWithOffset(Vector3 offsetWorld, Vector2 offset2D)
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

        int rays = Mathf.Clamp(visionRayCount, 12, 256);
        float half = fovAngle * 0.5f;

        // 現在の向きで視界を計算
        Facing curFacing = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);
        float yaw = FacingToYaw(curFacing) + visionYawOffsetDeg;
        Vector2 fwd = GetForward2DFromYaw(yaw).normalized;

        // 扇形原点（セル中心 + サブセルオフセット + 前方オフセット）
        Vector3 origin = WorldCenter(pos, board.visionY) + offsetWorld + new Vector3(fwd.x, 0f, fwd.y) * Mathf.Max(0f, visionOriginForwardOffset);

        // ワークバッファ（再利用）
        _visionVerts ??= new List<Vector3>(rays + 2);
        _visionTris  ??= new List<int>(rays * 3);

        if (_visionVerts.Capacity < rays + 2) _visionVerts.Capacity = rays + 2;
        if (_visionTris.Capacity  < rays * 3) _visionTris.Capacity  = rays * 3;

        _visionVerts.Clear();
        _visionTris.Clear();

        _visionVerts.Add(origin);

        for (int i = 0; i <= rays; i++)
        {
            float t = (rays == 0) ? 0f : (i / (float)rays);
            float ang = (yaw - half) + (fovAngle * t);

            // グリッドCast結果（中心座標系の連続値）→ 丸めずにワールドへ変換
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

        // Vision のルートは常に上向き・ワールド固定
        root.transform.rotation = Quaternion.identity;
    }

    GameObject GetVisionRoot()
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

    void EnsureFacingArrow()
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

    Facing StepToFacing(Vector2Int st)
    {
        if (st.x > 0) return Facing.Right;
        if (st.x < 0) return Facing.Left;
        if (st.y > 0) return Facing.Up;
        return Facing.Down;
    }

    Vector2Int FacingToVec(Facing f)
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
    List<Vector3> _visionVerts = null;
    List<int> _visionTris = null;

    void UpdateFacingByWatchMode()
    {
        if (watchMode == WatchMode.OneDir) return;
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

        // 即スナップ（体感遅延を抑える）
        if (board != null && board.snapGuardFacingOnMove)
        {
            currentYaw = targetYaw;
            ApplyVisualYaw();
        }

        // 反転中は一定時間停止＋視界無効（理由: watch-rotate）
        BeginFlipPause("watch-rotate");
        // 見た目更新（2系統＋反転）
        ApplyVisualByFacing();
    }

    // 外部から切替したい場合に呼べるトグル
    public void SetVisionMode(VisionMode mode)
    {
        visionMode = mode;
        UpdateVisionOverlay();
    }

    void OnDestroy()
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
    }

    // 個別停止の診断用
    string lastFlipReason = "";
    bool flipHoldLogged = false;

    // 移動完了直後に1手だけ消化するためのラッパ
    void RunQueuedStepOnce()
    {
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (turn == null) return;
        if (turn.gameOver || turn.cleared) return;
        if (IsFlipping()) return;

        // すぐまた isMoving を立てる可能性があるが、そのためのキューは次回以降に任せる
        DoOneStepCore();
    }
}