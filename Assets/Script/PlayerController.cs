using UnityEngine;

public class PlayerController : MonoBehaviour
{
    BoardManager board;
    TurnManager turn;

    public Vector2Int pos;

    int areaSize = 3; // 3 or 5
    bool aiming = false;
    Vector2Int aimCenter;

    GameObject ghostRoot;

    public void Init(BoardManager b, Vector2Int start)
    {
        board = b;
        pos = start;
        transform.position = board.GridToWorldActor(pos);
        turn = UnityCompat.FindFirst<TurnManager>();
    }

    void Update()
    {
        if (turn == null || !turn.IsPlayerTurn()) return;

        // 範囲切替
        if (Input.GetKeyDown(KeyCode.Alpha1)) { areaSize = 3; if (aiming) { BuildGhostTiles(); UpdateGhostVisual(); } }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { areaSize = 5; if (aiming) { BuildGhostTiles(); UpdateGhostVisual(); } }

        // 視界可視化トグル
        if (Input.GetKeyDown(KeyCode.V))
        {
            board.ToggleAllGuardVision();
        }

        // クリックでエイム開始/更新
        if (Input.GetMouseButtonDown(0))
        {
            Vector2Int g;
            if (TryGetMouseGrid(out g))
            {
                aiming = true;
                aimCenter = g;
                ShowGhost(true);
            }
        }
        // 右クリック or Esc で解除
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
        {
            aiming = false;
            ShowGhost(false);
        }

        // 移動（1手消費）
        Vector2Int dir = Vector2Int.zero;
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) dir = Vector2Int.up;
        else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) dir = Vector2Int.down;
        else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) dir = Vector2Int.left;
        else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) dir = Vector2Int.right;
        if (dir != Vector2Int.zero)
        {
            var np = pos + dir;
            if (board.IsWalkable(np))
            {
                pos = np;
                transform.position = board.GridToWorldActor(pos);
                board.TryPickup(pos);
                // クリア判定
                if (board.cells[pos.y, pos.x] == CellType.Exit)
                {
                    turn.TriggerClear();
                }
                turn.EndPlayerTurn();
                return;
            }
        }

        // エイム中：Q/E or ホイールで回転実行（1手消費）
        if (aiming && (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E)))
        {
            int dirRot = Input.GetKeyDown(KeyCode.Q) ? -1 : +1;
            TryRotate(dirRot);
        }
        if (aiming && Input.mouseScrollDelta.y != 0f)
        {
            int dirRot = Input.mouseScrollDelta.y > 0 ? +1 : -1;
            TryRotate(dirRot);
        }

        // エイム中はゴースト更新（OK/NG色）
        if (aiming) UpdateGhostVisual();
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
            // 盤外でもエイム開始可（部分回転対応）。Grid値はそのまま持つ。
            return true;
        }
        return false;
    }

    void TryRotate(int dirRot)
    {
        // プレビュー確認（境界/衝突NGなら不発）
        var pv = board.GetPreview(aimCenter, areaSize);
        if (!pv.valid)
        {
            UpdateGhostVisual(); // NG色
            return;
        }

        // 実行 → 成功後：エイム解除＆ターン終了
        board.RotateArea(aimCenter, areaSize, dirRot, () =>
        {
            aiming = false;
            ShowGhost(false);
            turn.EndPlayerTurn();
        });
    }

    // ====== ゴースト表示（エイム時のみNxN半透明を出す） ======
    void ShowGhost(bool on)
    {
        if (!on)
        {
            if (ghostRoot != null) Destroy(ghostRoot);
            return;
        }
        if (ghostRoot == null)
        {
            ghostRoot = new GameObject("Ghost");
        }
        BuildGhostTiles();
        UpdateGhostVisual();
    }

    void BuildGhostTiles()
    {
        foreach (Transform c in ghostRoot.transform) Destroy(c.gameObject);
        int k = (areaSize - 1) / 2;
        for (int j = -k; j <= k; j++)
            for (int i = -k; i <= k; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.transform.SetParent(ghostRoot.transform, false);
                quad.transform.rotation = Quaternion.Euler(90, 0, 0);
                quad.transform.localScale = new Vector3(1f, 1f, 1f);

                var cell = new Vector2Int(aimCenter.x + i, aimCenter.y + j);
                quad.transform.position = board.CellCenter(cell, board.previewY);

                var mr = quad.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                Destroy(quad.GetComponent<MeshCollider>());
            }
    }

    void UpdateGhostVisual()
    {
        if (ghostRoot == null) return;
        int k = (areaSize - 1) / 2;
        var pv = board.GetPreview(aimCenter, areaSize);
        var mat = pv.valid ? board.ghostOkMat : board.ghostNgMat;

        int idx = 0;
        for (int j = -k; j <= k; j++)
            for (int i = -k; i <= k; i++)
            {
                var tf = ghostRoot.transform.GetChild(idx++);
                var cell = new Vector2Int(aimCenter.x + i, aimCenter.y + j);
                tf.position = board.CellCenter(cell, board.previewY);

                var mr = tf.GetComponent<MeshRenderer>();
                if (mat != null) mr.material = mat;
            }
    }

    // === UI から呼ぶため ===
public void UI_RotateCW()
    {
        if (turn == null || !turn.IsPlayerTurn()) return;
        if (!aiming) { aimCenter = pos; aiming = true; ShowGhost(true); }
        TryRotate(+1);
    }

    public void UI_RotateCCW()
    {
        if (turn == null || !turn.IsPlayerTurn()) return;
        if (!aiming) { aimCenter = pos; aiming = true; ShowGhost(true); }
        TryRotate(-1);
    }

    public void UI_ToggleAreaSize()
    {
        areaSize = (areaSize == 3) ? 5 : 3;
        if (aiming) { BuildGhostTiles(); UpdateGhostVisual(); }
    }

    public void UI_ToggleVision()
    {
        board.ToggleAllGuardVision();
    }

    public void UI_CancelAim()
    {
        aiming = false;
        ShowGhost(false);
    }
}
