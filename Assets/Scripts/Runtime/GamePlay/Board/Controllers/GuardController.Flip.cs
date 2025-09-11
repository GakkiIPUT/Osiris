using System.Collections.Generic;
using UnityEngine;

public partial class GuardController : MonoBehaviour
{
    // 反転小休止
    private bool IsFlipping() => Time.time < flippingUntil;

    private void BeginFlipPause() => BeginFlipPause("default");

    // 反転小休止
    private void BeginFlipPause(string reason)
    {
        // flipPauseSeconds をそのまま使用（pre-flipは別で1セル時間待つ）
        float pause = Mathf.Max(0f, flipPauseSeconds);

        flippingUntil = Time.time + pause;
        lastFlipReason = reason;

        // 反転確定直後は視界を消す（更新間隔を待たずに即時クリア）
        if (showVision) ClearVision();

        Debug.Log($"[GuardPause] {name} reason={reason} dur={pause:F3} until={flippingUntil:F3} t={Time.time:F3}");
    }

    // 反転前待機中か
    private bool IsPreFlipHolding() => preFlipActive && Time.time < preFlipUntil;

    // 反転前待機を開始（移動だけ止める。視界はそのまま）
    private void BeginPreFlipHold(Vector2Int newForward, string reason)
    {
        // Inspector で指定した固定秒数を使用
        float hold = Mathf.Max(0f, preFlipHoldSeconds);

        preFlipActive = true;
        preFlipUntil = Time.time + hold;

        pendingTurn = true;
        pendingForward = newForward;
        pendingTargetYaw = FacingToYaw(StepToFacing(newForward));
        pendingFlipReason = reason;

        // 視界は維持（消さない）
        Debug.Log($"[GuardPause] {name} preHold={hold:F3}s reason={reason} until={preFlipUntil:F3}");
    }

    // 反転前待機が終わったら反転を適用し、flip pause へ遷移
    private void TryApplyPendingTurn()
    {
        if (!pendingTurn) return;
        if (IsPreFlipHolding()) return;

        // ここで初めて向きを反転
        forward = pendingForward;
        targetYaw = pendingTargetYaw;

        if (board != null && board.snapGuardFacingOnMove)
        {
            currentYaw = targetYaw;
            ApplyVisualYaw();
        }
        ApplyVisualByFacing();

        pendingTurn = false;
        preFlipActive = false;
        preFlipUntil = 0f;

        // 次段: 反転後は視界OFFの小休止
        BeginFlipPause(pendingFlipReason);
        pendingFlipReason = "";
    }

    // 追加: 視界メッシュ用ワークバッファ（毎フレームの割当削減）
    private List<Vector3> _visionVerts = null;

    private List<int> _visionTris = null;

    private void UpdateFacingByWatchMode()
    {
        if (watchMode == WatchMode.OneDir) return;
        if (IsPreFlipHolding() || IsFlipping()) return; // ← 追加: 待機中は回さない
        if (Time.time - lastRotateTime < rotatePeriod) return;
        lastRotateTime = Time.time;

        switch (watchMode)
        {
            case WatchMode.TwoDirUD:
                startFacing = (startFacing == Facing.Up) ? Facing.Down : Facing.Up;
                break;

            case WatchMode.TwoDirLR:
                startFacing = (startFacing == Facing.Left) ? Facing.Right : Facing.Left;
                break;

            case WatchMode.Rotate4Dir:
                facingIndex = (facingIndex + (rotateClockwise ? 1 : 3)) & 3;
                startFacing = (Facing)facingIndex;
                break;
        }

        forward = FacingToVec(startFacing);
        targetYaw = FacingToYaw(startFacing);

        if (board != null && board.snapGuardFacingOnMove)
        {
            currentYaw = targetYaw;
            ApplyVisualYaw();
        }

        BeginFlipPause("watch-rotate");
        ApplyVisualByFacing();
    }
}
