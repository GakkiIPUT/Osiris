using System.Collections.Generic;
using UnityEngine;

public partial class GuardController : MonoBehaviour
{
    // 旧スタイル（セル毎Quad）/ 新グリッド整合の描画
    public void UpdateVisionOverlay()
    {
        if (!showVision || IsFlipping()) { ClearVision(); return; }

        Vector3 worldCenter = WorldCenter(pos, board.visionY);
        Vector3 offsetWorld = new Vector3(transform.position.x - worldCenter.x, 0f, transform.position.z - worldCenter.z);
        Vector2 offset2D = new Vector2(offsetWorld.x, offsetWorld.z);

        // SmoothFan（ビジョン用Rendererは1つ）
        if (visionMode == VisionMode.SmoothFan)
        {
            BuildSmoothVisionMeshWithOffset(offsetWorld, offset2D);
            if (visionMr) ApplyVisionColor(visionMr);
            return;
        }

        // CellFan / GridAligned（セルごとQuad）
        var root = GetVisionRoot();
        ClearVision();

        int r = viewRange;
        float half = fovAngle * 0.5f;
        var mat = (board.guardVisionMat != null) ? board.guardVisionMat
                 : (board.ghostOkMat != null ? board.ghostOkMat : board.ghostNgMat);

        Facing curFacing = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);
        float yaw = FacingToYaw(curFacing) + visionYawOffsetDeg;
        Vector2 fwd = GetForward2DFromYaw(yaw).normalized;

        // CellFan は起点をサブセルまでずらす。GridAligned は判定がグリッド基準なので未使用
        Vector2 origin2D = new Vector2(pos.x + 0.5f, pos.y + 0.5f) + offset2D + fwd * Mathf.Max(0f, visionOriginForwardOffset);

