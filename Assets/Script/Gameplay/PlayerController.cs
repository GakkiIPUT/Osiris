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

    public bool invincible = false; // 無敵モード
    public int areaSize
    {
        get => _areaSize;
        set
        {
            // 常に3×3固定
            _areaSize = 3;
            // 見た目更新のみ（Overlay側が追従）
            UpdateGhostVisual();
        }
    }
    private int _areaSize = 3;

    bool aiming = true;           // 常時ON
    Vector2Int aimCenter;

    // 公開: FreeRotateController から参照
    public Vector2Int AimCenter => aimCenter;   

    GameObject ghostRoot; // 互換のため残置（未使用）

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
    public bool gamepadTogglesAim = true; // 常時選択化では無効

    // エイム中のPadカーソル移動（右スティック）
    [Header("Aim Move (Pad)")]
    public bool allowAimPadMove = true;
    [Min(0.05f)] public float aimInitialDelay = 0.25f;
    [Min(0.03f)] public float aimRepeatInterval = 0.08f;
    Vector2Int aimHoldDir = Vector2Int.zero;
    float aimHoldNextTime = 0f;

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

        // 常時選択ON（初期中心はプレイヤー位置）
        aiming = true;
        aimCenter = pos;

        // 枠線表示はOverlay（自動アタッチ）側が担当
        UpdateGhostVisual();
    }

    void Update()
    {
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (turn != null && (turn.gameOver || turn.cleared)) return;
        if (GlobalEscMenu.IsMenuOpen) return;
        if (GameFlow.TutorialOverlayOpen) return;

        UpdatePadState();

        // スムーズ移動更新
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

                // プレイヤー移動完了時：内枠中心を外枠（プレイヤー中心R=3）内へクランプ
                aimCenter = ClampAimCenterToOuter(aimCenter, pos);
                UpdateGhostVisual();

                turn?.RegisterActionPoint();
                if (board.cells[pos.y, pos.x] == CellType.Exit) turn?.TriggerClear();
                turn?.EndPlayerTurn();
            }
            return;
        }

        // キーマウ: QE回転
        if (aiming && (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E)))
        {
            if (board != null)
            {
                int dirRot = Input.GetKeyDown(KeyCode.Q) ? -1 : +1;
                TryRotate(dirRot);
            }
        }

        if (Input.GetKeyDown(KeyCode.V)) board.ToggleAllGuardVision();

        // 左クリックで中心変更（クランプ適用）
        if (Input.GetMouseButtonDown(0))
        {
            if (TryGetMouseGrid(out var g))
            {
                aimCenter = ClampAimCenterToOuter(g, pos);
                UpdateGhostVisual();
            }
        }

        // 解除入力は無効化（常時選択）

        HandleGamepadButtons();

        if (aiming) { HandleAimPadInput(); UpdateGhostVisual(); }
        else { HandleMoveInput(); }
    }

    void HandleMoveInput()
    {
        if (board != null && board.IsFreePreviewActive)
        {
            holdDir = Vector2Int.zero;
            return;
        }

        Vector2Int padDir = Vector2Int.zero;
        GetPadDigitalDir(ref padDir);
        if (padDir != Vector2Int.zero)
        {
            if (holdDir == Vector2Int.zero || padDir != holdDir)
            {
                StartHold(padDir);
            }
        }

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

            // 即時移動でも内枠中心をクランプ
            aimCenter = ClampAimCenterToOuter(aimCenter, pos);
            UpdateGhostVisual();

            turn?.RegisterActionPoint();
            if (board.cells[pos.y, pos.x] == CellType.Exit) turn?.TriggerClear();
            turn?.EndPlayerTurn();
        }
        
        PlaySound(walkAudio);
        return true;
    }

    public void UI_RotateCW() { if (aiming) TryRotate(+1); }
    public void UI_RotateCCW() { if (aiming) TryRotate(-1); }
    public void UI_ToggleAreaSize() { /* 無効化（常に3） */ }
    void ToggleAreaSize() { /* 無効化（常に3） */ }

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

    // 回転実行（成功時も選択は維持）
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

        bool started = board.TryRotateArea(AimCenter, areaSize, dirRot, () =>
        {
            var t = UnityCompat.FindFirst<TurnManager>();
            t?.RegisterRotation();
            UpdateGhostVisual(); // 見た目更新（色だけ）
            turn.EndPlayerTurn();
            PlaySound(rotateAudio);
        });

        if (!started)
        {
            UpdateGhostVisual();
        }
    }

    // ====== Ghost系はNO-OPにしてOverlayへ委譲 ======
    void ShowGhost(bool on) { /* NO-OP（枠線表示に統一） */ }
    void BuildGhostTiles() { /* NO-OP */ }

    void UpdateGhostVisual()
    {
        if (board == null) return;
        var pv = board.GetPreview(AimCenter, areaSize, 0);
        bool centerOk = IsCenterAllowed(AimCenter);
        bool lockedInArea = AreaContainsLocked(AimCenter, areaSize);
        bool ok = pv.valid && centerOk && !lockedInArea;

        var overlay = board.GetComponent<SelectionFramesOverlay>();
        if (overlay != null) overlay.SetInnerOk(ok);
    }

    public void ClearGhost() { /* NO-OP（選択は常時表示） */ }

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

    // FreeRotateController からの外部呼び出しはOverlayへ中継
    public void ShowGhostExtern(bool on, Vector2Int center, int size, bool ok)
    {
        this.aimCenter = center;
        this._areaSize = 3;
        var overlay = board != null ? board.GetComponent<SelectionFramesOverlay>() : null;
        if (overlay != null) overlay.SetInnerOk(ok);
    }

    public void UpdateGhostOkExtern(bool ok)
    {
        var overlay = board != null ? board.GetComponent<SelectionFramesOverlay>() : null;
        if (overlay != null) overlay.SetInnerOk(ok);
    }

    public void FlashNgGhostExtern(Vector2Int center, int size, float seconds)
    {
        var overlay = board != null ? board.GetComponent<SelectionFramesOverlay>() : null;
        if (overlay != null) overlay.FlashInnerNg(seconds);
    }

    public void AttachGhostTo(Transform parent, bool worldPositionStays = true) { /* NO-OP */ }
    public void DetachGhost(bool worldPositionStays = true) { /* NO-OP */ }

    void HandleGamepadButtons()
    {
#if ENABLE_INPUT_SYSTEM
        if (!enableGamepad) return;
        var gp = Gamepad.current;
        if (gp == null) return;

        // R2＝サイズ切替（無効化）

        // 自由回転プレビュー中はここで打ち切り
        if (board != null && board.IsFreePreviewActive) return;

        // 南/東ボタンの選択トグル・解除は無効

        // Lスティック押し込み＝視界トグル
        if (gp.leftStickButton.wasPressedThisFrame) board?.ToggleAllGuardVision();

        // QE回転（右/左ショルダー）
        if (aiming)
        {
            if (gp.rightShoulder.wasPressedThisFrame) TryRotate(+1);
            if (gp.leftShoulder.wasPressedThisFrame) TryRotate(-1);
        }
#endif
    }

    // Pad: 選択中の範囲移動（右スティック）…クランプ適用
    void HandleAimPadInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (!enableGamepad || !allowAimPadMove) return;
        if (board != null && (board.IsAnimating || board.IsFreePreviewActive)) return;

        var gp = Gamepad.current;
        if (gp == null) return;

        // 1) D-Pad でキャラクターを移動（最優先）
        Vector2 dv = gp.dpad.ReadValue();
        if (Mathf.Abs(dv.x) > 0.5f || Mathf.Abs(dv.y) > 0.5f)
        {
            Vector2Int moveDir = Mathf.Abs(dv.x) > Mathf.Abs(dv.y)
                ? (dv.x > 0f ? Vector2Int.right : Vector2Int.left)
                : (dv.y > 0f ? Vector2Int.up : Vector2Int.down);

            if (holdDir == Vector2Int.zero || moveDir != holdDir)
            {
                StartHold(moveDir);
            }
            else
            {
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
            return;
        }

        // 2) 右スティックで選択範囲（aimCenter）移動（クランプ適用）
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

    // 内枠中心の移動にもクランプを適用
    void MoveAimCenter(Vector2Int dir)
    {
        if (board == null) return;
        var wanted = AimCenter + dir;
        wanted = ClampAimCenterToOuter(wanted, pos);
        if (!board.InBounds(wanted)) return;
        aimCenter = wanted;
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
        audioSource = gameObject.AddComponent<AudioSource>();
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

    // ====== 内枠クランプ（外枠=プレイヤー中心R=3、内枠=3×3,k=1） ======
    Vector2Int ClampAimCenterToOuter(Vector2Int c, Vector2Int playerPos)
    {
        int k = (areaSize - 1) / 2; // =1
        int rout = k + 2;           // =3
        // 1) プレイヤー中心から±(rout - k) = ±2 にクランプ（中心Cの許容範囲）
        int minXByOuter = playerPos.x - (rout - k);
        int maxXByOuter = playerPos.x + (rout - k);
        int minYByOuter = playerPos.y - (rout - k);
        int maxYByOuter = playerPos.y + (rout - k);

        int cx = Mathf.Clamp(c.x, minXByOuter, maxXByOuter);
        int cy = Mathf.Clamp(c.y, minYByOuter, maxYByOuter);

        // 2) 内枠3×3が盤外に出ないよう、盤面境界でもクランプ
        if (board != null)
        {
            int minX = k;
            int maxX = board.Width - 1 - k;
            int minY = k;
            int maxY = board.Height - 1 - k;
            cx = Mathf.Clamp(cx, minX, maxX);
            cy = Mathf.Clamp(cy, minY, maxY);
        }
        return new Vector2Int(cx, cy);
    }
}
