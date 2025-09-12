using UnityEngine;

#if ENABLE_INPUT_SYSTEM

using UnityEngine.InputSystem;

#endif

/// <summary>
/// マウス/ゲームパッドでの自由回転プレビューと確定を扱う入力コントローラ。
/// スナップ、±90/±180対応、キャンセル/確定、ゴースト連携を行う。
/// </summary>
public class FreeRotateController : MonoBehaviour
{
    private BoardManager board;
    private PlayerController player;
    private TurnManager turn;

    private bool freeDragging = false;
    private Vector2Int freeCenter;
    private int freeSize = 3;
    private int freeNearestSteps = 0; // -2..2
    private bool freeStepOK = false;

    private float freeStartAngleDeg = 0f;
    private float freeDeltaDeg = 0f;

    [Header("Free Rotate Stabilizer")]
    [Range(0.0f, 0.5f)] public float mouseDeadZoneRadius = 0.18f;

#if ENABLE_INPUT_SYSTEM

    [Header("Free Rotate / Pad Tuning")]
    [Tooltip("フリー回転時 左スティック左右を反転")]
    public bool padInvertHorizontal = false;

    [Tooltip("開始/停止用の外側デッドゾーン半径 (0=無し, 推奨0.2~0.3)")]
    [Range(0f, 0.9f)] public float padDeadZone = 0.25f;

    [Tooltip("1Dモードで角度ゼロへ潰さないための内側係数 (外側×係数)")]
    [Range(0f, 1f)] public float pad1DInnerDeadZoneFactor = 0.4f;

    [Tooltip("±90°判定に必要な最小角度")][Range(5f, 89f)] public float padStepMinDegrees = 30f;

    [Tooltip("スティック離しで確定を使う（OFFでAボタン明示確定方式）")]
    public bool padUseReleaseToCommit = true;

    [Tooltip("離した瞬間スナップ成立で即確定（Releaseモード時）")]
    public bool padCommitImmediateOnRelease = true;

    [Tooltip("即確定失敗時/Immediate OFF 時の遅延確定秒数")]
    [Range(0.02f, 0.5f)] public float padCommitIdleSeconds = 0.12f;

    [Tooltip("スナップしていない離しはキャンセルする（Releaseモード時）")]
    public bool padCancelIfNotSnappedOnRelease = true;

    [Tooltip("離し or A確定時 自動吸着を許可")]
    public bool padAutoSnapOnRelease = true;

    [Tooltip("自動吸着許容角度")][Range(10f, 89f)] public float padAutoSnapDeg = 50f;

    [Tooltip("離し後この ms は最後のスナップを保持（ノイズ救済）")]
    [Range(0f, 300f)] public int padGraceSnapMillis = 120;

    [Header("Post Commit / Cancel")]
    [Tooltip("ボタン確定・キャンセル後の再センター割合（外側DZ×係数以下で解除）")]
    [Range(0.2f, 1f)] public float padRecenterClearFactor = 0.6f;

    [Header("Explicit Commit Mode (padUseReleaseToCommit=OFF)")]
    [Tooltip("明示確定モード: 離したらプレビューを0°へ戻す")]
    public bool padExplicitReleaseReturn = true;

    [Tooltip("0=即時復帰 / 1=遅め補間")][Range(0f, 1f)] public float padExplicitReturnLerp = 0.35f;
    [Tooltip("明示確定モード: ニュートラルで自動キャンセル")] public bool padExplicitAutoCancelOnNeutral = true;
    [Tooltip("ニュートラル継続秒数で自動キャンセル")][Range(0f, 0.5f)] public float padExplicitNeutralCancelDelay = 0.12f;
#endif

    private bool _isSnapped = false;
    private int _latchedSteps = 0;
    private float _previewDegSmoothed = 0f;

#if ENABLE_INPUT_SYSTEM
    private bool _padLeftWasActive = false;
    private float _padLeftInactiveSince = 0f;
    private bool _usingPad1D = false;
    private float _lastInputAngleDeg = 0f;
    private int _lastSnappedSteps = 0;
    private float _lastSnapTime = 0f;
    private bool _padNeedRecenter = false;
    private float _padNeutralSince = -1f;
#endif

