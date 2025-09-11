using System.Collections.Generic;
using UnityEngine;

public partial class GuardController : MonoBehaviour
{
    // 視界：プレイヤーを見ているか
    public bool CanSeePlayer()
    {
        if (board == null || board.player == null) return false;
        if (IsFlipping()) return false;

        Vector2Int p = board.player.pos;
        if ((p - pos).sqrMagnitude > viewRange * viewRange) return false;

        if (visionMode == VisionMode.GridAligned)
        {
            Facing curF = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);
            float half = fovAngle * 0.5f;
            return IsCellVisibleGridAligned(p, curF, half);
        }

        Facing curFacing = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);
        float yaw = FacingToYaw(curFacing) + visionYawOffsetDeg;
        Vector2 fwd = YawToDir2D(yaw).normalized;

        Vector3 wc = WorldCenter(pos, board.visionY);
        Vector3 offW = new Vector3(transform.position.x - wc.x, 0f, transform.position.z - wc.z);
        Vector2 origin = new Vector2(pos.x + 0.5f, pos.y + 0.5f)
                       + new Vector2(offW.x, offW.z)
                       + fwd * Mathf.Max(0f, visionOriginForwardOffset);

        Vector2 to = new Vector2(p.x + 0.5f, p.y + 0.5f);
        Vector2 dir = to - origin;
        if (dir.sqrMagnitude < 1e-6f) return false;
        dir.Normalize();

        if (Vector2.Angle(fwd, dir) > fovAngle * 0.5f) return false;
        return board.HasLineOfSight(pos, p);
    }

    // 追加: 汎用セル視認判定（泥棒用）
    public bool CanSeeCell(Vector2Int gp)
    {
        if (board == null) return false;
        if ((gp - pos).sqrMagnitude > viewRange * viewRange) return false;

        if (visionMode == VisionMode.GridAligned)
        {
            Facing curF = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);
            float half = fovAngle * 0.5f;
            return IsCellVisibleGridAligned(gp, curF, half);
        }

        Facing curFacing = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);
        float yaw = FacingToYaw(curFacing) + visionYawOffsetDeg;
        Vector2 fwd = YawToDir2D(yaw).normalized;

        Vector3 wc = WorldCenter(pos, board.visionY);
        Vector3 offW = new Vector3(transform.position.x - wc.x, 0f, transform.position.z - wc.z);
        Vector2 origin = new Vector2(pos.x + 0.5f, pos.y + 0.5f)
                       + new Vector2(offW.x, offW.z)
                       + fwd * Mathf.Max(0f, visionOriginForwardOffset);

        Vector2 to = new Vector2(gp.x + 0.5f, gp.y + 0.5f);
        Vector2 dir = to - origin;
        if (dir.sqrMagnitude < 1e-6f) return false;
        dir.Normalize();

        if (Vector2.Angle(fwd, dir) > fovAngle * 0.5f) return false;
        return board.HasLineOfSight(pos, gp);
    }

    // 追加: 視界内の泥棒を宝箱へ変える
    // 追加: 視界内の泥棒を変換（d=宝箱, e=鍵）
    private void RevealThievesInSight()
    {
        if (board == null || board.itemAt == null || board.itemAt.Count == 0) return;

        var toTreasure = new List<Vector2Int>();
        var toKey = new List<Vector2Int>();

        foreach (var kv in board.itemAt)
        {
            char sym = kv.Value.sym;
            if (sym != 'd' && sym != 'e') continue; // 対象は泥棒系のみ
            var p = kv.Key;
            if (CanSeeCell(p))
            {
                if (sym == 'd') toTreasure.Add(p);
                else if (sym == 'e') toKey.Add(p);
            }
        }

        for (int i = 0; i < toTreasure.Count; i++)
            board.TransformThiefToTreasureAt(toTreasure[i]);

        for (int i = 0; i < toKey.Count; i++)
            board.TransformThiefToKeyAt(toKey[i]);
    }

    // セル中心（BoardManager.CellCenter に統一）
    private Vector3 WorldCenter(Vector2Int p, float y)
    {
        return board.CellCenter(p, y);
    }

    // GridAligned 用: マス単位の視界判定（階段状の前方扇形）
    private bool IsCellVisibleGridAligned(Vector2Int gp, Facing curFacing, float halfDeg)
    {
        // LoS が必要（両立）
        if (!board.HasLineOfSight(pos, gp)) return false;

        Vector2Int d = gp - pos;

        int forwardDist, lateral;
        switch (curFacing)
        {
            case Facing.Right:
                forwardDist = d.x;
                lateral = Mathf.Abs(d.y);
                break;

            case Facing.Left:
                forwardDist = -d.x;
                lateral = Mathf.Abs(d.y);
                break;

            case Facing.Up:
                forwardDist = d.y;
                lateral = Mathf.Abs(d.x);
                break;

            default: // Facing.Down
                forwardDist = -d.y;
                lateral = Mathf.Abs(d.x);
                break;
        }

        if (forwardDist <= 0) return false;           // 真後ろ/同位置は除外
        if (forwardDist > viewRange) return false;    // 射程外

        // 角度→グリッド幅へ離散化
        // 先頭列(forwardDist=1)は1マスだけ可視にするため (forwardDist - 1) を使用
        float slope = Mathf.Tan(halfDeg * Mathf.Deg2Rad);
        int lateralMax = Mathf.FloorToInt(Mathf.Max(0f, slope * (forwardDist - 1)) + 1e-4f);

        // fovAngle=90 の場合、幅は 1,3,5,...（きれいな三角形）
        return lateral <= lateralMax;
    }

    // グリッド上で前方に進み、遮蔽セルで停止する簡易レイ
    private Vector2 CastVisionRay(Vector2Int start, float rad, int maxRange, float step, float startOffset = 0f)
    {
        Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)); // 0°=+Z
        Vector2 p = new Vector2(start.x + 0.5f, start.y + 0.5f) + dir * Mathf.Max(0f, startOffset);
        float remain = Mathf.Max(0f, maxRange);

        while (remain > 0f)
        {
            float adv = Mathf.Min(step, remain);
            Vector2 np = p + dir * adv;

            var gp = new Vector2Int(Mathf.FloorToInt(np.x), Mathf.FloorToInt(np.y));
            if (!board.InBounds(gp) || board.BlocksVision(gp))
                break;

            p = np;
            remain -= adv;
        }
        return p; // グリッド座標（中心基準）
    }

    // 外部から切替したい場合に呼べるトグル
    public void SetVisionMode(VisionMode mode)
    {
        visionMode = mode;
        UpdateVisionOverlay();
    }
}