        for (int yCell = pos.y - r; yCell <= pos.y + r; yCell++)
            for (int xCell = pos.x - r; xCell <= pos.x + r; xCell++)
            {
                var gp = new Vector2Int(xCell, yCell);
                if (!board.InBounds(gp)) continue;
                if ((gp - pos).sqrMagnitude > r * r) continue;

                bool visible = (visionMode == VisionMode.CellFan)
                    ? (board.HasLineOfSight(pos, gp) &&
                       Vector2.Angle(fwd, (new Vector2(xCell + 0.5f, yCell + 0.5f) - origin2D).normalized) <= half)
                    : IsCellVisibleGridAligned(gp, curFacing, half);

                if (!visible) continue;

                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = $"Vision_{xCell}_{yCell}";
                q.transform.SetParent(root.transform, false);
                q.transform.rotation = Quaternion.Euler(90, 0, 0);
                q.transform.localScale = Vector3.one;

                Vector3 drawPos = WorldCenter(gp, board.visionY);
                if (visionMode == VisionMode.CellFan)
                    drawPos += offsetWorld;
                q.transform.position = drawPos;

                var mr = q.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                if (mat != null) mr.material = mat;
                ApplyVisionColor(mr);
                Destroy(q.GetComponent<MeshCollider>());
            }
    }

    private void BuildSmoothVisionMeshWithOffset(Vector3 offsetWorld, Vector2 offset2D)
    {
        var root = GetVisionRoot();

        var mf = root.GetComponent<MeshFilter>();
        if (mf == null) mf = root.AddComponent<MeshFilter>();
        var mr = root.GetComponent<MeshRenderer>();
        if (mr == null) mr = root.AddComponent<MeshRenderer>();
        visionMf = mf;
        visionMr = mr;

        if (visionMesh == null)
        {
            visionMesh = new Mesh { name = "VisionFan" };
            visionMesh.MarkDynamic();
        }
        visionMf.sharedMesh = visionMesh;

        var mat = (board.guardVisionMat != null) ? board.guardVisionMat
                 : (board.ghostOkMat != null ? board.ghostOkMat : board.ghostNgMat);
        if (visionMr.sharedMaterial != mat) visionMr.sharedMaterial = mat;

        // flipPause 中は UpdateVisionOverlay が先に return するため通常ここは呼ばれませんが、
        // 念のため描画再開時に有効化
        if (!visionMr.enabled) visionMr.enabled = true; // ← 追加

        int rays = Mathf.Clamp(visionRayCount, 12, 256);
        float half = fovAngle * 0.5f;

        Facing curFacing = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);
        float yaw = FacingToYaw(curFacing) + visionYawOffsetDeg;
        Vector2 fwd = GetForward2DFromYaw(yaw).normalized;

        Vector3 origin = WorldCenter(pos, board.visionY) + offsetWorld + new Vector3(fwd.x, 0f, fwd.y) * Mathf.Max(0f, visionOriginForwardOffset);

        _visionVerts ??= new List<Vector3>(rays + 2);
        _visionTris ??= new List<int>(rays * 3);

        if (_visionVerts.Capacity < rays + 2) _visionVerts.Capacity = rays + 2;
        if (_visionTris.Capacity < rays * 3) _visionTris.Capacity = rays * 3;

        _visionVerts.Clear();
        _visionTris.Clear();

        _visionVerts.Add(origin);

        for (int i = 0; i <= rays; i++)
        {
            float t = (rays == 0) ? 0f : (i / (float)rays);
            float ang = (yaw - half) + (fovAngle * t);

            Vector2 end = CastVisionRay(pos, ang * Mathf.Deg2Rad, viewRange, visionRayStep, visionOriginForwardOffset);
            Vector3 v = WorldFromGridCenterCoords(end, board.visionY) + offsetWorld;
            _visionVerts.Add(v);
        }

        for (int i = 1; i < _visionVerts.Count - 1; i++)
        {
            _visionTris.Add(0); _visionTris.Add(i); _visionTris.Add(i + 1);
        }

        visionMesh.Clear();
        visionMesh.SetVertices(_visionVerts);
        visionMesh.SetTriangles(_visionTris, 0);
        visionMesh.RecalculateBounds();

        root.transform.rotation = Quaternion.identity;
    }

    private void BuildSmoothVisionMesh()
    {
        // 互換: 旧呼び出しからはオフセット(0)で呼ぶ
        BuildSmoothVisionMeshWithOffset(Vector3.zero, Vector2.zero);
    }

    private GameObject GetVisionRoot()
    {
        if (visionRoot == null)
        {
            visionRoot = new GameObject($"{name}_Vision");
            if (board != null && board.actorsRoot != null)
                visionRoot.transform.SetParent(board.actorsRoot, false);
            else
                visionRoot.transform.SetParent(null, false);

            visionRoot.transform.position = Vector3.zero;
            visionRoot.transform.rotation = Quaternion.identity;
            visionRoot.transform.localScale = Vector3.one;
        }
        return visionRoot;
    }

    // ===== 追加: 視界色の適用 =====
    private void ApplyVisionColor(Renderer r)
    {
        if (!r) return;
        var mat = r.sharedMaterial;
        if (!mat) return;

        _mpb ??= new MaterialPropertyBlock();
        _mpb.Clear();

        bool setAny = false;
        if (mat.HasProperty("_Color")) { _mpb.SetColor("_Color", visionColor); setAny = true; }
        if (mat.HasProperty("_BaseColor")) { _mpb.SetColor("_BaseColor", visionColor); setAny = true; }
        if (mat.HasProperty("_TintColor")) { _mpb.SetColor("_TintColor", visionColor); setAny = true; }

        if (setAny) r.SetPropertyBlock(_mpb);
        else
        {
            var inst = r.material;
            if (inst.HasProperty("_Color")) inst.SetColor("_Color", visionColor);
            else if (inst.HasProperty("_BaseColor")) inst.SetColor("_BaseColor", visionColor);
            else if (inst.HasProperty("_TintColor")) inst.SetColor("_TintColor", visionColor);
        }

        // 追加: 犯人は後描画にする
        ApplyVisionRenderOrder(r);
    }

    // 透明レンダーキュー（犯人を後描画にする）
    private const int VisionQueueBase = 3000;          // Transparent

    private const int VisionQueueKiller = VisionQueueBase + 20; // 犯人用に少し後ろ

    private void ApplyVisionRenderOrder(Renderer r)
    {
        if (!r) return;

        if (_killerHighlighted)
        {
            // 犯人はマテリアルインスタンス化してレンダーキューを上げる
            var inst = r.material; // インスタンス化
            if (inst != null) inst.renderQueue = VisionQueueKiller;

            // 念のためソーティングオーダーも上げておく（同一距離のタイブレーク）
            r.sortingOrder = 10;
        }
        else
        {
            var inst = r.material;
            if (inst != null) inst.renderQueue = VisionQueueBase;
            // 非犯人はデフォルトのまま（共有マテリアルのキュー=3000）
            r.sortingOrder = 0;
            // ここで inst に戻す必要は特にありません（再生成時に共有マテリアルへ戻るため）
        }
    }

    // 視界の子オブジェクト/メッシュをクリア
    private void ClearVision()
    {
        if (visionRoot == null) return;

        if (visionMode == VisionMode.SmoothFan)
        {
            // Mesh を空にしつつ Renderer も明示的に無効化して完全に非表示化
            if (visionMesh != null) visionMesh.Clear();
            if (visionMr != null) visionMr.enabled = false; // ← 追加
            return;
        }

        for (int i = visionRoot.transform.childCount - 1; i >= 0; --i)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(visionRoot.transform.GetChild(i).gameObject);
            else
#endif
                Destroy(visionRoot.transform.GetChild(i).gameObject);
        }
    }

    // 追加: グリッド「中心座標系」の連続値(Vector2)をワールド座標に変換
    // center は (i+0.5, j+0.5) をセル中心とする座標系。
    // → CellCenter に (center - 0.5) のローカル差分を加えてワールドへ。
    private Vector3 WorldFromGridCenterCoords(Vector2 center, float y)
    {
        int cx = Mathf.FloorToInt(center.x);
        int cy = Mathf.FloorToInt(center.y);
        Vector3 c = board.CellCenter(new Vector2Int(cx, cy), y);
        float fx = center.x - cx - 0.5f; // [-0.5 .. +0.5)
        float fz = center.y - cy - 0.5f; // [-0.5 .. +0.5)
        return c + new Vector3(fx, 0f, fz);
    }
}
