using System.Collections.Generic;
using UnityEngine;

public class GuardController : MonoBehaviour
{
    public enum PatrolMode
    {
        Static,
        AutoEdgePingPong, // 既存：通路の端～端を自動検出して往復
        PingPong,         // パターンで定義した経路を往復（端で折り返し）
        Loop              // パターンで定義した経路を巡回（ぐるぐる）
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

    [Tooltip("例: R5 で右に5マス往復 / R3,U2,L3,D2 で四角形巡回など")]
    public string pattern = "";                 // 例: "R5" / "R3,U2,L3,D2" / "R4,U3"
    public bool patternIsRelative = true;       // Start からの相対指定（推奨）
    public bool debugDrawPath = false;
    public bool useBoardDefaultViewRange = true;
    public enum Facing { Up, Right, Down, Left }   
    public Facing startFacing = Facing.Right;

    // 経路情報
    readonly List<Vector2Int> path = new();     // 絶対座標のウェイポイント（startは含めない）
    int pathIndex = 0;                          // 現在のターゲットindex
    int pingDir = +1;                           // PingPong時の進行方向（+1/-1）

    // 可視化
    public bool showVision = true;
    GameObject visionRoot;

    // ====== 初期化 ======

    public void Init(BoardManager b, Vector2Int start)
    {
        board = b;
        pos = start;
        transform.position = b.GridToWorldActor(pos);
        turn = UnityCompat.FindFirst<TurnManager>();

        if (patrolMode == PatrolMode.Static)
        {
            //  Static は経路を作らず、向きだけ設定
            forward = FacingToVec(startFacing);   
        }
        else
        {
            BuildPatrolPath(start);               // 経路構築

            // Static 以外でパターン解釈失敗時のみ AutoEdge にフォールバック
            if (path.Count == 0 && patrolMode != PatrolMode.AutoEdgePingPong)
            {
                patrolMode = PatrolMode.AutoEdgePingPong;
            }

            // 最初のターゲットと forward を決定
            Vector2Int tgt = GetCurrentTargetOrFallback(start);
            forward = DirToStep(tgt - pos);
        }

        UpdateVisionOverlay();
        if (useBoardDefaultViewRange) viewRange = board.defaultGuardViewRange;
    }


