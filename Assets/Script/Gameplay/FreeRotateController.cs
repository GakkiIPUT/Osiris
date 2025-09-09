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
    float freeDeltaDeg = 0f; // 現在までの角度差（CCWが+）

    [Header("Free Rotate Stabilizer")]
    [Range(0.0f, 0.5f)] public float mouseDeadZoneRadius = 0.18f;

#if ENABLE_INPUT_SYSTEM
    [Header("Free Rotate / Pad Tuning")]
    [Tooltip("フリー回転時 左スティック左右を反転")]
    public bool padInvertHorizontal = false;
    [Tooltip("開始/停止用の外側デッドゾーン半径 (0=無し, 推奨0.2~0.3)")]
    [Range(0f, 0.9f)] public float padDeadZone = 0.25f;
    [Tooltip("1Dモードで角度ゼロへ潰さないための内側係数 (外側×係数)。小さいほど保持しやすい")]
    [Range(0f, 1f)] public float pad1DInnerDeadZoneFactor = 0.4f;
    [Tooltip("±90°判定に必要な最小角度（これ未満で離したら 0 ステップ）")]
    [Range(5f, 89f)] public float padStepMinDegrees = 30f;
    [Tooltip("離した瞬間スナップ成立で即確定")]
    public bool padCommitImmediateOnRelease = true;
    [Tooltip("即確定失敗時/Immediate OFF 時の遅延確定秒数")]
    [Range(0.02f, 0.5f)] public float padCommitIdleSeconds = 0.12f;
    [Tooltip("スナップしていない離しはキャンセルする")]
    public bool padCancelIfNotSnappedOnRelease = true;
    [Tooltip("離し時、スナップしてなくてもこの角度以内なら自動吸着して確定")]
    public bool padAutoSnapOnRelease = true;
    [Tooltip("自動吸着許容角度 (例: 50°なら ±90°へ 40~90°帯で吸着)")]
    [Range(10f, 89f)] public float padAutoSnapDeg = 50f;
    [Tooltip("離し後この ms は最後にスナップしていたステップを保持（ノイズ救済）")]
    [Range(0f, 300f)] public int padGraceSnapMillis = 120;
#endif

    bool _isSnapped = false;
    int _latchedSteps = 0;
    float _previewDegSmoothed = 0f;

#if ENABLE_INPUT_SYSTEM
    bool _padLeftWasActive = false;
    float _padLeftInactiveSince = 0f;
    bool _usingPad1D = false; // ±90°制限時のみ true
    float _lastInputAngleDeg = 0f;
    bool _hadSnapThisFrame = false;
    int _lastSnappedSteps = 0;
    float _lastSnapTime = 0f;
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
        _usingPad1D = false;
        _lastSnappedSteps = 0;
#endif
    }

    void HandleFreeRotate()
    {
        // キャンセル
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.T))
        {
            if (freeDragging) CancelIfNeeded();
            return;
        }

        // マウス開始
        if (Input.GetMouseButtonDown(0))
        {
            if (!TryGetMouseGrid(out var center)) return;
            freeSize = player != null ? player.areaSize : 3;
            if (!IsCenterAllowed(center)) { FlashNg(center); return; }
            if (!HasAnyStep(center)) { FlashNg(center); return; }

            float startAngle;
            bool hasAngle = TryGetMouseWorld(center, out startAngle);
            BeginPreview(center, hasAngle ? startAngle : 0f);
            if (!hasAngle) board.UpdateFreePreviewAngle(0f);
            return;
        }

