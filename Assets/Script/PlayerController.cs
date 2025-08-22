using UnityEngine;

public class PlayerController : MonoBehaviour
{
    BoardManager board;
    TurnManager turn;

    public Vector2Int pos;

    public bool invincible = false; // 無敵モード
    public int areaSize
    {
        get => _areaSize;
        set
        {
            _areaSize = Mathf.Clamp(value, 3, 9); // 3,5,7,9など
            if (aiming) { BuildGhostTiles(); UpdateGhostVisual(); }
        }
    }
    private int _areaSize = 3;

    bool aiming = false;
    Vector2Int aimCenter;

    GameObject ghostRoot;

    // UI から参照するためのプロパティ
    public bool IsAiming => aiming;

    public void Init(BoardManager b, Vector2Int start)
    {
        board = b;
        pos = start;
        transform.position = board.GridToWorldActor(pos);
        turn = UnityCompat.FindFirst<TurnManager>();
    }

    void Update()
    {
        //ターン制 if (turn == null || !turn.IsPlayerTurn()) return;

        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (turn != null && (turn.gameOver || turn.cleared)) return;

        // 範囲切替（ホイールで切替／キーで固定）
        if (Input.mouseScrollDelta.y != 0f)
        {
            ToggleAreaSize();
            if (aiming) { BuildGhostTiles(); UpdateGhostVisual(); }
        }
        if (Input.GetKeyDown(KeyCode.Alpha1)) { areaSize = 3; if (aiming) { BuildGhostTiles(); UpdateGhostVisual(); } }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { areaSize = 5; if (aiming) { BuildGhostTiles(); UpdateGhostVisual(); } }

        // 敵視界トグル（常時表示運用でもトグルは残す）
        if (Input.GetKeyDown(KeyCode.V)) board.ToggleAllGuardVision();

        // クリックでエイム開始/更新
        if (Input.GetMouseButtonDown(0))
        {
            if (TryGetMouseGrid(out var g))
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

        // WASD移動（1手消費）
        Vector2Int dir = Vector2Int.zero;
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) dir = Vector2Int.up;
        else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) dir = Vector2Int.down;
        else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) dir = Vector2Int.left;
        else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) dir = Vector2Int.right;
        if (dir != Vector2Int.zero)
        {
            var np = pos + dir;
            //if (board.IsWalkable(np))
            //{
            //    pos = np;
            //    transform.position = board.GridToWorldActor(pos);

            //    if (board.cells[pos.y, pos.x] == CellType.Exit)
            //    {
            //        turn.TriggerClear();
            //    }
            //    else
            //    {
            //        // アイテムがあれば取得
            //        board.TryPickupItemAt(pos);
            //    }

            //    turn.EndPlayerTurn();
            //    return;
            //}
            if (board.IsWalkable(np, true))
            {
                pos = np;
                transform.position = board.GridToWorldActor(pos);

                // アイテムがあれば取得
                board.TryPickupItemAt(pos);

                // クリア判定（出口は通行可のまま）
                if (board.cells[pos.y, pos.x] == CellType.Exit)
                {
                    turn.TriggerClear();
                }
                turn.EndPlayerTurn();
                return;
            }
        }

        // エイム中：Q/E で回転実行（1手消費）
        if (aiming && (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E)))
        {
            int dirRot = Input.GetKeyDown(KeyCode.Q) ? -1 : +1;
            TryRotate(dirRot);
        }

        // エイム中はゴースト更新（範囲NGやアンカー含みで赤表示）
        if (aiming) UpdateGhostVisual();
    }

    public void UI_RotateCW() { if (aiming) TryRotate(+1); }
    public void UI_RotateCCW() { if (aiming) TryRotate(-1); }
    public void UI_ToggleAreaSize() { ToggleAreaSize(); if (aiming) { BuildGhostTiles(); UpdateGhostVisual(); } }

    void ToggleAreaSize() { areaSize = (areaSize == 3) ? 5 : 3; }

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
            return true; // 盤外でも照準は可能（部分回転対応）;
        }
        return false;
    }

    bool IsCenterAllowed(Vector2Int c)
    {
        // プレイヤーからのチェビシェフ距離（8近傍距離）で判定
        int dx = Mathf.Abs(c.x - pos.x);
        int dy = Mathf.Abs(c.y - pos.y);
        int dist = Mathf.Max(dx, dy);
        return dist <= Mathf.Max(0, board.rotationCenterMaxDistance);
    }

    bool AreaContainsLocked(Vector2Int center, int size)
    {
        int k = (size - 1) / 2;
        for (int j = -k; j <= k; j++)
            for (int i = -k; i <= k; i++)
            {
                var p = new Vector2Int(center.x + i, center.y + j);
                if (!board.InBounds(p)) continue;
                if (board.cells[p.y, p.x] == CellType.Exit || board.cells[p.y, p.x] == CellType.Anchor)
                    return true;
            }
        return false;
    }

    void TryRotate(int dirRot)
    {
        // 中心距離チェック
        if (!IsCenterAllowed(aimCenter)) { UpdateGhostVisual(); return; }
        // アンカー/出口含みチェック
        if (AreaContainsLocked(aimCenter, areaSize)) { UpdateGhostVisual(); return; }

        // プレビュー確認
        var pv = board.GetPreview(aimCenter, areaSize, 0);
        if (!pv.valid) { UpdateGhostVisual(); return; }

        // ★ 方向固有：プレイヤーが敵に重なる回転は不発にする
        if (board.WouldPlayerOverlapGuard(aimCenter, areaSize, dirRot))
        {
            UpdateGhostVisual(); // 必要なら点滅等のフィードバックも可
            return;
        }

        // 実行
        board.RotateArea(aimCenter, areaSize, dirRot, () =>
        {
            // ★回転成功として登録
            var t = UnityCompat.FindFirst<TurnManager>();
            t?.RegisterRotation();

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
        if (ghostRoot == null) ghostRoot = new GameObject("Ghost");
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
                quad.transform.position = board.GridToWorld(new Vector2Int(aimCenter.x + i, aimCenter.y + j))
                                          + new Vector3(0, board.ghostY, 0);
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
        var pv = board.GetPreview(aimCenter, areaSize, 0);

        // 追加NG条件：距離オーバー or ロックセル含む
        bool centerOk = IsCenterAllowed(aimCenter);
        bool lockedInArea = AreaContainsLocked(aimCenter, areaSize);

        bool ok = pv.valid && centerOk && !lockedInArea;
        var mat = ok ? board.ghostOkMat : board.ghostNgMat;

        int idx = 0;
        for (int j = -k; j <= k; j++)
            for (int i = -k; i <= k; i++)
            {
                var tf = ghostRoot.transform.GetChild(idx++);
                tf.position = board.GridToWorld(new Vector2Int(aimCenter.x + i, aimCenter.y + j))
                              + new Vector3(0, board.ghostY, 0);
                var mr = tf.GetComponent<MeshRenderer>();
                if (mat != null) mr.material = mat;
            }
    }

    public void ClearGhost()
    {
        aiming = false;
        if (ghostRoot != null)
        {
            Destroy(ghostRoot);
            ghostRoot = null;
        }
    }

    public void SaveDevModeSettings()
    {
        PlayerPrefs.SetInt("player_invincible", invincible ? 1 : 0);
        PlayerPrefs.SetInt("player_areaSize", areaSize);
        PlayerPrefs.Save();
    }

    public void LoadDevModeSettings()
    {
        invincible = PlayerPrefs.GetInt("player_invincible", 0) == 1;
        areaSize = PlayerPrefs.GetInt("player_areaSize", 3); // 3はデフォルト
    }
}
