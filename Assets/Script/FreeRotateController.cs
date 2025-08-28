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

    // 追加：押下時の基準角と相対角
    float freeStartAngleDeg = 0f;
    float freeDeltaDeg = 0f; // 押下→現在（CCWを+）

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

            // 押下時の基準角を記録
            if (!TryGetMouseWorld(center, out var startAngle)) return;
            freeStartAngleDeg = startAngle;
            freeDeltaDeg = 0f;

            board.BeginFreePreview(center, freeSize);
            freeCenter = center;
            freeNearestSteps = 0;
            freeStepOK = false;
            freeDragging = true;

            // 開始時点は判断未定なので赤で出しておく
            player?.ShowGhostExtern(true, center, freeSize, false);
            return;
        }

        // ドラッグ中：相対角Δ→最近傍ステップ→OK/NG表示
        if (freeDragging && Input.GetMouseButton(0))
        {
            if (!TryGetMouseWorld(freeCenter, out var curAngle)) return;

            // 押下→現在の相対角（CCWを+）
            freeDeltaDeg = Mathf.DeltaAngle(freeStartAngleDeg, curAngle);

            // 実プレビューを相対角で回す（Unityの+Y回転は見下ろしでCWのため符号を反転）
            board.UpdateFreePreviewAngle(-freeDeltaDeg);

            // 最寄りステップ（四捨五入して -2..2 に）
            freeNearestSteps = Mathf.RoundToInt(freeDeltaDeg / 90f);
            freeNearestSteps = Mathf.Clamp(
                freeNearestSteps,
                board.devAllow180Rotation ? -2 : -1,
                board.devAllow180Rotation ?  2 :  1
            );

            // 成立可否（注意: BoardManager は dir>0=CW, dir<0=CCW）
            var v = board.GetStepValidity(freeCenter, freeSize);
            freeStepOK = IsStepAllowed(v, freeNearestSteps, board.devAllow180Rotation);

            // GhostのOK/NG（緑/赤）
            player?.UpdateGhostOkExtern(freeStepOK);
            return;
        }

        // 左解放：スナップ確定 or キャンセル
        if (freeDragging && Input.GetMouseButtonUp(0))
        {
            // プレビュー解除（親戻し）→ 即時確定（アニメなし）
            board.RestoreFreePreview();

            if (freeStepOK && freeNearestSteps != 0)
            {
                CommitRotationInstant(freeCenter, freeSize, freeNearestSteps);
            }
            else
            {
                player?.FlashNgGhostExtern(freeCenter, freeSize, board.devNgGhostSeconds);
            }

            freeDragging = false;
            return;
        }
    }

    // steps>0 = CCW（dir=-1 側）, steps<0 = CW（dir=+1 側）
    bool IsStepAllowed(BoardManager.StepValidity v, int steps, bool allow180)
    {
        if (steps == +1) return v.ccw90;
        if (steps == -1) return v.cw90;
        if (!allow180) return false;
        if (steps == +2) return v.ccw180;
        if (steps == -2) return v.cw180;
        return false;
    }

    void CommitRotationInstant(Vector2Int center, int size, int steps)
    {
        int dirPerStep = (steps > 0) ? -1 : +1; // CCW(+)→-1, CW(-)→+1
        int count = Mathf.Abs(steps);

        player?.ClearGhost();

        for (int i = 0; i < count; i++)
        {
            board.RotateAreaInstant(center, size, dirPerStep);

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
            // +X基準で CCW を正に取る
            Vector2 v = new Vector2(hit.x - wc.x, hit.z - wc.z);
            if (v.sqrMagnitude < 0.0001f) return false;
            angleDeg = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            return true;
        }
        return false;
    }
}