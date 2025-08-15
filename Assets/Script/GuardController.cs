using System.Collections.Generic;
using UnityEngine;

public class GuardController : MonoBehaviour
{
    public enum PatrolMode
    {
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

        BuildPatrolPath(start);
        if (path.Count == 0 && patrolMode != PatrolMode.AutoEdgePingPong)
        {
            // パターン解釈に失敗したら自動往復へフォールバック
            patrolMode = PatrolMode.AutoEdgePingPong;
        }

        // 最初のターゲットとforwardを決定
        Vector2Int tgt = GetCurrentTargetOrFallback(start);
        forward = DirToStep(tgt - pos);
        UpdateVisionOverlay();
        if (useBoardDefaultViewRange) viewRange = board.defaultGuardViewRange;
    }

    // ====== パターン構築 ======
    void BuildPatrolPath(Vector2Int start)
    {
        path.Clear();
        if (patrolMode == PatrolMode.AutoEdgePingPong || string.IsNullOrWhiteSpace(pattern))
        {
            // 旧仕様：通路の端同士を自動検出して長い方を採用
            var hA = FindEdge(start, Vector2Int.left);
            var hB = FindEdge(start, Vector2Int.right);
            var vA = FindEdge(start, Vector2Int.down);
            var vB = FindEdge(start, Vector2Int.up);
            int lenH = (hB - hA).sqrMagnitude;
            int lenV = (vB - vA).sqrMagnitude;

            if (lenV > lenH) { path.Add(vA); path.Add(vB); }
            else { path.Add(hA); path.Add(hB); }

            // スタートが端点の場合は、スタートを含む片方を最初のターゲットにしやすい
            pathIndex = (path.Count > 0) ? 0 : 0;
            pingDir = +1;
            return;
        }

        // パターン字句解析（例: "R5", "U3", "L10", "D1" をカンマ/スペース区切りで並べる）
        var tokens = pattern.Split(new char[] { ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        Vector2Int curr = start;

        foreach (var tk in tokens)
        {
            if (tk.Length == 0) continue;
            char c = char.ToUpperInvariant(tk[0]);
            int n = 1;
            if (tk.Length > 1)
            {
                int.TryParse(tk.Substring(1), out n);
                if (n <= 0) n = 1;
            }

            Vector2Int delta = Vector2Int.zero;
            if (c == 'R') delta = new Vector2Int(+n, 0);
            else if (c == 'L') delta = new Vector2Int(-n, 0);
            else if (c == 'U') delta = new Vector2Int(0, +n);
            else if (c == 'D') delta = new Vector2Int(0, -n);
            else continue;

            Vector2Int next = patternIsRelative ? (curr + delta) : (start + delta);
            // 盤内にクランプ（壁は無視：動作時に対処）
            next = new Vector2Int(Mathf.Clamp(next.x, 0, board.Width - 1),
                                  Mathf.Clamp(next.y, 0, board.Height - 1));
            path.Add(next);
            curr = next;
        }

        // インデックス初期化
        pathIndex = (path.Count > 0) ? 0 : 0;
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
    public void DoTurn()
    {
        if (turn == null || turn.gameOver || turn.cleared) return;

        Vector2Int tgt = GetCurrentTargetOrFallback(pos);

        // ターゲットに1歩近づく
        Vector2Int step = DirToStep(tgt - pos);

        // ターゲットに既に到達していたら、次のターゲットを選んでから再計算
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
                // ★ブロック時の挙動
                moved = HandleBlocked(ref step);
            }
        }

        // 視界チェック
        if (board.player != null && CanSeePlayer()) turn.TriggerGameOver();

        // 可視化更新
        UpdateVisionOverlay();
    }

    // ブロック時の対処：PingPongは反転、Loopは次ウェイポイントへスキップして試行
    bool HandleBlocked(ref Vector2Int step)
    {
        if (patrolMode == PatrolMode.PingPong || patrolMode == PatrolMode.AutoEdgePingPong)
        {
            // 折り返して再試行
            pingDir *= -1;
            StepIndex(pingDir); // ひとつ戻る
            Vector2Int tgt = GetCurrentTargetOrFallback(pos);
            step = DirToStep(tgt - pos);

            if (step != Vector2Int.zero && board.IsWalkable(pos + step))
            {
                pos += step;
                transform.position = board.GridToWorldActor(pos);
                forward = step;
                return true;
            }
            return false; // どうしても動けない
        }
        else // Loop
        {
            // 次のウェイポイントへ順にスキップしながら、動ける1歩を探す
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
            return false; // どこへも進めない
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