#if ENABLE_INPUT_SYSTEM
        var gp = Gamepad.current;
        if (gp != null)
        {
            // B / X などで中断
            if (freeDragging && (gp.buttonSouth.wasPressedThisFrame || gp.buttonEast.wasPressedThisFrame))
            {
                CancelIfNeeded();
                return;
            }

            // Aiming 中のみ開始/角度更新を許可（離し後確定は Aiming 外でも行うため後段で処理）
            if (player != null && player.IsAiming)
            {
                Vector2 lv = gp.leftStick.ReadValue();

                float baseThr = player != null ? Mathf.Max(0.2f, player.stickDigitalThreshold * 0.6f) : 0.3f;
                float outerThr = Mathf.Max(Mathf.Clamp01(padDeadZone), baseThr);        // 開始/停止
                float inner1D = outerThr * Mathf.Clamp01(pad1DInnerDeadZoneFactor);     // 角度保持用(1D)

                float magSqr = lv.sqrMagnitude;
                float outerThrSqr = outerThr * outerThr;

                if (magSqr >= outerThrSqr)
                {
                    if (!freeDragging)
                    {
                        var center = player.AimCenter;
                        freeSize = player.areaSize;
                        if (!IsCenterAllowed(center) || !HasAnyStep(center)) { FlashNg(center); return; }

                        bool allow180 = board != null && board.devAllow180Rotation;
                        float startAngle = allow180 ? Mathf.Atan2(lv.y, lv.x) * Mathf.Rad2Deg : 0f;

                        BeginPreview(center, startAngle);
                        _padLeftWasActive = true;
                        _padLeftInactiveSince = 0f;
                        _usingPad1D = !allow180;
                    }
                    else
                    {
                        if (_usingPad1D)
                        {
                            float x = Mathf.Clamp(lv.x, -1f, 1f);
                            if (padInvertHorizontal) x = -x;

                            // 内側デッドゾーンで 0 クリップ（小さいので角度保持しやすい）
                            if (Mathf.Abs(x) < inner1D) x = 0f;

                            float curAngle = x * 90f;
                            _lastInputAngleDeg = curAngle;
                            UpdateAngleFrom(curAngle);
                        }
                        else
                        {
                            float curAngle = Mathf.Atan2(lv.y, lv.x) * Mathf.Rad2Deg;
                            if (padInvertHorizontal) curAngle = -curAngle;
                            _lastInputAngleDeg = curAngle;
                            UpdateAngleFrom(curAngle);
                        }
                        _padLeftWasActive = true;
                        _padLeftInactiveSince = 0f;
                    }
                }
            }

            // ここから「離し検出と確定」処理 (IsAiming でなくても動く) ★
            if (freeDragging && _padLeftWasActive)
            {
                Vector2 lv2 = gp.leftStick.ReadValue();

                float baseThr2 = player != null ? Mathf.Max(0.2f, player.stickDigitalThreshold * 0.6f) : 0.3f;
                float outerThr2 = Mathf.Max(Mathf.Clamp01(padDeadZone), baseThr2);
                bool belowOuter = lv2.sqrMagnitude < outerThr2 * outerThr2;

                if (belowOuter)
                {
                    // まだ開始タイマー未セット
                    if (_padLeftInactiveSince <= 0f)
                        _padLeftInactiveSince = Time.time;

                    // スナップ保持グレース (最後にスナップしたステップを一時保持)
                    bool withinGrace =
                        (Time.time - _lastSnapTime) * 1000f <= padGraceSnapMillis &&
                        _lastSnappedSteps != 0;

                    int candidateSteps = freeNearestSteps;
                    bool candidateOk = freeStepOK;

                    if (!_isSnapped && withinGrace)
                    {
                        candidateSteps = _lastSnappedSteps;
                        candidateOk = true; // 直前にOKだったとみなす
                    }
                    else if (!_isSnapped && padAutoSnapOnRelease && freeNearestSteps == 0)
                    {
                        // 自動吸着: 現在角度が +/−90 へ近いか判定
                        float ad = Mathf.Abs(freeDeltaDeg);
                        if (ad >= padStepMinDegrees && (90f - ad) <= padAutoSnapDeg)
                        {
                            candidateSteps = (freeDeltaDeg > 0f) ? +1 : -1;
                            // 有効性再チェック
                            var v = board.GetStepValidity(freeCenter, freeSize);
                            candidateOk = IsStepAllowed(v, candidateSteps, board.devAllow180Rotation) &&
                                          !board.AreaContainsLockedExceptCenter(freeCenter, freeSize);
                        }
                    }

                    // 即確定
                    if (padCommitImmediateOnRelease)
                    {
                        if (candidateOk && candidateSteps != 0)
                        {
                            // 強制的に nearest と状態を合わせる
                            freeNearestSteps = candidateSteps;
                            freeStepOK = true;
                            EndPreviewAndCommit();
                            return;
                        }
                        else if (!candidateOk && padCancelIfNotSnappedOnRelease)
                        {
                            CancelIfNeeded();
                            return;
                        }
                    }

                    // 遅延確定 (Immediate OFF か / 即確定条件未達)
                    if (Time.time - _padLeftInactiveSince >= padCommitIdleSeconds)
                    {
                        if (candidateOk && candidateSteps != 0)
                        {
                            freeNearestSteps = candidateSteps;
                            freeStepOK = true;
                            EndPreviewAndCommit();
                        }
                        else
                        {
                            if (padCancelIfNotSnappedOnRelease)
                                CancelIfNeeded();
                            else
                                EndPreviewAndCommit(); // スナップなし確定を許すならここを変更
                        }
                        return;
                    }
                }
                else
                {
                    // 再び動かしたので待機解除
                    _padLeftInactiveSince = 0f;
                }
            }
        }