    /// <summary>
    /// 参照取得と入力ハンドリングの入り口。プレイヤーターン外ではキャンセル。
    /// </summary>
    private void Update()
    {
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();
        if (player == null) player = UnityCompat.FindFirst<PlayerController>();
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (board == null || turn == null) return;
        if (!turn.IsPlayerTurn()) { CancelIfNeeded(); return; }
        HandleFreeRotate();
    }

    /// <summary>
    /// プレビュー中であればキャンセルし、状態を初期化する。
    /// </summary>
    private void CancelIfNeeded()
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
        _padNeutralSince = -1f;
#endif
    }

    /// <summary>
    /// 自由回転プレビューの開始/更新/確定/キャンセルを処理する（マウス/Pad）。
    /// </summary>
    private void HandleFreeRotate()
    {
        // キャンセル（マウス/キー）
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
            if (!IsCenterAllowed(center) || !HasAnyStep(center)) { FlashNg(center); return; }
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
            // B(East) キャンセル / A(South) 確定 は従来通り
            if (freeDragging && gp.buttonEast.wasPressedThisFrame)
            {
                CancelIfNeeded();
                _padNeedRecenter = true;
                return;
            }
            if (freeDragging && gp.buttonSouth.wasPressedThisFrame)
            {
                if (TryPadCommitExplicit())
                {
                    _padNeedRecenter = true;
                    return;
                }
            }

            // ★ 左スティック移動モード時は、Pad左スティックでの自由回転入力を無効化（マウスは可）
            bool leftStickMoves = board != null && board.devPadLeftStickMoves;
            if (!leftStickMoves)
            {
                Vector2 lvFull = gp.leftStick.ReadValue();

                // 再センター解除
                if (_padNeedRecenter)
                {
                    float baseThrC = player != null ? Mathf.Max(0.2f, player.stickDigitalThreshold * 0.6f) : 0.3f;
                    float outerThrC = Mathf.Max(Mathf.Clamp01(padDeadZone), baseThrC);
                    float clearThr = outerThrC * padRecenterClearFactor;
                    if (lvFull.sqrMagnitude < (clearThr * clearThr))
                        _padNeedRecenter = false;
                }

                // 明示確定モード: ニュートラル処理 & 自動キャンセル
                if (!padUseReleaseToCommit && freeDragging && !_padNeedRecenter)
                {
                    float baseThrX = player != null ? Mathf.Max(0.2f, player.stickDigitalThreshold * 0.6f) : 0.3f;
                    float outerThrX = Mathf.Max(Mathf.Clamp01(padDeadZone), baseThrX);
                    bool neutral = lvFull.sqrMagnitude < outerThrX * outerThrX;

                    if (neutral)
                    {
                        if (padExplicitReleaseReturn)
                            ExplicitReturnPreview();

                        if (padExplicitAutoCancelOnNeutral &&
                            freeNearestSteps == 0 &&
                            Mathf.Abs(freeDeltaDeg) < 1f) // ほぼ原点
                        {
                            if (_padNeutralSince < 0f) _padNeutralSince = Time.time;
                            else if (Time.time - _padNeutralSince >= padExplicitNeutralCancelDelay)
                            {
                                // 自動キャンセル（再センター要求は不要）
                                CancelIfNeeded();
                                return;
                            }
                        }
                    }
                    else
                    {
                        _padNeutralSince = -1f; // ニュートラル離脱
                    }
                }

                // 角度更新（Aiming 中）
                if (player != null && player.IsAiming)
                {
                    Vector2 lv = lvFull;
                    float baseThr = player != null ? Mathf.Max(0.2f, player.stickDigitalThreshold * 0.6f) : 0.3f;
                    float outerThr = Mathf.Max(Mathf.Clamp01(padDeadZone), baseThr);
                    float inner1D = outerThr * Mathf.Clamp01(pad1DInnerDeadZoneFactor);
                    float magSqr = lv.sqrMagnitude;

                    if (magSqr >= outerThr * outerThr)
                    {
                        _padNeutralSince = -1f; // ★ 回転操作再開でリセット
                        if (_padNeedRecenter && !freeDragging) return; // 再センター待ち中は開始禁止

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

                // Release モードの離し確定
                if (padUseReleaseToCommit && !_padNeedRecenter && freeDragging && _padLeftWasActive)
                {
                    Vector2 lv2 = gp.leftStick.ReadValue();
                    float baseThr2 = player != null ? Mathf.Max(0.2f, player.stickDigitalThreshold * 0.6f) : 0.3f;
                    float outerThr2 = Mathf.Max(Mathf.Clamp01(padDeadZone), baseThr2);
                    bool belowOuter = lv2.sqrMagnitude < outerThr2 * outerThr2;

                    if (belowOuter)
                    {
                        if (_padLeftInactiveSince <= 0f) _padLeftInactiveSince = Time.time;

                        bool withinGrace =
                            (Time.time - _lastSnapTime) * 1000f <= padGraceSnapMillis &&
                            _lastSnappedSteps != 0;

                        int candidateSteps = freeNearestSteps;
                        bool candidateOk = freeStepOK;

                        if (!_isSnapped && withinGrace)
                        {
                            candidateSteps = _lastSnappedSteps;
                            candidateOk = true;
                        }
                        else if (!_isSnapped && padAutoSnapOnRelease && freeNearestSteps == 0)
                        {
                            float ad = Mathf.Abs(freeDeltaDeg);
                            if (ad >= padStepMinDegrees && (90f - ad) <= padAutoSnapDeg)
                            {
                                candidateSteps = (freeDeltaDeg > 0f) ? +1 : -1;
                                var v = board.GetStepValidity(freeCenter, freeSize);
                                candidateOk = IsStepAllowed(v, candidateSteps, board.devAllow180Rotation) &&
                                              !board.AreaContainsLockedExceptCenter(freeCenter, freeSize);
                            }
                        }

                        if (padCommitImmediateOnRelease)
                        {
                            if (candidateOk && candidateSteps != 0)
                            {
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
                                    EndPreviewAndCommit();
                            }
                            return;
                        }
                    }
                    else
                    {
                        _padLeftInactiveSince = 0f;
                    }
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

#if ENABLE_INPUT_SYSTEM

    /// <summary>
    /// 明示確定モード時（Aボタン）に回転を確定する。状況により自動スナップも行う。
    /// </summary>
    private bool TryPadCommitExplicit()
    {
        if (!freeDragging) return false;
        if (_isSnapped && freeNearestSteps != 0 && freeStepOK)
        {
            EndPreviewAndCommit();
            return true;
        }

        int candidateSteps = freeNearestSteps;
        bool candidateOk = freeStepOK;

        if ((!_isSnapped || freeNearestSteps == 0) && padAutoSnapOnRelease)
        {
            float ad = Mathf.Abs(freeDeltaDeg);
            if (ad >= padStepMinDegrees)
            {
                if (board != null && !board.devAllow180Rotation)
                {
                    if ((90f - ad) <= padAutoSnapDeg)
                        candidateSteps = (freeDeltaDeg > 0f) ? +1 : -1;
                }
                else
                {
                    int rounded = Mathf.RoundToInt(freeDeltaDeg / 90f);
                    candidateSteps = Mathf.Clamp(rounded, -2, 2);
                }
                var v = board.GetStepValidity(freeCenter, freeSize);
                candidateOk = IsStepAllowed(v, candidateSteps, board.devAllow180Rotation) &&
                              !board.AreaContainsLockedExceptCenter(freeCenter, freeSize);
            }
        }

        if (candidateOk && candidateSteps != 0)
        {
            freeNearestSteps = candidateSteps;
            freeStepOK = true;
            EndPreviewAndCommit();
            return true;
        }

        player?.FlashNgGhostExtern(freeCenter, freeSize, board != null ? board.devNgGhostSeconds : 0.4f);
        return true;
    }

#endif

    /// <summary>
    /// プレビューを開始し、Pivot/ゴーストをセットアップする。
    /// </summary>
    private void BeginPreview(Vector2Int center, float startAngle)
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
        _padNeutralSince = -1f; // ★ reset
#endif
        player?.ShowGhostExtern(true, center, freeSize, false);
        var pivot = board.GetFreePreviewPivot();
        if (pivot != null) player?.AttachGhostTo(pivot, true);
    }

    /// <summary>
    /// 現在の角度からプレビュー角・スナップ状態・可否を更新する。
    /// </summary>
    private void UpdateAngleFrom(float curAngle)
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
        int maxStep = (board != null && board.devAllow180Rotation) ? 2 : 1;
        int candSteps;
        if (board != null && !board.devAllow180Rotation)
        {
            float ad = Mathf.Abs(freeDeltaDeg);
            candSteps = (ad >= padStepMinDegrees) ? (freeDeltaDeg > 0f ? +1 : -1) : 0;
        }
        else
        {
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

        var v2 = board.GetStepValidity(freeCenter, freeSize);
        bool lockedExceptCenter = board.AreaContainsLockedExceptCenter(freeCenter, freeSize);
        int checkSteps = _isSnapped ? _latchedSteps : candSteps;
        bool stepAllowed = IsStepAllowed(v2, checkSteps, board.devAllow180Rotation);

        freeNearestSteps = checkSteps;
        freeStepOK = (_isSnapped || (checkSteps != 0)) && stepAllowed && !lockedExceptCenter;
#if ENABLE_INPUT_SYSTEM
        if (_isSnapped && checkSteps != 0)
        {
            _lastSnappedSteps = checkSteps;
            _lastSnapTime = Time.time;
        }
#endif
        player?.UpdateGhostOkExtern(_isSnapped);
    }

    /// <summary>
    /// 明示確定モードでプレビュー角をゼロへ戻す（補間可）。
    /// </summary>
    private void ExplicitReturnPreview()
    {
        // すでに原点付近なら何もしない
        float target = 0f;
        if (padExplicitReturnLerp <= 0f)
        {
            _previewDegSmoothed = 0f;
        }
        else
        {
            _previewDegSmoothed = Mathf.LerpAngle(_previewDegSmoothed, target, padExplicitReturnLerp);
            // 十分近づいたらスナップ
            if (Mathf.Abs(Mathf.DeltaAngle(_previewDegSmoothed, 0f)) < 0.5f)
                _previewDegSmoothed = 0f;
        }

        freeDeltaDeg = 0f;
        freeNearestSteps = 0;
        freeStepOK = false;
        _isSnapped = false;
        _latchedSteps = 0;

        board.UpdateFreePreviewAngle(-_previewDegSmoothed);
        player?.UpdateGhostOkExtern(false);
    }

    /// <summary>
    /// プレビューを終了し、必要なら回転を確定する（NG時はフィードバック）。
    /// </summary>
    private void EndPreviewAndCommit()
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
        _padNeutralSince = -1f;
#endif
    }

    /// <summary>
    /// ステップ可否を検証するユーティリティ。
    /// </summary>
    private bool IsStepAllowed(BoardManager.StepValidity v, int steps, bool allow180)
    {
        return (steps == +1) ? v.ccw90 :
               (steps == -1) ? v.cw90 :
               (!allow180) ? false :
               (steps == +2) ? v.ccw180 :
               (steps == -2) ? v.cw180 : false;
    }

    /// <summary>
    /// 即時回転コミット（複数ステップを順に適用）。成功分だけAP加算しターンを進める。
    /// </summary>
    private void CommitRotationInstant(Vector2Int center, int size, int steps)
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
        if (applied > 0) turn?.EndPlayerTurn();
    }

    /// <summary>マウスのグリッド座標取得（XZ平面レイ）</summary>
    private bool TryGetMouseGrid(out Vector2Int grid)
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

    private bool TryGetMouseWorld(Vector2Int center, out float angleDeg)
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

    private bool IsCenterAllowed(Vector2Int center)
    {
        freeSize = player != null ? player.areaSize : 3;
        if (board == null) return false;
        return board.IsCenterWithinLimit(center) && !board.AreaContainsLockedExceptCenter(center, freeSize);
    }

    private bool HasAnyStep(Vector2Int center)
    {
        var steps = board.GetStepValidity(center, freeSize);
        return steps.Any(board.devAllow180Rotation);
    }

    private void FlashNg(Vector2Int center)
    {
        player?.FlashNgGhostExtern(center, freeSize, board != null ? board.devNgGhostSeconds : 0.5f);
    }
}
