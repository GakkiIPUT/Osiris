using System.Collections.Generic;
using UnityEngine;

public partial class BoardManager
{
    private GameObject freePreviewPivot;
    private Transform freePreviewGroup;
    private List<Transform> freeTiles = new();
    private List<Transform> freeItems = new();
    private Transform freePlayerTf;
    private Quaternion freeSavedPlayerRot = Quaternion.identity;
    private bool freePlayerIn = false;
    private Vector2Int freeCenter;
    private int freeSize;

    private struct PreviewWallEntry
    {
        public Transform tf;
        public WallOrigin origin;
        public Vector2Int originalGrid;
    }
    private List<PreviewWallEntry> previewWalls = new();

    public bool AreaContainsLockedExceptCenter(Vector2Int center, int size)
    {
        int k = (size - 1) / 2;
        for (int j = -k; j <= k; j++)
        {
            for (int i = -k; i <= k; i++)
            {
                var p = new Vector2Int(center.x + i, center.y + j);
                if (!InBounds(p)) continue;
                if (p == center) continue;
                if (IsRotateLockedCell(p)) return true;
            }
        }
        return false;
    }

    public bool BeginFreePreview(Vector2Int center, int size)
    {
        if (freePreviewPivot != null) RestoreFreePreview();
        freeCenter = center;
        freeSize = size;

        freePreviewPivot = new GameObject($"FreePreviewPivot_{center.x}_{center.y}");
        freePreviewPivot.transform.position = CellCenter(center, floorY + 0.05f);

        freePreviewGroup = new GameObject("FreePreviewGroup").transform;
        freePreviewGroup.SetParent(freePreviewPivot.transform, false);
        freePreviewGroup.localPosition = Vector3.zero;
        freePreviewGroup.localRotation = Quaternion.identity;
        freePreviewGroup.localScale = Vector3.one;

        int k = (size - 1) / 2;

        freeTiles.Clear();
        previewWalls.Clear();
        for (int j = -k; j <= k; j++)
        {
            for (int i = -k; i <= k; i++)
            {
                var p = new Vector2Int(center.x + i, center.y + j);
                if (!InBounds(p)) continue;
                var go = tileGOs[p.y, p.x];
                if (!go) continue;
                go.transform.SetParent(freePreviewGroup, true);
                freeTiles.Add(go.transform);
                if (cells[p.y, p.x] == CellType.Wall)
                    previewWalls.Add(new PreviewWallEntry
                    {
                        tf = go.transform,
                        origin = autoGenerateOuterRings ? wallOrigin[p.y, p.x] : WallOrigin.Core,
                        originalGrid = p
                    });
            }
        }

        freeItems.Clear();
        if (itemAt != null && itemAt.Count > 0)
        {
            foreach (var kv in itemAt)
            {
                var p = kv.Key;
                if (p.x >= center.x - k && p.x <= center.x + k &&
                    p.y >= center.y - k && p.y <= center.y + k)
                {
                    var go = kv.Value.go;
                    if (go)
                    {
                        go.transform.SetParent(freePreviewGroup, true);
                        freeItems.Add(go.transform);
                    }
                }
            }
        }

        freePlayerIn = IsPlayerInsideArea(center, size);
        if (player != null && rotatePlayerWithArea && freePlayerIn)
        {
            freePlayerTf = player.transform;
            freeSavedPlayerRot = freePlayerTf.rotation;
            freePlayerTf.SetParent(freePreviewGroup, true);
        }
        else
        {
            freePlayerTf = null;
        }

        return true;
    }

    public void UpdateFreePreviewAngle(float angleDeg)
    {
        if (freePreviewPivot == null) return;
        freePreviewPivot.transform.rotation = Quaternion.Euler(0f, angleDeg, 0f);

        if (!autoGenerateOuterRings) return;
        if (previewWalls.Count == 0) return;
        if (wallNormalMat == null || wallOuterMat == null) return;

        float rad = angleDeg * Mathf.Deg2Rad;
        float sin = Mathf.Sin(rad);
        float cos = Mathf.Cos(rad);

        foreach (var w in previewWalls)
        {
            var d = w.originalGrid - freeCenter;
            float rx = d.x * cos - d.y * sin;
            float ry = d.x * sin + d.y * cos;
            var proj = new Vector2Int(Mathf.RoundToInt(rx) + freeCenter.x,
                                      Mathf.RoundToInt(ry) + freeCenter.y);
            bool outside = !IsInsideCore(proj);

            var rend = w.tf.GetComponentInChildren<Renderer>();
            if (!rend) continue;

            if (w.origin == WallOrigin.Outer && outside)
                rend.sharedMaterial = wallOuterMat;
            else
                rend.sharedMaterial = wallNormalMat;
        }
    }

    public void RestoreFreePreview()
    {
        if (freePreviewPivot == null) return;

        freePreviewPivot.transform.rotation = Quaternion.identity;
        if (freePreviewGroup != null) freePreviewGroup.localRotation = Quaternion.identity;

        for (int i = 0; i < freeTiles.Count; i++)
        {
            var t = freeTiles[i];
            if (t) t.SetParent(tilesRoot, true);
        }
        freeTiles.Clear();

        for (int i = 0; i < freeItems.Count; i++)
        {
            var t = freeItems[i];
            if (t) t.SetParent(itemsRoot, true);
        }
        freeItems.Clear();

        if (freePlayerTf)
        {
            freePlayerTf.SetParent(actorsRoot, true);
            freePlayerTf.rotation = freeSavedPlayerRot;
        }
        freePlayerTf = null;
        freePlayerIn = false;

        if (freePreviewGroup != null)
        {
            SafeDestroy(freePreviewGroup.gameObject);
            freePreviewGroup = null;
        }
        SafeDestroy(freePreviewPivot);
        freePreviewPivot = null;
        previewWalls.Clear();
        UpdateAllWallAppearances();
    }

    // 追加: コミット専用（角度をリセットしない）
    public void RestoreFreePreviewForCommit()
    {
        if (freePreviewPivot == null) return;

        // ピボットの角度は保持したまま、子だけ元の親へ戻す（worldPositionStays=true）
        for (int i = 0; i < freeTiles.Count; i++)
        {
            var t = freeTiles[i];
            if (t) t.SetParent(tilesRoot, true);
        }
        freeTiles.Clear();

        for (int i = 0; i < freeItems.Count; i++)
        {
            var t = freeItems[i];
            if (t) t.SetParent(itemsRoot, true);
        }
        freeItems.Clear();

        if (freePlayerTf)
        {
            freePlayerTf.SetParent(actorsRoot, true);
            // freeSavedPlayerRot は使わない（回転後の見た目を維持）
        }
        freePlayerTf = null;
        freePlayerIn = false;

        if (freePreviewGroup != null)
        {
            SafeDestroy(freePreviewGroup.gameObject);
            freePreviewGroup = null;
        }
        SafeDestroy(freePreviewPivot);
        freePreviewPivot = null;
        previewWalls.Clear();
        UpdateAllWallAppearances();
    }

    public Transform GetFreePreviewPivot()
    {
        return freePreviewGroup != null ? freePreviewGroup : (freePreviewPivot != null ? freePreviewPivot.transform : null);
    }

    public bool IsFreePreviewActive => freePreviewPivot != null;
}
