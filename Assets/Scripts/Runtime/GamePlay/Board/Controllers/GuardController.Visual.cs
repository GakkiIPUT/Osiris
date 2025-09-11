using System.Collections.Generic;
using UnityEngine;

public partial class GuardController : MonoBehaviour
{
    // 見た目は「上下/左右」flipだけを担当（transform.rotationは触らない）
    private void ApplyVisualByFacing()
    {
        Facing curFacing = (forward == Vector2Int.zero) ? startFacing : StepToFacing(forward);

        // コード優先: 方向ごとの固定マッピング（Inspector を無視）
        bool flipX = false;
        bool flipY = false;

        switch (curFacing)
        {
            // 右だけ上下反転していた件: 右は X/Y 両方反転に固定
            case Facing.Right: flipX = false; flipY = true; break;
            case Facing.Left: flipX = false; flipY = false; break;
            case Facing.Up: flipX = false; flipY = false; break;
            case Facing.Down: flipX = false; flipY = false; break;
        }

        ApplySpriteFlipAll(flipX, flipY);
        lastVisualFacing = curFacing;
    }

    // 追加: すべての SpriteRenderer に反転を適用（なければ localScale フォールバック）
    private void ApplySpriteFlipAll(bool flipX, bool flipY)
    {
        var srs = GetComponentsInChildren<SpriteRenderer>(true);
        if (srs != null && srs.Length > 0)
        {
            foreach (var sr in srs) { sr.flipX = flipX; sr.flipY = flipY; }
        }
        else
        {
            var ls = transform.localScale;
            ls.x = Mathf.Abs(ls.x) * (flipX ? -1f : 1f);
            ls.z = Mathf.Abs(ls.z) * (flipY ? -1f : 1f); // X=90°のため上下=Z
            transform.localScale = ls;
        }
    }

    private void ApplyVisualYaw()
    {
        // 見た目の回転だけを担当（flipは触らない）
        float yawVisual = currentYaw + visualYawOffsetDeg;
        transform.rotation = Quaternion.AngleAxis(yawVisual, Vector3.up) * baseRot;
    }

    private void EnsureFacingArrow()
    {
        if (facingArrow != null) return;
        facingArrow = GameObject.CreatePrimitive(PrimitiveType.Quad);
        facingArrow.name = "FacingArrow";
        facingArrow.transform.SetParent(transform, false);
        facingArrow.transform.localRotation = Quaternion.Euler(90, 0, 0);
        facingArrow.transform.localScale = new Vector3(0.25f, 0.25f, 1f);
        facingArrow.transform.localPosition = new Vector3(0f, 0.02f, 0.35f);
        var mr = facingArrow.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        if (board != null && board.ghostOkMat != null) mr.material = board.ghostOkMat;
        mr.material.color = facingArrowColor;
        Destroy(facingArrow.GetComponent<MeshCollider>());
    }

    // ======== 追加: GameOver時の強調表示 ========
    // 強調表示のON/OFF
    public void SetKillerHighlight(bool on)
    {
        if (on == _killerHighlighted) return;
        _killerHighlighted = on;

        if (on)
        {
            if (_bodyRenderers == null) CacheBodyRenderers();

            _savedVisionColor = visionColor;
            if (useBodyTintOnHighlight) ApplyBodyTint(killerBodyTint);
            visionColor = killerVisionColor;
            UpdateVisionOverlay();
            ShowKillerOutline(true);
        }
        else
        {
            if (useBodyTintOnHighlight) RestoreBodyTint();
            visionColor = _savedVisionColor;
            UpdateVisionOverlay();
            ShowKillerOutline(false);
        }
    }

    public void SetVisionColorAndRefresh(Color c)
    {
        visionColor = c;
        UpdateVisionOverlay();
    }

    // ===== 追加: アウトライン（周りに色） =====
    public void ShowKillerOutline(bool on)
    {
        if (on)
        {
            if (_killerOutlineLR == null) BuildKillerOutline();
            if (_killerOutlineLR != null) _killerOutlineLR.enabled = true;
        }
        else
        {
            if (_killerOutlineLR != null) _killerOutlineLR.enabled = false;
        }
    }

    // ===== 追加: 『！』スプライト =====
    private void BuildKillerOutline()
    {
        if (board == null) return;

        var go = new GameObject("KillerOutline");
        if (board.actorsRoot != null) go.transform.SetParent(board.actorsRoot, false);
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true; // ワールド空間で直接描く
        lr.loop = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.textureMode = LineTextureMode.Stretch;
        lr.alignment = LineAlignment.View; // 水平リングで問題なし
        lr.startWidth = killerOutlineWidth;
        lr.endWidth = killerOutlineWidth;

        var mat = new Material(Shader.Find("Sprites/Default"));
        lr.material = mat;
        lr.startColor = killerOutlineColor;
        lr.endColor = killerOutlineColor;
        lr.numCornerVertices = 2;
        lr.numCapVertices = 2;

        int seg = Mathf.Clamp(killerOutlineSegments, 12, 128);
        lr.positionCount = seg;
        float r = Mathf.Max(0.01f, killerOutlineRadius);

        Vector3 center = transform.position + new Vector3(0f, killerOutlineYOffset, 0f);
        for (int i = 0; i < seg; i++)
        {
            float t = (i / (float)seg) * Mathf.PI * 2f;
            float x = Mathf.Cos(t) * r;
            float z = Mathf.Sin(t) * r; // XZ平面に配置
            lr.SetPosition(i, new Vector3(center.x + x, center.y, center.z + z));
        }

        _killerOutlineLR = lr;
    }

