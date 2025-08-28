using System.Collections;
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

    // スムーズ移動
    bool isMoving = false;
    Vector3 moveFrom, moveTo;
    float moveT = 0f;
    float moveDur = 0.2f; // cellsPerSecから計算

    // UI から参照するためのプロパティ
    public bool IsAiming => aiming;

    [Header("Hold Move (長押し移動)")]
    public bool allowHoldMove = true;
    [Min(0.05f)] public float holdInitialDelay = 0.25f;
    [Min(0.03f)] public float holdRepeatInterval = 0.08f;
    Vector2Int holdDir = Vector2Int.zero;
    float holdNextTime = 0f;

    public void Init(BoardManager b, Vector2Int start)
    {
        board = b;
        pos = start;
        transform.position = board.GridToWorldActor(pos);
        turn = UnityCompat.FindFirst<TurnManager>();
    }

    void Update()
    {
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (turn != null && (turn.gameOver || turn.cleared)) return;

        // スムーズ移動更新
        if (board != null && board.smoothPlayerMove && isMoving)
        {
            if (board.IsAnimating) return; // 盤回転中は停止（破綻回避）

            moveT += Time.deltaTime / Mathf.Max(0.0001f, moveDur);
            float t = Mathf.Clamp01(moveT);
            transform.position = Vector3.Lerp(moveFrom, moveTo, t);

            if (t >= 1f)
            {
                isMoving = false;
                // 到達時にグリッド更新とイベント
                pos = board.WorldToGrid(moveTo);
                transform.position = board.GridToWorldActor(pos);

                board.TryPickupItemAt(pos);
                turn?.RegisterActionPoint();

                if (board.cells[pos.y, pos.x] == CellType.Exit)
                {
                    turn?.TriggerClear();
                }

                turn?.EndPlayerTurn();
            }
            return; // 補間中は他の入力無視
        }

        // 範囲切替・視界トグル・エイム開始/終了
        if (Input.mouseScrollDelta.y != 0f) { ToggleAreaSize(); if (aiming) { BuildGhostTiles(); UpdateGhostVisual(); } }
        if (Input.GetKeyDown(KeyCode.Alpha1)) { areaSize = 3; if (aiming) { BuildGhostTiles(); UpdateGhostVisual(); } }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { areaSize = 5; if (aiming) { BuildGhostTiles(); UpdateGhostVisual(); } }
        if (Input.GetKeyDown(KeyCode.V)) board.ToggleAllGuardVision();
        if (Input.GetMouseButtonDown(0)) { if (TryGetMouseGrid(out var g)) { aiming = true; aimCenter = g; ShowGhost(true); } }
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) { aiming = false; ShowGhost(false); }
        if (Input.GetKeyDown(KeyCode.T)) { aiming = false; ShowGhost(false); } // 自由回転ON時でもキャンセル専用

        // 長押し移動入力
        HandleMoveInput();

        // エイム中：Q/E で回転実行（自由回転OFF時のみ有効）
        if (aiming && (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E)))
        {
            if (board != null && !board.devEnableFreeRotate)
            {
                int dirRot = Input.GetKeyDown(KeyCode.Q) ? -1 : +1;
                TryRotate(dirRot);
            }
        }

        if (aiming) UpdateGhostVisual();
    }

    void HandleMoveInput()
    {
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) StartHold(Vector2Int.up);
        else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) StartHold(Vector2Int.down);
        else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) StartHold(Vector2Int.left);
        else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) StartHold(Vector2Int.right);

        if (holdDir != Vector2Int.zero)
        {
            bool upHeld = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
            bool downHeld = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
            bool leftHeld = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
            bool rightHeld = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);

            bool stillHeld =
                (holdDir == Vector2Int.up && upHeld) ||
                (holdDir == Vector2Int.down && downHeld) ||
                (holdDir == Vector2Int.left && leftHeld) ||
                (holdDir == Vector2Int.right && rightHeld);

            if (!stillHeld)
            {
                holdDir = Vector2Int.zero;
                return;
            }

            if (!allowHoldMove) return;
            if (board != null && board.IsAnimating) return;
            if (board != null && board.smoothPlayerMove && isMoving) return;

            if (Time.time >= holdNextTime)
            {
                if (TryMoveInDir(holdDir))
                {
                    holdNextTime = Time.time + holdRepeatInterval;
                }
                else
                {
                    holdNextTime = Time.time + holdRepeatInterval;
                }
            }
        }
    }

    void StartHold(Vector2Int dir)
    {
        holdDir = dir;
        TryMoveInDir(dir);
        holdNextTime = Time.time + holdInitialDelay;
    }

    bool TryMoveInDir(Vector2Int dir)
    {
        if (dir == Vector2Int.zero || board == null) return false;
        if (board.IsAnimating) return false;
        if (board.smoothPlayerMove && isMoving) return false;

        var np = pos + dir;
        if (!board.IsWalkable(np, true)) return false;

        if (board.smoothPlayerMove)
        {
            isMoving = true;
            moveFrom = transform.position;
            moveTo = board.GridToWorldActor(np);
            moveT = 0f;
            float cellsPerSec = Mathf.Max(0.1f, board.playerMoveCellsPerSec);
            moveDur = 1f / cellsPerSec;
        }
        else
        {
            pos = np;
            transform.position = board.GridToWorldActor(pos);
            board.TryPickupItemAt(pos);
            turn?.RegisterActionPoint();
            if (board.cells[pos.y, pos.x] == CellType.Exit) turn?.TriggerClear();
            turn?.EndPlayerTurn();
        }
        return true;
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
            return true;
        }
        return false;
    }

    bool IsCenterAllowed(Vector2Int c)
    {
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
        if (!IsCenterAllowed(aimCenter)) { UpdateGhostVisual(); return; }
        if (AreaContainsLocked(aimCenter, areaSize)) { UpdateGhostVisual(); return; }

        var pv = board.GetPreview(aimCenter, areaSize, 0);
        if (!pv.valid) { UpdateGhostVisual(); return; }

        if (board.WouldPlayerOverlapGuard(aimCenter, areaSize, dirRot))
        {
            UpdateGhostVisual();
            return;
        }

        board.RotateArea(aimCenter, areaSize, dirRot, () =>
        {
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
        // ルートを中心セルへ（高さはghostY）
        if (ghostRoot == null) ghostRoot = new GameObject("Ghost");
        ghostRoot.transform.position = board.GridToWorld(aimCenter) + new Vector3(0, board.ghostY, 0);
        ghostRoot.transform.rotation = Quaternion.identity;

        // いったん全削除
        for (int i = ghostRoot.transform.childCount - 1; i >= 0; i--)
            Destroy(ghostRoot.transform.GetChild(i).gameObject);

        int k = (areaSize - 1) / 2;
        for (int j = -k; j <= k; j++)
        {
            for (int i = -k; i <= k; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = $"Ghost_{i}_{j}";
                quad.transform.SetParent(ghostRoot.transform, false);

                // ローカル配置（中心セルからの相対 i,j）
                quad.transform.localPosition = new Vector3(i, 0f, j);
                quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                quad.transform.localScale = new Vector3(1f, 1f, 1f);

                var mr = quad.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                Destroy(quad.GetComponent<MeshCollider>());
            }
        }
    }

    void UpdateGhostVisual()
    {
        if (ghostRoot == null) return;

        // 中心に追従（子タイルはローカル配置のまま）
        ghostRoot.transform.position = board.GridToWorld(aimCenter) + new Vector3(0, board.ghostY, 0);

        var pv = board.GetPreview(aimCenter, areaSize, 0);
        bool centerOk = IsCenterAllowed(aimCenter);
        bool lockedInArea = AreaContainsLocked(aimCenter, areaSize);

        bool ok = pv.valid && centerOk && !lockedInArea;
        var mat = ok ? board.ghostOkMat : board.ghostNgMat;

        // 子の座標は触らず、マテリアルだけ更新
        if (mat != null)
        {
            var rends = ghostRoot.GetComponentsInChildren<MeshRenderer>(true);
            for (int idx = 0; idx < rends.Length; idx++)
                rends[idx].material = mat;
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
        areaSize = 3; // 初期値は必ず3×3
    }

    // ▼ 外部（GameUI/自由回転）からNxN Ghostを操作
    public void ShowGhostExtern(bool on, Vector2Int center, int size, bool ok)
    {
        this.aimCenter = center;
        this._areaSize = size;

        ShowGhost(on);
        if (on)
        {
            BuildGhostTiles();
            ApplyGhostMaterial(ok);
        }
    }

    public void UpdateGhostOkExtern(bool ok)
    {
        ApplyGhostMaterial(ok);
    }

    public void FlashNgGhostExtern(Vector2Int center, int size, float seconds)
    {
        StartCoroutine(CoFlashNgGhost(center, size, seconds));
    }

    IEnumerator CoFlashNgGhost(Vector2Int center, int size, float seconds)
    {
        ShowGhostExtern(true, center, size, false);
        yield return new WaitForSeconds(seconds);
        ClearGhost();
    }

    void ApplyGhostMaterial(bool ok)
    {
        if (ghostRoot == null) return;
        var mat = (ok ? board.ghostOkMat : board.ghostNgMat) ?? board.ghostOkMat;
        var rends = ghostRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] != null) rends[i].sharedMaterial = mat;
        }
    }

    // ▼ 追加: Ghostの親付け/解除（自由回転プレビューと同期回転させる）
    public void AttachGhostTo(Transform parent, bool worldPositionStays = true)
    {
        if (ghostRoot != null && parent != null)
            ghostRoot.transform.SetParent(parent, worldPositionStays);
    }
    public void DetachGhost(bool worldPositionStays = true)
    {
        if (ghostRoot != null)
            ghostRoot.transform.SetParent(null, worldPositionStays);
    }
}
