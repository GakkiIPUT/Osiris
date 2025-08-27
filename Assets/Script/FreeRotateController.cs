using UnityEngine;
using System.Collections;

public class FreeRotateController : MonoBehaviour
{
    BoardManager board;
    PlayerController player;
    TurnManager turn;

    // 自由回転（ドラッグ）用の一時状態
    bool freeDragging = false;
    Vector2Int freeCenter;
    int freeSize = 3;
    int freeNearestSteps = 0; // -2..2（0は非成立）
    bool freeStepOK = false;

    void Update()
    {
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();
        if (player == null) player = UnityCompat.FindFirst<PlayerController>();
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (board == null || turn == null) return;

        // 自由回転がOFF、またはプレイヤーターン外は処理しない
        if (!board.devEnableFreeRotate) { CancelIfNeeded(); return; }
        if (!turn.IsPlayerTurn()) { CancelIfNeeded(); return; }

        HandleFreeRotate();
    }

    void CancelIfNeeded()
    {
        if (!freeDragging) return;
        board.RestoreFreePreview();
        player?.ClearGhost();
        freeDragging = false;
    }

    void HandleFreeRotate()
    {
        // キャンセル（いつでも）
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.T))
        {
            if (freeDragging)
            {
                board.RestoreFreePreview();
                player?.ClearGhost();
                freeDragging = false;
            }
            return;
        }

        // 左押下：開始
        if (Input.GetMouseButtonDown(0))
        {
            if (!TryGetMouseGrid(out var center)) return;

            freeSize = player != null ? player.areaSize : 3;

            // 中心距離/ロック/ステップ成立性を事前チェック
            if (!board.IsCenterWithinLimit(center) || board.AreaContainsLocked(center, freeSize))
            {
                player?.FlashNgGhostExtern(center, freeSize, board.devNgGhostSeconds);
                return;
            }
            var steps = board.GetStepValidity(center, freeSize);
            if (!steps.Any(board.devAllow180Rotation))
            {
                player?.FlashNgGhostExtern(center, freeSize, board.devNgGhostSeconds);
                return;
            }

            board.BeginFreePreview(center, freeSize);
            freeCenter = center;
            freeNearestSteps = 0;
            freeStepOK = false;
            freeDragging = true;

            // 開始時点は判断未定なので赤で出しておく
            player?.ShowGhostExtern(true, center, freeSize, false);
            return;
        }

        // ドラッグ中：角度→最近傍ステップ→OK/NG表示
        if (freeDragging && Input.GetMouseButton(0))
        {
            if (!TryGetMouseWorld(freeCenter, out var angleDeg)) return;

            // 実プレビューを連続角で回す
            board.UpdateFreePreviewAngle(angleDeg);

            // 最寄りステップ（+はCW、-はCCW）
            freeNearestSteps = NearestStepsFromAngle(angleDeg, board.devAllow180Rotation);

            // 成立可否
            var v = board.GetStepValidity(freeCenter, freeSize);
            freeStepOK = IsStepAllowed(v, freeNearestSteps, board.devAllow180Rotation);

            // GhostのOK/NG（緑/赤）
            player?.UpdateGhostOkExtern(freeStepOK);
            return;
        }

        // 左解放：スナップ確定 or キャンセル
        if (freeDragging && Input.GetMouseButtonUp(0))
        {
            board.RestoreFreePreview();

            if (freeStepOK && freeNearestSteps != 0)
            {
                StartCoroutine(CommitRotation(freeCenter, freeSize, freeNearestSteps));
            }
            else
            {
                player?.FlashNgGhostExtern(freeCenter, freeSize, board.devNgGhostSeconds);
            }

            freeDragging = false;
            return;
        }
    }

    // 角度から最近傍のステップ数（-2..2、0は非成立）を返す。+はCW、-はCCW。
    int NearestStepsFromAngle(float angleDeg, bool allow180)
    {
        // +X基準CCW正 → CW正にしたいので符号反転。90度単位へ丸め。
        int steps = Mathf.RoundToInt(-Mathf.DeltaAngle(0f, angleDeg) / 90f);
        steps = Mathf.Clamp(steps, allow180 ? -2 : -1, allow180 ? 2 : 1);
        if (!allow180 && steps == 0) return 0;
        return steps;
    }

    bool IsStepAllowed(BoardManager.StepValidity v, int steps, bool allow180)
    {
        if (steps == +1) return v.cw90;
        if (steps == -1) return v.ccw90;
        if (!allow180) return false;
        if (steps == +2) return v.cw180;
        if (steps == -2) return v.ccw180;
        return false;
    }

    IEnumerator CommitRotation(Vector2Int center, int size, int steps)
    {
        int dir = steps > 0 ? +1 : -1;
        int count = Mathf.Abs(steps);

        player?.ClearGhost();

        for (int i = 0; i < count; i++)
        {
            bool finished = false;
            board.RotateArea(center, size, dir, () => { finished = true; });
            while (!finished) yield return null;

            if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
            turn?.RegisterRotation();
        }

        turn?.EndPlayerTurn();
    }

    bool TryGetMouseGrid(out Vector2Int grid)
    {
        grid = default;
        var cam = Camera.main;
        if (cam == null) return false;

        Ray r = cam.ScreenPointToRay(Input.mousePosition);
        if (new Plane(Vector3.up, Vector3.zero).Raycast(r, out float enter))
        {
            Vector3 hit = r.GetPoint(enter);
            grid = board.WorldToGrid(hit);
            return true;
        }
        return false;
    }

    bool TryGetMouseWorld(Vector2Int center, out float angleDeg)
    {
        angleDeg = 0f;
        var cam = Camera.main;
        if (cam == null) return false;

        Ray r = cam.ScreenPointToRay(Input.mousePosition);
        if (new Plane(Vector3.up, Vector3.zero).Raycast(r, out float enter))
        {
            Vector3 hit = r.GetPoint(enter);
            Vector3 wc = board.GridToWorld(center);
            Vector2 v = new Vector2(hit.x - wc.x, hit.z - wc.z);
            if (v.sqrMagnitude < 0.0001f) return false;
            angleDeg = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            return true;
        }
        return false;
    }
}