using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PlayerController : MonoBehaviour
{
    BoardManager board;
    TurnManager turn;

    public Vector2Int pos;

    public bool invincible = false; // ���G���[�h
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

    // 公開: FreeRotateController から参照
    public Vector2Int AimCenter => aimCenter;   

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

    [Header("Gamepad")]
    public bool enableGamepad = true;
    [Range(0.1f, 0.9f)] public float stickDigitalThreshold = 0.5f;
    public bool gamepadTogglesAim = true; // Southでトグル開始/終了

    // エイム中のPadカーソル移動（右スティック）
    [Header("Aim Move (Pad)")]
    public bool allowAimPadMove = true;
    [Min(0.05f)] public float aimInitialDelay = 0.25f;
    [Min(0.03f)] public float aimRepeatInterval = 0.08f;
    Vector2Int aimHoldDir = Vector2Int.zero;
    float aimHoldNextTime = 0f;

    // Padトリガーの立ち上がり検出用
#if ENABLE_INPUT_SYSTEM
    float _prevLT = 0f, _prevRT = 0f;
    bool _ltDown = false, _rtDown = false;
    const float _triggerEdge = 0.5f;
#endif


    //効果音
    public AudioClip walkAudio;
    public AudioClip rotateAudio;
    public AudioClip goalAudio;
    public AudioClip gameoverAudio;
    private AudioSource audioSource;

    public void Init(BoardManager b, Vector2Int start)
    {
        board = b;
        pos = start;
        transform.position = board.GridToWorldActor(pos);
        turn = UnityCompat.FindFirst<TurnManager>();
        // ▼AudioSourceの準備
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    void Update()
    {
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (turn != null && (turn.gameOver || turn.cleared)) return;
        if (GlobalEscMenu.IsMenuOpen) return;
        UpdatePadState();

        // �X���[�Y�ړ��X�V
        if (board != null && board.smoothPlayerMove && isMoving)
        {
            if (board.IsAnimating) return;
            moveT += Time.deltaTime / Mathf.Max(0.0001f, moveDur);
            float t = Mathf.Clamp01(moveT);
            transform.position = Vector3.Lerp(moveFrom, moveTo, t);
            if (t >= 1f)
            {
                isMoving = false;
                pos = board.WorldToGrid(moveTo);
                transform.position = board.GridToWorldActor(pos);
                board.TryPickupItemAt(pos);
                turn?.RegisterActionPoint();
                if (board.cells[pos.y, pos.x] == CellType.Exit) turn?.TriggerClear();
                turn?.EndPlayerTurn();
            }
            return;
        }

        // キーマウ: QE回転は自由回転ON/OFFに関係なく有効（両立）
        if (aiming && (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E)))
        {
            if (board != null)
            {
                int dirRot = Input.GetKeyDown(KeyCode.Q) ? -1 : +1;
                TryRotate(dirRot);
            }
        }

        // 既存のキーマウ入力
        if (Input.mouseScrollDelta.y != 0f) { ToggleAreaSize(); if (aiming) { BuildGhostTiles(); UpdateGhostVisual(); } }
        if (Input.GetKeyDown(KeyCode.Alpha1)) { areaSize = 3; if (aiming) { BuildGhostTiles(); UpdateGhostVisual(); } }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { areaSize = 5; if (aiming) { BuildGhostTiles(); UpdateGhostVisual(); } }
        if (Input.GetKeyDown(KeyCode.V)) board.ToggleAllGuardVision();
        if (Input.GetMouseButtonDown(0)) { if (TryGetMouseGrid(out var g)) { aiming = true; aimCenter = g; ShowGhost(true); } }
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) { aiming = false; ShowGhost(false); }
        if (Input.GetKeyDown(KeyCode.T)) { aiming = false; ShowGhost(false); }

        HandleGamepadButtons();

        if (aiming) { HandleAimPadInput(); UpdateGhostVisual(); }
        else { HandleMoveInput(); }
    }

    void HandleMoveInput()
    {
        // 追加: 回転プレビュー中は移動入力を無効化
        if (board != null && board.IsFreePreviewActive)
        {
            holdDir = Vector2Int.zero;
            return;
        }

        // ========= Padのデジタル化（左スティック / D-Pad） =========
        Vector2Int padDir = Vector2Int.zero;
        GetPadDigitalDir(ref padDir);
        if (padDir != Vector2Int.zero)
        {
            if (holdDir == Vector2Int.zero || padDir != holdDir)
            {
                StartHold(padDir);
            }
        }

        // ========= キーボード =========
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) StartHold(Vector2Int.up);
        else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) StartHold(Vector2Int.down);
        else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) StartHold(Vector2Int.left);
        else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) StartHold(Vector2Int.right);

        if (holdDir != Vector2Int.zero)
        {
            // KB held
            bool upHeld = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
            bool downHeld = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
            bool leftHeld = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
            bool rightHeld = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);

            // Pad held
            bool upHeldPad = false, downHeldPad = false, leftHeldPad = false, rightHeldPad = false;
            if (enableGamepad)
            {
                Vector2 v = GetPadMoveRaw();
                upHeldPad = v.y >= stickDigitalThreshold;
                downHeldPad = v.y <= -stickDigitalThreshold;
                leftHeldPad = v.x <= -stickDigitalThreshold;
                rightHeldPad = v.x >= stickDigitalThreshold;
            }

            bool stillHeld =
                (holdDir == Vector2Int.up && (upHeld || upHeldPad)) ||
                (holdDir == Vector2Int.down && (downHeld || downHeldPad)) ||
                (holdDir == Vector2Int.left && (leftHeld || leftHeldPad)) ||
                (holdDir == Vector2Int.right && (rightHeld || rightHeldPad));

            if (!stillHeld) { holdDir = Vector2Int.zero; return; }

            if (!allowHoldMove) return;
            if (board != null && board.IsAnimating) return;
            if (board != null && board.smoothPlayerMove && isMoving) return;

            if (Time.time >= holdNextTime)
            {
                if (TryMoveInDir(holdDir))
                    holdNextTime = Time.time + holdRepeatInterval;
                else
                    holdNextTime = Time.time + holdRepeatInterval;
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
        if (board.IsFreePreviewActive) return false;
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
        
        PlaySound(walkAudio);
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
        if (!IsCenterAllowed(AimCenter)) { UpdateGhostVisual(); return; }
        if (AreaContainsLocked(AimCenter, areaSize)) { UpdateGhostVisual(); return; }

        var pv = board.GetPreview(AimCenter, areaSize, 0);
        if (!pv.valid) { UpdateGhostVisual(); return; }

        if (board.WouldPlayerOverlapGuard(AimCenter, areaSize, dirRot))
        {
            UpdateGhostVisual(); return;
        }

        board.RotateArea(AimCenter, areaSize, dirRot, () =>
        {
            var t = UnityCompat.FindFirst<TurnManager>();
            t?.RegisterRotation();

            aiming = false;
            ShowGhost(false);
            turn.EndPlayerTurn();


            PlaySound(rotateAudio);
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
        ghostRoot.transform.position = board.GridToWorld(AimCenter) + new Vector3(0, board.ghostY, 0);

        var pv = board.GetPreview(AimCenter, areaSize, 0);
        bool centerOk = IsCenterAllowed(AimCenter);
        bool lockedInArea = AreaContainsLocked(AimCenter, areaSize);

        bool ok = pv.valid && centerOk && !lockedInArea;
        var mat = ok ? board.ghostOkMat : board.ghostNgMat;

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
        if (ghostRoot != null) { Destroy(ghostRoot); ghostRoot = null; }
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
        areaSize = 3;
    }

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
            if (rends[i] != null) rends[i].sharedMaterial = mat;
    }

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

    void HandleGamepadButtons()
    {
#if ENABLE_INPUT_SYSTEM
        if (!enableGamepad) return;
        var gp = Gamepad.current;
        if (gp == null) return;

        // R2＝サイズ切替（プレビュー中も許可）
        if (_rtDown)
        {
            ToggleAreaSize();
            if (aiming) { BuildGhostTiles(); UpdateGhostVisual(); }
        }

        // 自由回転プレビュー中はここで打ち切り（QE/LRなどは衝突回避のため無効化）
        if (board != null && board.IsFreePreviewActive) return;

        // 南ボタン＝選択トグル
        if (gamepadTogglesAim && gp.buttonSouth.wasPressedThisFrame)
        {
            if (!aiming) { aiming = true; aimCenter = pos; ShowGhost(true); }
            else { aiming = false; ShowGhost(false); }
        }

        // 東ボタン＝選択解除
        if (gp.buttonEast.wasPressedThisFrame)
        {
            aiming = false; ShowGhost(false);
        }

        // Lスティック押し込み＝視界トグル
        if (gp.leftStickButton.wasPressedThisFrame) board?.ToggleAllGuardVision();

        // QE回転（devEnableFreeRotateに関係なく有効＝両立）
        if (aiming)
        {
            if (gp.rightShoulder.wasPressedThisFrame) TryRotate(+1);
            if (gp.leftShoulder.wasPressedThisFrame) TryRotate(-1);
        }
#endif
    }

    // Pad: 選択中の範囲移動（D-Padのみ）。FreePreview中は移動不可（IsFreePreviewActiveで抑止）
    // Pad: 選択中の範囲移動（右スティック優先＋D-Pad）。FreePreview中は移動不可
    // Pad: 選択中の入力
    // - D-Pad＝キャラクター移動（選択は維持）。回転プレビュー中は無効。
    // - 右スティック＝選択範囲移動（ホールドリピート）。回転プレビュー中は無効。
    // 左スティックは自由回転（FreeRotateController側）に使用。
    void HandleAimPadInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (!enableGamepad || !allowAimPadMove) return;
        // 回転プレビュー中は一切の移動不可
        if (board != null && (board.IsAnimating || board.IsFreePreviewActive)) return;

        var gp = Gamepad.current;
        if (gp == null) return;

        // 1) D-Pad でキャラクターを移動（最優先、選択は維持）
        Vector2 dv = gp.dpad.ReadValue();
        if (Mathf.Abs(dv.x) > 0.5f || Mathf.Abs(dv.y) > 0.5f)
        {
            Vector2Int moveDir = Mathf.Abs(dv.x) > Mathf.Abs(dv.y)
                ? (dv.x > 0f ? Vector2Int.right : Vector2Int.left)
                : (dv.y > 0f ? Vector2Int.up : Vector2Int.down);

            // 通常移動のホールド機構を流用
            if (holdDir == Vector2Int.zero || moveDir != holdDir)
            {
                StartHold(moveDir); // 1歩動く＋holdNextTimeセット
            }
            else
            {
                // D-Pad長押しリピート
                Vector2 dv2 = gp.dpad.ReadValue();
                bool held =
                    (holdDir == Vector2Int.up && dv2.y > 0.5f) ||
                    (holdDir == Vector2Int.down && dv2.y < -0.5f) ||
                    (holdDir == Vector2Int.left && dv2.x < -0.5f) ||
                    (holdDir == Vector2Int.right && dv2.x > 0.5f);

                if (!held)
                {
                    holdDir = Vector2Int.zero;
                }
                else if (Time.time >= holdNextTime)
                {
                    TryMoveInDir(holdDir);
                    holdNextTime = Time.time + holdRepeatInterval;
                }
            }
            // D-Padを最優先で処理するため、このフレームはここで終了
            return;
        }

        // 2) 右スティックで選択範囲（aimCenter）移動（デジタル化＋ホールド）
        Vector2 rs = gp.rightStick.ReadValue();
        Vector2Int dir = Vector2Int.zero;
        if (Mathf.Abs(rs.x) >= stickDigitalThreshold || Mathf.Abs(rs.y) >= stickDigitalThreshold)
        {
            if (Mathf.Abs(rs.x) > Mathf.Abs(rs.y))
                dir = (rs.x > 0f) ? Vector2Int.right : Vector2Int.left;
            else
                dir = (rs.y > 0f) ? Vector2Int.up : Vector2Int.down;
        }

        if (dir != Vector2Int.zero)
        {
            if (aimHoldDir == Vector2Int.zero || dir != aimHoldDir)
            {
                AimStartHold(dir);
            }
        }

        if (aimHoldDir != Vector2Int.zero)
        {
            // 右スティック長押し判定
            Vector2 rsv = gp.rightStick.ReadValue();
            bool held =
                (aimHoldDir == Vector2Int.up && rsv.y >= stickDigitalThreshold) ||
                (aimHoldDir == Vector2Int.down && rsv.y <= -stickDigitalThreshold) ||
                (aimHoldDir == Vector2Int.left && rsv.x <= -stickDigitalThreshold) ||
                (aimHoldDir == Vector2Int.right && rsv.x >= stickDigitalThreshold);

            if (!held)
            {
                aimHoldDir = Vector2Int.zero;
                return;
            }

            if (Time.time >= aimHoldNextTime)
            {
                MoveAimCenter(aimHoldDir);
                aimHoldNextTime = Time.time + aimRepeatInterval;
            }
        }
#endif
    }
    void AimStartHold(Vector2Int dir)
    {
        aimHoldDir = dir;
        MoveAimCenter(dir);
        aimHoldNextTime = Time.time + aimInitialDelay;
    }

    void MoveAimCenter(Vector2Int dir)
    {
        if (board == null) return;
        var next = AimCenter + dir;
        if (!board.InBounds(next)) return;
        aimCenter = next;
        UpdateGhostVisual();
    }

    void GetPadDigitalDir(ref Vector2Int dir)
    {
        dir = Vector2Int.zero;
        if (!enableGamepad) return;
        Vector2 v = GetPadMoveRaw();
        if (Mathf.Abs(v.x) < stickDigitalThreshold && Mathf.Abs(v.y) < stickDigitalThreshold) return;
        if (Mathf.Abs(v.x) > Mathf.Abs(v.y))
            dir = (v.x > 0f) ? Vector2Int.right : Vector2Int.left;
        else
            dir = (v.y > 0f) ? Vector2Int.up : Vector2Int.down;
    }

    Vector2 GetPadMoveRaw()
    {
#if ENABLE_INPUT_SYSTEM
        if (!enableGamepad) return Vector2.zero;
        var gp = Gamepad.current;
        if (gp == null) return Vector2.zero;

        Vector2 v = gp.leftStick.ReadValue();
        v += gp.dpad.ReadValue();
        if (v.sqrMagnitude > 1f) v.Normalize();
        return v;
#else
        return Vector2.zero;
#endif
    }

    void UpdatePadState()
    {
#if ENABLE_INPUT_SYSTEM
        _ltDown = _rtDown = false;
        var gp = Gamepad.current;
        if (gp == null) return;

        float lt = gp.leftTrigger.ReadValue();
        float rt = gp.rightTrigger.ReadValue();
        _ltDown = (lt >= _triggerEdge && _prevLT < _triggerEdge);
        _rtDown = (rt >= _triggerEdge && _prevRT < _triggerEdge);
        _prevLT = lt;
        _prevRT = rt;
#endif
    }
    private void Awake()
    {
        // AudioSource を追加
        audioSource = gameObject.AddComponent<AudioSource>();

        // Resources からロード
        walkAudio = Resources.Load<AudioClip>("Audio/walk");
        rotateAudio = Resources.Load<AudioClip>("Audio/rotate");
        goalAudio = Resources.Load<AudioClip>("Audio/goal");
        gameoverAudio = Resources.Load<AudioClip>("Audio/gameover");
    }
    private void PlaySound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }
}
