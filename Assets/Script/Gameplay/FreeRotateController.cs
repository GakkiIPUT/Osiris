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
    float freeDeltaDeg = 0f; // 現在までの角度（CCWが+）

    // 安定化用（遊び・ヒステリシス・平滑化）
    [Header("Free Rotate Stabilizer")]
    [Tooltip("回転中心近傍の無反応半径（セル=1.0）。この半径未満では角度更新しない")]
    [Range(0.0f, 0.5f)] public float mouseDeadZoneRadius = 0.18f;
    // スナップは board.devSnapAngleDeg（IN）と board.devCommitAngleDeg（OUTの追加幅）を使用
    // 平滑化は board.devStickiness を係数として使う（0=弱,1=強）

    // 内部状態（スナップのラッチ）
    bool _isSnapped = false;
    int _latchedSteps = 0;             // -2..2
    float _previewDegSmoothed = 0f;    // 表示用角度（平滑化後）

#if ENABLE_INPUT_SYSTEM
    // スティック回転のコミット待ち（既存）
    bool _padLeftWasActive = false;
    float _padLeftInactiveSince = 0f;
    const float _padCommitIdleSec = 0.12f;
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
        _isSnapped = false;
        _latchedSteps = 0;
#if ENABLE_INPUT_SYSTEM
        _padLeftWasActive = false;
        _padLeftInactiveSince = 0f;
#endif
    }

    void HandleFreeRotate()
    {
        // キーマウス右クリック/Esc/T でキャンセル
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
            if (!TryGetMouseWorld(center, out var startAngle)) return; // デッドゾーン内は開始しない
            BeginPreview(center, startAngle);
            return;
        }

#if ENABLE_INPUT_SYSTEM
        // パッド入力（既存） … 略（元コード維持）
        var gp = Gamepad.current;
        if (gp != null)
        {
            if (freeDragging && (gp.buttonSouth.wasPressedThisFrame || gp.buttonEast.wasPressedThisFrame))
            {
                CancelIfNeeded();
                return;
            }
            if (player != null && player.IsAiming)
            {
                Vector2 lv = gp.leftStick.ReadValue();
                float thr = player != null ? Mathf.Max(0.2f, player.stickDigitalThreshold * 0.6f) : 0.3f;
                float mag = lv.sqrMagnitude;

                if (mag >= thr * thr)
                {
                    if (!freeDragging)
                    {
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
                        float curAngle = Mathf.Atan2(lv.y, lv.x) * Mathf.Rad2Deg;
                        UpdateAngleFrom(curAngle);
                        _padLeftWasActive = true;
                        _padLeftInactiveSince = 0f;
                    }
                }
                else if (freeDragging)
                {
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
        }
#endif

        // マウス：ドラッグ中は角度更新（デッドゾーン内はTryGetMouseWorld=falseで更新しない）
        if (freeDragging && Input.GetMouseButton(0))
        {
            if (!TryGetMouseWorld(freeCenter, out var curAngle)) return;
            UpdateAngleFrom(curAngle);
            return;
        }

        // マウス：ボタンを離したら確定/取消
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

        // 安定化状態リセット
        _isSnapped = false;
        _latchedSteps = 0;
        _previewDegSmoothed = 0f;

        player?.ShowGhostExtern(true, center, freeSize, false);
        var pivot = board.GetFreePreviewPivot();
        if (pivot != null) player?.AttachGhostTo(pivot, true);
    }

    void UpdateAngleFrom(float curAngle)
    {
        // 角度差（-180..180）
        freeDeltaDeg = Mathf.DeltaAngle(freeStartAngleDeg, curAngle);

        // 許容ステップ範囲
        int minStep = (board != null && board.devAllow180Rotation) ? -2 : -1;
        int maxStep = (board != null && board.devAllow180Rotation) ?  2 :  1;

        // 候補ステップ
        int candSteps = Mathf.Clamp(Mathf.RoundToInt(freeDeltaDeg / 90f), minStep, maxStep);

        // スナップのしきい値（IN/OUT ヒステリシス）
        float snapInDeg = Mathf.Max(1f, board != null ? board.devSnapAngleDeg : 15f);
        float snapOutDeg = snapInDeg + Mathf.Max(0f, board != null ? board.devCommitAngleDeg : 10f);

        // スナップ・ラッチ制御（シュミットトリガ）
        if (_isSnapped)
        {
            // いまのラッチ目標からどれだけズレたか
            float err = Mathf.DeltaAngle(freeDeltaDeg, _latchedSteps * 90f);
            if (Mathf.Abs(err) > snapOutDeg)
            {
                // ラッチ解除（再びフリーへ）
                _isSnapped = false;
            }
        }
        if (!_isSnapped)
        {
            float errToCandidate = Mathf.DeltaAngle(freeDeltaDeg, candSteps * 90f);
            if (Mathf.Abs(errToCandidate) <= snapInDeg)
            {
                _isSnapped = true;
                _latchedSteps = candSteps;
            }
        }

        // プレビュー角度（スナップ中は目標に固定、非スナップ時はフリー）
        float previewWantedDeg = _isSnapped ? (_latchedSteps * 90f) : freeDeltaDeg;

        // 平滑化（devStickinessで係数を調整：0=弱~1=強）
        float alpha = Mathf.Lerp(0.18f, 0.45f, board != null ? Mathf.Clamp01(board.devStickiness) : 0.5f);
        _previewDegSmoothed = Mathf.LerpAngle(_previewDegSmoothed, previewWantedDeg, alpha);

        // +Yは右手系なのでCWが正回転
        board.UpdateFreePreviewAngle(-_previewDegSmoothed);

        // 可否チェック（スナップしているステップのみ確定候補）
        var v = board.GetStepValidity(freeCenter, freeSize);
        bool lockedExceptCenter = board.AreaContainsLockedExceptCenter(freeCenter, freeSize);
        int checkSteps = _isSnapped ? _latchedSteps : candSteps;
        bool stepAllowed = IsStepAllowed(v, checkSteps, board.devAllow180Rotation);

        freeNearestSteps = checkSteps;
        freeStepOK = _isSnapped && stepAllowed && !lockedExceptCenter;

        // ゴースト色（緑/赤）は「スナップ中か」で示す
        player?.UpdateGhostOkExtern(_isSnapped);
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
        _isSnapped = false;
        _latchedSteps = 0;
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

        int applied = 0;
        for (int i = 0; i < count; i++)
        {
            if (!board.RotateAreaInstantIfPossible(center, size, dirPerStep))
                break;

            if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
            turn?.RegisterRotation();
            applied++;
        }

        if (applied > 0)
        {
            turn?.EndPlayerTurn();
        }
        // 0回適用なら何もしない（NGフラッシュは上位で済ませている）
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

    // デッドゾーン（中心近傍）では angleDeg を返さず false にする
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
            float deadSqr = mouseDeadZoneRadius * mouseDeadZoneRadius; // セル基準
            if (v.sqrMagnitude < deadSqr) return false;                // 中心近傍は無視（遊び）
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

    void FlashNg(Vector2Int center)
    {
        player?.FlashNgGhostExtern(center, freeSize, board != null ? board.devNgGhostSeconds : 0.5f);
    }
}