    private void ClearKillerOutline()
    {
        if (_killerOutlineLR != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(_killerOutlineLR.gameObject);
            else
#endif
                Destroy(_killerOutlineLR.gameObject);
            _killerOutlineLR = null;
        }
    }

    // ===== 追加: 体のRendererキャッシュ/ティント適用/復元 =====
    private void CacheBodyRenderers()
    {
        _bodySRs = GetComponentsInChildren<SpriteRenderer>(true);
        if (_bodySRs != null && _bodySRs.Length > 0)
        {
            _bodySRsDefault = new Color[_bodySRs.Length];
            for (int i = 0; i < _bodySRs.Length; i++)
                _bodySRsDefault[i] = _bodySRs[i].color;
        }
        var allRends = GetComponentsInChildren<Renderer>(true);
        var list = new List<Renderer>(allRends.Length);
        for (int i = 0; i < allRends.Length; i++)
        {
            var r = allRends[i];
            if (r == null) continue;
            if (facingArrow != null && r.gameObject == facingArrow) continue; // 矢印は除外
            list.Add(r);
        }
        _bodyRenderers = list.ToArray();
        _bodyMpb = new MaterialPropertyBlock();
    }

    private void ApplyBodyTint(Color tint)
    {
        if (_bodySRs != null)
        {
            for (int i = 0; i < _bodySRs.Length; i++)
                if (_bodySRs[i]) _bodySRs[i].color = tint;
        }
        if (_bodyRenderers != null)
        {
            for (int i = 0; i < _bodyRenderers.Length; i++)
            {
                var r = _bodyRenderers[i];
                if (!r) continue;
                var mat = r.sharedMaterial;
                if (!mat) continue;

                _bodyMpb.Clear();
                bool setAny = false;
                if (mat.HasProperty("_Color")) { _bodyMpb.SetColor("_Color", tint); setAny = true; }
                if (mat.HasProperty("_BaseColor")) { _bodyMpb.SetColor("_BaseColor", tint); setAny = true; }
                if (mat.HasProperty("_TintColor")) { _bodyMpb.SetColor("_TintColor", tint); setAny = true; }
                if (setAny) r.SetPropertyBlock(_bodyMpb);
            }
        }
    }

    private void RestoreBodyTint()
    {
        if (_bodySRs != null && _bodySRsDefault != null && _bodySRsDefault.Length == _bodySRs.Length)
        {
            for (int i = 0; i < _bodySRs.Length; i++)
                if (_bodySRs[i]) _bodySRs[i].color = _bodySRsDefault[i];
        }
        if (_bodyRenderers != null)
        {
            for (int i = 0; i < _bodyRenderers.Length; i++)
                if (_bodyRenderers[i]) _bodyRenderers[i].SetPropertyBlock(null);
        }
    }

    // ===== 追加: 『！』表示（寿命なし常時表示/破棄） =====
    public void ShowKillerMarkPersistent()
    {
        HideKillerMark();
        if (exclamationSprite == null) return;

        _killerMarkGO = new GameObject("KillerMark_Exclamation");
        if (board != null && board.actorsRoot != null)
            _killerMarkGO.transform.SetParent(board.actorsRoot, false);

        var sr = _killerMarkGO.AddComponent<SpriteRenderer>();
        sr.sprite = exclamationSprite;
        sr.color = exclamationTint;
        if (!string.IsNullOrEmpty(exclamationSortingLayer))
            sr.sortingLayerName = exclamationSortingLayer;
        sr.sortingOrder = exclamationSortingOrder;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;

        // ワールド相対位置（exclamationWorldOffset が優先。後方互換として y=0 の場合は exclamationHeight を足す）
        Vector3 offset = exclamationWorldOffset;
        if (Mathf.Approximately(offset.y, 0f) && exclamationHeight > 0f)
            offset.y = exclamationHeight;

        Vector3 worldPos = transform.position + offset + Vector3.up * Mathf.Max(0f, exclamationLiftEpsilon);
        _killerMarkGO.transform.position = worldPos;

        // 姿勢：床に寝かせる or 直立
        float pitch = exclamationLayOnFloor ? 90f : 0f;
        _killerMarkGO.transform.rotation = Quaternion.Euler(pitch, exclamationWorldYaw, 0f);

        // 表示サイズ
        _killerMarkGO.transform.localScale = new Vector3(exclamationSize.x, exclamationSize.y, 1f);
    }

    private void HideKillerMark()
    {
        if (_killerMarkGO == null) return;
#if UNITY_EDITOR
        if (!Application.isPlaying) DestroyImmediate(_killerMarkGO);
        else
#endif
            Destroy(_killerMarkGO);
        _killerMarkGO = null;
    }

    private Facing StepToFacing(Vector2Int st)
    {
        if (st.x > 0) return Facing.Right;
        if (st.x < 0) return Facing.Left;
        if (st.y > 0) return Facing.Up;
        return Facing.Down;
    }

    private Vector2Int FacingToVec(Facing f)
    {
        switch (f)
        {
            case Facing.Up: return Vector2Int.up;
            case Facing.Down: return Vector2Int.down;
            case Facing.Left: return Vector2Int.left;
            default: return Vector2Int.right;
        }
    }

    // 向き/回転
    private float FacingToYaw(Facing f)
    {
        switch (f)
        {
            case Facing.Up: return 0f;
            case Facing.Right: return 90f;
            case Facing.Down: return 180f;
            case Facing.Left: return 270f;
        }
        return 0f;
    }
}
