using UnityEngine;
using System.Collections;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class FreeRotateController : MonoBehaviour
{
    BoardManager board;
    PlayerController player;
    TurnManager turn;

    bool freeDragging = false;
    Vector2Int freeCenter;
    int freeSize = 3;
    int freeNearestSteps = 0; // -2..2（0は未確定）
    bool freeStepOK = false;

    float freeStartAngleDeg = 0f;
    float freeDeltaDeg = 0f; // 現在までの差（CCWが+）

#if ENABLE_INPUT_SYSTEM
    // 左スティック回転のコミット判定（無入力アイドルで確定）
    bool _padLeftWasActive = false;
    float _padLeftInactiveSince = 0f;
    const float _padCommitIdleSec = 0.12f; // この秒数以上ニュートラルなら確定
#endif

    void Update()
    {
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();
        if (player == null) player = UnityCompat.FindFirst<PlayerController>();
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (board == null || turn == null) return;

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
#if ENABLE_INPUT_SYSTEM
        _padLeftWasActive = false;
        _padLeftInactiveSince = 0f;
#endif
    }

    void HandleFreeRotate()
    {
        // キーマウ：右クリック/Esc/T でキャンセル
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.T))
        {
            if (freeDragging) CancelIfNeeded();
            return;
        }

        // マウス開始（左クリックで中心取得→ドラッグで回転）
        if (Input.GetMouseButtonDown(0))
        {
            if (!TryGetMouseGrid(out var center)) return;
            freeSize = player != null ? player.areaSize : 3;
            if (!IsCenterAllowed(center)) { FlashNg(center); return; }
            if (!HasAnyStep(center)) { FlashNg(center); return; }
            if (!TryGetMouseWorld(center, out var startAngle)) return;
            BeginPreview(center, startAngle);
            return;
        }

        // パッド入力
#if ENABLE_INPUT_SYSTEM
        var gp = Gamepad.current;
        if (gp != null)
        {
            // A/B でキャンセル
            if (freeDragging && (gp.buttonSouth.wasPressedThisFrame || gp.buttonEast.wasPressedThisFrame))
            {
                CancelIfNeeded();
                return;
            }

            // 選択時：左スティックで回転（プレビュー未開始なら開始トリガ）
            if (player != null && player.IsAiming)
            {
                Vector2 lv = gp.leftStick.ReadValue();
                float thr = player != null ? Mathf.Max(0.2f, player.stickDigitalThreshold * 0.6f) : 0.3f;
                float mag = lv.sqrMagnitude;

                if (mag >= thr * thr)
                {
                    if (!freeDragging)
                    {
                        // まだ開始していなければ開始
                        var center = player.AimCenter;
                        freeSize = player.areaSize;
                        if (!IsCenterAllowed(center) || !HasAnyStep(center)) { FlashNg(center); return; }
                        float startAngle = Mathf.Atan2(lv.y, lv.x) * Mathf.Rad2Deg;
                        BeginPreview(center, startAngle);
                        _padLeftWasActive = true;
                        _padLeftInactiveSince = 0f;
                    }
                    else
                    {
                        // 角度更新
                        float curAngle = Mathf.Atan2(lv.y, lv.x) * Mathf.Rad2Deg;
                        UpdateAngleFrom(curAngle);
                        _padLeftWasActive = true;
                        _padLeftInactiveSince = 0f;
                    }
                }
                else if (freeDragging)
                {
                    // ニュートラルに戻ったらアイドルタイマ開始→一定時間で確定
                    if (_padLeftWasActive)
                    {
                        if (_padLeftInactiveSince <= 0f) _padLeftInactiveSince = Time.time;
                        else if (Time.time - _padLeftInactiveSince >= _padCommitIdleSec)
                        {
                            EndPreviewAndCommit();
                            return;
                        }
                    }
                }
            }

            // R2 サイズ切替は PlayerController 側で処理（プレビュー中も可）
        }
#endif

        // マウス：ドラッグ回転
        if (freeDragging && Input.GetMouseButton(0))
        {
            if (!TryGetMouseWorld(freeCenter, out var curAngle)) return;
            UpdateAngleFrom(curAngle);
            return;
        }

        // マウス：ボタンを離したら確定/却下
        if (freeDragging && Input.GetMouseButtonUp(0))
        {
            EndPreviewAndCommit();
            return;
        }
    }

    void BeginPreview(Vector2Int center, float startAngle)
    {
        freeStartAngleDeg = startAngle;
        freeDeltaDeg = 0f;
        board.BeginFreePreview(center, freeSize);
        freeCenter = center;
        freeNearestSteps = 0;
        freeStepOK = false;
        freeDragging = true;
        player?.ShowGhostExtern(true, center, freeSize, false);
        var pivot = board.GetFreePreviewPivot();
        if (pivot != null) player?.AttachGhostTo(pivot, true);
    }

    void UpdateAngleFrom(float curAngle)
    {
        freeDeltaDeg = Mathf.DeltaAngle(freeStartAngleDeg, curAngle);
        freeNearestSteps = Mathf.RoundToInt(freeDeltaDeg / 90f);
        freeNearestSteps = Mathf.Clamp(
            freeNearestSteps,
            board != null && board.devAllow180Rotation ? -2 : -1,
            board != null && board.devAllow180Rotation ?  2 :  1
        );

        float targetSnapDeg = freeNearestSteps * 90f;
        bool snapped = Mathf.Abs(freeDeltaDeg - targetSnapDeg) <= Mathf.Max(0f, board.devSnapAngleDeg);
        float previewDeg = snapped ? targetSnapDeg : freeDeltaDeg;

        // +Y は右手系なのでCW が負回転
        board.UpdateFreePreviewAngle(-previewDeg);

        var v = board.GetStepValidity(freeCenter, freeSize);
        bool stepAllowed = IsStepAllowed(v, freeNearestSteps, board.devAllow180Rotation);
        bool lockedExceptCenter = board.AreaContainsLockedExceptCenter(freeCenter, freeSize);

        freeStepOK = snapped && stepAllowed && !lockedExceptCenter;
        player?.UpdateGhostOkExtern(snapped);
    }

    void EndPreviewAndCommit()
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
#if ENABLE_INPUT_SYSTEM
        _padLeftWasActive = false;
        _padLeftInactiveSince = 0f;
#endif
    }

    // steps>0 = CCW（dir=-1 回転）, steps<0 = CW（dir=+1 回転）
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
            angleDeg = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg; // +X基準CCW
            return true;
        }
        return false;
    }

    bool IsCenterAllowed(Vector2Int center)
    {
        freeSize = player != null ? player.areaSize : 3;
        if (board == null) return false;
        return board.IsCenterWithinLimit(center) && !board.AreaContainsLockedExceptCenter(center, freeSize);
    }
    bool HasAnyStep(Vector2Int center)
    {
        var steps = board.GetStepValidity(center, freeSize);
        return steps.Any(board.devAllow180Rotation);
    }

    // NGプレビューの簡易ヘルパー
    void FlashNg(Vector2Int center)
    {
        player?.FlashNgGhostExtern(center, freeSize, board != null ? board.devNgGhostSeconds : 0.5f);
    }
}