#endif

        // マウスドラッグ
        if (freeDragging && Input.GetMouseButton(0))
        {
            if (!TryGetMouseWorld(freeCenter, out var curAngle)) return;
            UpdateAngleFrom(curAngle);
            return;
        }

        // マウスアップ
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

        _isSnapped = false;
        _latchedSteps = 0;
        _previewDegSmoothed = 0f;
#if ENABLE_INPUT_SYSTEM
        _lastSnappedSteps = 0;
        _lastSnapTime = 0f;
#endif
        player?.ShowGhostExtern(true, center, freeSize, false);
        var pivot = board.GetFreePreviewPivot();
        if (pivot != null) player?.AttachGhostTo(pivot, true);
    }

    void UpdateAngleFrom(float curAngle)
    {
        freeDeltaDeg = Mathf.DeltaAngle(freeStartAngleDeg, curAngle);

#if ENABLE_INPUT_SYSTEM
        if (_usingPad1D && !(board != null && board.devAllow180Rotation))
        {
            if (freeDeltaDeg > 90f) freeDeltaDeg = 90f;
            else if (freeDeltaDeg < -90f) freeDeltaDeg = -90f;
        }
#endif

        int minStep = (board != null && board.devAllow180Rotation) ? -2 : -1;
        int maxStep = (board != null && board.devAllow180Rotation) ?  2 :  1;

        int candSteps;
        if (board != null && !board.devAllow180Rotation)
        {
            // ±90°のみ：閾値式
            float ad = Mathf.Abs(freeDeltaDeg);
            candSteps = (ad >= padStepMinDegrees) ? (freeDeltaDeg > 0f ? +1 : -1) : 0;
        }
        else
        {
            // 180許可：従来 90°刻み丸め（2ステップまで）
            candSteps = Mathf.Clamp(Mathf.RoundToInt(freeDeltaDeg / 90f), minStep, maxStep);
        }

        float snapInDeg = Mathf.Max(1f, board != null ? board.devSnapAngleDeg : 15f);
        float snapOutDeg = snapInDeg + Mathf.Max(0f, board != null ? board.devCommitAngleDeg : 10f);

        if (_isSnapped)
        {
            float err = Mathf.DeltaAngle(freeDeltaDeg, _latchedSteps * 90f);
            if (Mathf.Abs(err) > snapOutDeg) _isSnapped = false;
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

        float previewWantedDeg = _isSnapped ? (_latchedSteps * 90f) : freeDeltaDeg;
        float alpha = Mathf.Lerp(0.18f, 0.45f, board != null ? Mathf.Clamp01(board.devStickiness) : 0.5f);
        _previewDegSmoothed = Mathf.LerpAngle(_previewDegSmoothed, previewWantedDeg, alpha);

        board.UpdateFreePreviewAngle(-_previewDegSmoothed);

        var v = board.GetStepValidity(freeCenter, freeSize);
        bool lockedExceptCenter = board.AreaContainsLockedExceptCenter(freeCenter, freeSize);
        int checkSteps = _isSnapped ? _latchedSteps : candSteps;
        bool stepAllowed = IsStepAllowed(v, checkSteps, board.devAllow180Rotation);

        freeNearestSteps = checkSteps;
        freeStepOK = (_isSnapped || (checkSteps != 0)) && stepAllowed && !lockedExceptCenter;

#if ENABLE_INPUT_SYSTEM
        _hadSnapThisFrame = _isSnapped;
        if (_isSnapped && checkSteps != 0)
        {
            _lastSnappedSteps = checkSteps;
            _lastSnapTime = Time.time;
        }
#endif
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
        _usingPad1D = false;
        _lastSnappedSteps = 0;
#endif
    }

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
            float deadSqr = mouseDeadZoneRadius * mouseDeadZoneRadius;
            if (v.sqrMagnitude < deadSqr) return false;
            angleDeg = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
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