    // ====== パターン構築 ======
    void BuildPatrolPath(Vector2Int start)
    {
        path.Clear();
        if (patrolMode == PatrolMode.Static) return;        // Static は経路を持たない
        // AutoEdge or パターン未指定 → 既存の自動往復
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

        // パターン解釈（R5, L5, U5, D5 / カンマ区切り）
        var tokens = pattern.Split(new char[] { ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        Vector2Int curr = start;

        foreach (var tk in tokens)
        {
            if (tk.Length == 0) continue;
            char c = char.ToUpperInvariant(tk[0]);
            int n = 1;
            if (tk.Length > 1) { int.TryParse(tk.Substring(1), out n); if (n <= 0) n = 1; }

            Vector2Int delta = Vector2Int.zero;
            if (c == 'R') delta = new Vector2Int(+n, 0);
            else if (c == 'L') delta = new Vector2Int(-n, 0);
            else if (c == 'U') delta = new Vector2Int(0, +n);
            else if (c == 'D') delta = new Vector2Int(0, -n);
            else continue;

            Vector2Int next = patternIsRelative ? (curr + delta) : (start + delta);
            next = new Vector2Int(Mathf.Clamp(next.x, 0, board.Width - 1),
                                  Mathf.Clamp(next.y, 0, board.Height - 1));
            path.Add(next);
            curr = next;
        }

        // ★ここがポイント：PingPong で waypoint が1つしか無い（=直線往復R5/L5/U5/D5等）
        //   → "start ↔ endpoint" の2端点で往復できるように start を追加
        if (patrolMode == PatrolMode.PingPong && path.Count == 1)
        {
            path.Insert(0, start); // 端点A=start, 端点B=path[1]
        }

        pathIndex = 0;
        pingDir = +1;
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

    // ====== 1ターン処理 ======
    //public void DoTurn()
    //{
    //    if (turn == null || turn.gameOver || turn.cleared) return;

    //    Vector2Int tgt = GetCurrentTargetOrFallback(pos);

    //    // ターゲットに1歩近づく
    //    Vector2Int step = DirToStep(tgt - pos);

    //    // ターゲットに既に到達していたら、次ターゲットを選び直し
    //    if (step == Vector2Int.zero)
    //    {
    //        AdvanceTarget();
    //        tgt = GetCurrentTargetOrFallback(pos);
    //        step = DirToStep(tgt - pos);
    //    }

    //    bool moved = false;

    //    if (step != Vector2Int.zero)
    //    {
    //        Vector2Int np = pos + step;
    //        if (board.IsWalkable(np))
    //        {
    //            // 通常前進
    //            pos = np;
    //            transform.position = board.GridToWorldActor(pos);
    //            forward = step;
    //            moved = true;
    //        }
    //        else
    //        {
    //            // ★ブロック時：まず即時バウンド（1マス後退＋向き反転）を試す
    //            if (TryImmediateBounce(step))
    //            {
    //                moved = true; // 成功（後退）
    //            }
    //            else
    //            {
    //                // それでも無理なら従来の反転/スキップ処理に委ねる
    //                moved = HandleBlocked(ref step);
    //            }
    //        }
    //    }

    //    // 視界チェック
    //    if (board.player != null && CanSeePlayer()) turn.TriggerGameOver();

    //    // 可視化更新
    //    UpdateVisionOverlay();
    //}

    /*
    /// 進行方向が塞がれているとき、1マスだけ後退して向きを反転する。
    /// 後退先も塞がれていれば何もしない（=袋小路なので停止）。
    */

    bool TryImmediateBounce(Vector2Int intendedStep)
    {
        if (intendedStep == Vector2Int.zero) return false;

        Vector2Int back = -intendedStep;
        Vector2Int np = pos + back;

        // 後退できなければ失敗（1マスの袋小路）
        if (!board.IsWalkable(np)) return false;

        // 後退して向き反転
        pos = np;
        transform.position = board.GridToWorldActor(pos);
        forward = back;

        // ★PingPong系は進行向きも反転し、ターゲットを対向端点に切替
        if (patrolMode == PatrolMode.PingPong || patrolMode == PatrolMode.AutoEdgePingPong)
        {
            pingDir *= -1;
            AdvanceTarget(); // ← これが重要（pathIndex手動操作はやめる）
        }
        return true;
    }

    // ブロック時の対処：PingPongは反転、Loopは次ウェイポイントへスキップして試行
    /*
    /// バウンドでも進めない場合の最終手段。
    /// PingPong系：反転して1歩を試す。Loop：次WPへスキップしながら動ける方向を探す。
    */

    public void StepAI()
    {
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (turn == null) return;
        if (turn.gameOver || turn.cleared) return;

        // 回転アニメ中は動かさない（破綻回避したい場合）
        if (board != null && board.IsAnimating) return;

        // Static は移動しない。視界チェック＆可視化のみ。
        if (patrolMode == PatrolMode.Static)                  
        {                                                     
            if (board.player != null && CanSeePlayer())       
                turn.TriggerGameOver();                       
            UpdateVisionOverlay();                            
            return;                                           
        }

        Vector2Int tgt = GetCurrentTargetOrFallback(pos);

        // ターゲットに1歩近づく
        Vector2Int step = DirToStep(tgt - pos);

        // 既に到達していたら、次ターゲットへ切替
        if (step == Vector2Int.zero)
        {
            AdvanceTarget();
            tgt = GetCurrentTargetOrFallback(pos);
            step = DirToStep(tgt - pos);
        }

        bool moved = false;

        if (step != Vector2Int.zero)
        {
            Vector2Int np = pos + step;
            if (board.IsWalkable(np))
            {
                pos = np;
                transform.position = board.GridToWorldActor(pos);
                forward = step;
                moved = true;
            }
            else
            {
                // ブロック時の処理（折り返し or ループで次WPへ）
                moved = HandleBlocked(ref step);
            }
        }

        // 視界チェック（見えたら即ゲームオーバー）
        if (board.player != null && CanSeePlayer())
        {
            turn.TriggerGameOver();
        }

        // 可視化更新
        UpdateVisionOverlay();
    }

    // 互換: 既存呼び出しが残っていても動くように
    public void DoTurn() => StepAI();


    bool HandleBlocked(ref Vector2Int step)
    {
        // PingPong（AutoEdge含む）：反転して試す
        if (patrolMode == PatrolMode.PingPong || patrolMode == PatrolMode.AutoEdgePingPong)
        {
            pingDir *= -1;            // 進行方向反転
            StepIndex(pingDir);       // パス端点管理（必要なら1つ戻る/進む）

            Vector2Int tgt = GetCurrentTargetOrFallback(pos);
            step = DirToStep(tgt - pos);

            if (step != Vector2Int.zero && board.IsWalkable(pos + step))
            {
                pos += step;
                transform.position = board.GridToWorldActor(pos);
                forward = step;
                return true;
            }
            return false; // どうしても動けない（1マス袋小路など）
        }
        else // Loop：次のウェイポイントへ順にスキップして動ける1歩を探す
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

    // ====== ターゲット管理 ======
    Vector2Int GetCurrentTargetOrFallback(Vector2Int fallback)
    {
        if (path.Count == 0)
        {
            // AutoEdgeがパターンなしにフォールバックしたケース
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

    void AdvanceTarget()
    {
        if (path.Count == 0) return;

        if (patrolMode == PatrolMode.Loop)
        {
            StepIndex(+1);
        }
        else // PingPong
        {
            if ((pathIndex == path.Count - 1 && pingDir > 0) ||
                (pathIndex == 0 && pingDir < 0))
            {
                pingDir *= -1; // 端で反転
            }
            StepIndex(pingDir);
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

    // ====== 視界 ======
    bool CanSeePlayer()
    {
        Vector2Int p = board.player.pos;

        if ((p - pos).sqrMagnitude > viewRange * viewRange) return false;

        Vector2 from = new Vector2(pos.x, pos.y);
        Vector2 to = new Vector2(p.x, p.y);
        Vector2 dir = (to - from).sqrMagnitude > 0 ? (to - from).normalized : Vector2.zero;
        Vector2 fwd = new Vector2(forward.x, forward.y);
        float ang = (dir == Vector2.zero) ? 0f : Vector2.Angle(fwd, dir);
        if (ang > fovAngle * 0.5f) return false;

        return board.HasLineOfSight(pos, p);
    }

    void ClearVision()
    {
        if (visionRoot == null) return;
        for (int i = visionRoot.transform.childCount - 1; i >= 0; --i)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(visionRoot.transform.GetChild(i).gameObject);
            else
#endif
                Destroy(visionRoot.transform.GetChild(i).gameObject);
        }
    }
    GameObject GetVisionRoot()
    {
        if (visionRoot == null)
        {
            visionRoot = new GameObject($"{name}_Vision");
            visionRoot.transform.SetParent(transform, false);
        }
        return visionRoot;
    }

    public void UpdateVisionOverlay()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return;
#endif
        if (!showVision) { ClearVision(); return; }

        var root = GetVisionRoot();
        ClearVision();

        int r = viewRange;
        float halfFov = fovAngle * 0.5f;
        var mat = (board.ghostNgMat != null) ? board.ghostNgMat
                 : (board.guardVisionMat != null ? board.guardVisionMat : board.ghostOkMat);

        for (int y = pos.y - r; y <= pos.y + r; y++)
        {
            for (int x = pos.x - r; x <= pos.x + r; x++)
            {
                var p = new Vector2Int(x, y);
                if (!board.InBounds(p)) continue;
                if ((p - pos).sqrMagnitude > r * r) continue;

                Vector2 from = new Vector2(pos.x, pos.y);
                Vector2 to = new Vector2(p.x, p.y);
                Vector2 dir = (to - from).sqrMagnitude > 0 ? (to - from).normalized : Vector2.zero;
                Vector2 fwd = new Vector2(forward.x, forward.y);
                float ang = (dir == Vector2.zero) ? 0f : Vector2.Angle(fwd, dir);
                if (ang > halfFov) continue;

                if (!board.HasLineOfSight(pos, p)) continue;

                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = $"Vision_{x}_{y}";
                q.transform.SetParent(root.transform, false);
                q.transform.rotation = Quaternion.Euler(90, 0, 0);
                q.transform.localScale = new Vector3(1f, 1f, 1f);
                q.transform.position = board.CellCenter(p, board.visionY);
                var mr = q.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                if (mat != null) mr.material = mat;
                Destroy(q.GetComponent<MeshCollider>());
            }
        }
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

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!debugDrawPath || path == null) return;
        Gizmos.color = Color.yellow;
        Vector3 last = board != null ? board.GridToWorld(pos) + new Vector3(0.5f, 0.02f, 0.5f) : transform.position;
        foreach (var wp in path)
        {
            Vector3 w = (board != null ? board.GridToWorld(wp) : new Vector3(wp.x, 0, wp.y)) + new Vector3(0.5f, 0.02f, 0.5f);
            Gizmos.DrawLine(last, w);
            Gizmos.DrawSphere(w, 0.05f);
            last = w;
        }
    }
#endif
}
