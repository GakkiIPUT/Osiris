using UnityEngine;
using System.Collections;

public class FreeRotateController : MonoBehaviour
{
    BoardManager board;
    PlayerController player;
    TurnManager turn;

    bool freeDragging = false;
    Vector2Int freeCenter;
    int freeSize = 3;
    int freeNearestSteps = 0; // -2..2（0は非成立）
    bool freeStepOK = false;

    float freeStartAngleDeg = 0f;
    float freeDeltaDeg = 0f; // 押下→現在（CCWを+）

    void Update()
    {
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();
        if (player == null) player = UnityCompat.FindFirst<PlayerController>();
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (board == null || turn == null) return;

        if (!board.devEnableFreeRotate) { CancelIfNeeded(); return; }
        if (!turn.IsPlayerTurn()) { CancelIfNeeded(); return; }

        HandleFreeRotate();
    }

    void CancelIfNeeded()
    {
        if (!freeDragging) return;
        player?.DetachGhost(true);
        board.RestoreFreePreview();
        player?.ClearGhost();
        freeDragging = false;
    }

    void HandleFreeRotate()
    {
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.T))
        {
            if (freeDragging)
            {
                player?.DetachGhost(true);
                board.RestoreFreePreview();
                player?.ClearGhost();
                freeDragging = false;
            }
            return;
        }

        // 左押下：開始（中心のみ例外）
        if (Input.GetMouseButtonDown(0))
        {
            if (!TryGetMouseGrid(out var center)) return;

            freeSize = player != null ? player.areaSize : 3;

            if (!board.IsCenterWithinLimit(center) || board.AreaContainsLockedExceptCenter(center, freeSize))
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

            if (!TryGetMouseWorld(center, out var startAngle)) return;
            freeStartAngleDeg = startAngle;
            freeDeltaDeg = 0f;

            board.BeginFreePreview(center, freeSize);
            freeCenter = center;
            freeNearestSteps = 0;
            freeStepOK = false;
            freeDragging = true;

            player?.ShowGhostExtern(true, center, freeSize, false);
            var pivot = board.GetFreePreviewPivot(); // Group優先
            if (pivot != null) player?.AttachGhostTo(pivot, true);

            return;
        }

        // ドラッグ中
        if (freeDragging && Input.GetMouseButton(0))
        {
            if (!TryGetMouseWorld(freeCenter, out var curAngle)) return;

            freeDeltaDeg = Mathf.DeltaAngle(freeStartAngleDeg, curAngle);

            freeNearestSteps = Mathf.RoundToInt(freeDeltaDeg / 90f);
            freeNearestSteps = Mathf.Clamp(
                freeNearestSteps,
                board.devAllow180Rotation ? -2 : -1,
                board.devAllow180Rotation ?  2 :  1
            );

            float targetSnapDeg = freeNearestSteps * 90f;
            bool snapped = Mathf.Abs(freeDeltaDeg - targetSnapDeg) <= Mathf.Max(0f, board.devSnapAngleDeg);
            float previewDeg = snapped ? targetSnapDeg : freeDeltaDeg;

            // +Y は見下ろしでCW → 符号反転
            board.UpdateFreePreviewAngle(-previewDeg);

            var v = board.GetStepValidity(freeCenter, freeSize);
            bool stepAllowed = IsStepAllowed(v, freeNearestSteps, board.devAllow180Rotation);

            // 中心のみ例外
            bool lockedExceptCenter = board.AreaContainsLockedExceptCenter(freeCenter, freeSize);

            // 確定可否は従来どおり（スナップ ＆ 成立 ＆ ロック例外OK）
            freeStepOK = snapped && stepAllowed && !lockedExceptCenter;

            // 表示色はスナップ基準（スナップ=緑、非スナップ=赤）
            player?.UpdateGhostOkExtern(snapped);
            return;
        }

        // 左解放
        if (freeDragging && Input.GetMouseButtonUp(0))
        {
            player?.DetachGhost(true);
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
        int dirPerStep = (steps > 0) ? -1 : +1;
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
            Vector2 v = new Vector2(hit.x - wc.x, hit.z - wc.z);
            if (v.sqrMagnitude < 0.0001f) return false;
            angleDeg = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg; // +X基準CCW正
            return true;
        }
        return false;
    }
}