using UnityEngine;

public partial class GuardController : MonoBehaviour
{
    /// <summary>
    /// パターンに従った巡回経路を構築する。
    /// </summary>
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
            loopDir = +1;
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
}
