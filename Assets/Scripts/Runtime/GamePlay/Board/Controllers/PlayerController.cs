using UnityEngine;

#if ENABLE_INPUT_SYSTEM

using UnityEngine.InputSystem;

#endif

/// <summary>
/// プレイヤーの移動・回転操作、エイム中心の管理、ゴースト表示連携を担当するコントローラ。
/// 入力はキーボード/ゲームパッドの両方に対応し、スムーズ移動や長押し移動にも対応する。
/// </summary>
public class PlayerController : MonoBehaviour
{
    private BoardManager board;
    private TurnManager turn;

    /// <summary>現在のグリッド座標</summary>
    public Vector2Int pos;

    /// <summary>無敵モード（視認によるゲームオーバーを無効化）</summary>
    public bool invincible = false;

    /// <summary>
    /// 選択エリアのサイズ（常に3x3固定）。値を変更しても3に強制される。
    /// </summary>
    public int areaSize
    {
        get => _areaSize;
        set
        {
            _areaSize = 3;
            UpdateGhostVisual();
        }
    }

    private int _areaSize = 3;

    private bool aiming = true;
    private Vector2Int aimCenter;

    /// <summary>現在のエイム中心（FreeRotateController から参照）</summary>
    public Vector2Int AimCenter => aimCenter;

    private GameObject ghostRoot; // 互換のため残置（未使用）

    // スムーズ移動
    private bool isMoving = false;

    private Vector3 moveFrom, moveTo;
    private float moveT = 0f;
    private float moveDur = 0.2f;

    /// <summary>エイム状態（常時ON）</summary>
    public bool IsAiming => aiming;

    [Header("Hold Move (長押し移動)")]
    public bool allowHoldMove = true;

    [Min(0.05f)] public float holdInitialDelay = 0.25f;
    [Min(0.03f)] public float holdRepeatInterval = 0.08f;
    private Vector2Int holdDir = Vector2Int.zero;
    private float holdNextTime = 0f;

    [Header("Gamepad")]
    public bool enableGamepad = true;

    [Range(0.1f, 0.9f)] public float stickDigitalThreshold = 0.5f;
    public bool gamepadTogglesAim = true;

    [Header("Aim Move (Pad)")]
    public bool allowAimPadMove = true;

    [Min(0.05f)] public float aimInitialDelay = 0.25f;
    [Min(0.03f)] public float aimRepeatInterval = 0.08f;
    private Vector2Int aimHoldDir = Vector2Int.zero;
    private float aimHoldNextTime = 0f;

#if ENABLE_INPUT_SYSTEM
    private float _prevLT = 0f, _prevRT = 0f;
    private bool _ltDown = false, _rtDown = false;
    private const float _triggerEdge = 0.5f;
#endif

    // 効果音
    public AudioClip walkAudio;

    public AudioClip rotateAudio;
    public AudioClip goalAudio;
    public AudioClip gameoverAudio;
    private AudioSource audioSource;

    /// <summary>
    /// 初期化。ボード・ターン管理の参照を確立し、位置・エイム・表示を初期化する。
    /// </summary>
    public void Init(BoardManager b, Vector2Int start)
    {
        board = b;
        pos = start;
        transform.position = board.GridToWorldActor(pos);
        turn = UnityCompat.FindFirst<TurnManager>();

        audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();

        aiming = true;
        aimCenter = pos;

        UpdateGhostVisual();
    }

    /// <summary>
    /// 入力処理、スムーズ移動の更新、UI連携（視界トグル/中心移動/回転）を行う。
    /// </summary>
    private void Update()
    {
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (turn != null && (turn.gameOver || turn.cleared)) return;
        if (GlobalEscMenu.IsMenuOpen) return;
        if (GameFlow.TutorialOverlayOpen) return;

        UpdatePadState();

        // スムーズ移動
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

                // 移動完了時に中心をクランプ
                aimCenter = ClampAimCenterToOuter(aimCenter, pos);
                UpdateGhostVisual();

                turn?.RegisterActionPoint();
                if (board.cells[pos.y, pos.x] == CellType.Exit) turn?.TriggerClear();
                turn?.EndPlayerTurn();
            }
            return;
        }

        // キー Q/E で回転
        if (aiming && (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E)))
        {
            if (board != null)
            {
                int dirRot = Input.GetKeyDown(KeyCode.Q) ? -1 : +1;
                TryRotate(dirRot);
            }
        }

        if (Input.GetKeyDown(KeyCode.V)) board.ToggleAllGuardVision();

        // 左クリックでエイム中心変更（クランプ有り）
        if (Input.GetMouseButtonDown(0))
        {
            if (TryGetMouseGrid(out var g))
            {
                aimCenter = ClampAimCenterToOuter(g, pos);
                UpdateGhostVisual();
            }
        }

        HandleGamepadButtons();

        if (aiming)
        {
            HandleKeyboardMoveInput();
            HandleAimPadInput();
            UpdateGhostVisual();
        }
        else
        {
            HandleMoveInput();
        }
    }

    /// <summary>
    /// 非エイム時の移動入力（長押し対応）。
    /// </summary>
    private void HandleMoveInput()
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

    /// <summary>
    /// 長押し移動の開始（初回入力）と次回時刻のセット。
    /// </summary>
    private void StartHold(Vector2Int dir)
    {
        holdDir = dir;
        TryMoveInDir(dir);
        holdNextTime = Time.time + holdInitialDelay;
    }

    /// <summary>
    /// 指定方向へ1歩移動を試行する。スムーズ移動設定に応じて補間する。
    /// </summary>
    private bool TryMoveInDir(Vector2Int dir)
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

    /// <summary>UI からの回転（時計回り）</summary>
    public void UI_RotateCW()
    { if (aiming) TryRotate(+1); }

    /// <summary>UI からの回転（反時計回り）</summary>
    public void UI_RotateCCW()
    { if (aiming) TryRotate(-1); }

    /// <summary>UI からのエリアサイズ切替（常に3固定のため無効）</summary>
    public void UI_ToggleAreaSize()
    { /* no-op */ }

    private void ToggleAreaSize()
    { /* no-op */ }

    /// <summary>
    /// マウス座標からグリッド座標を取得する。
    /// </summary>
    private bool TryGetMouseGrid(out Vector2Int grid)
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

    /// <summary>
    /// エイム中心がプレイヤーからの距離制限以内か判定する（チェビシェフ距離）。
    /// </summary>
    private bool IsCenterAllowed(Vector2Int c)
    {
        int dx = Mathf.Abs(c.x - pos.x);
        int dy = Mathf.Abs(c.y - pos.y);
        int dist = Mathf.Max(dx, dy);
        return dist <= Mathf.Max(0, board.rotationCenterMaxDistance);
    }

    /// <summary>
    /// 回転エリア内に回転禁止セル（Exit/Anchor）が含まれるか。
    /// </summary>
    private bool AreaContainsLocked(Vector2Int center, int size)
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

    /// <summary>
    /// 回転を試行する。可否判定に通ればアニメ/確定し、ターンを進める。
    /// </summary>
    private void TryRotate(int dirRot)
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
            UpdateGhostVisual();
            turn.EndPlayerTurn();
            PlaySound(rotateAudio);
        });

        if (!started)
        {
            UpdateGhostVisual();
        }
    }

    // ====== ゴースト表示（Overlay に委譲） ======

    /// <summary>ゴースト表示切替（Overlay へ移行済みのため無効）</summary>
    private void ShowGhost(bool on)
    { /* no-op */ }

    private void BuildGhostTiles()
    { /* no-op */ }

    /// <summary>
    /// エイム中心の状態に応じて枠色（OK/NG）を更新する。
    /// </summary>
    private void UpdateGhostVisual()
    {
        if (board == null) return;
        var pv = board.GetPreview(AimCenter, areaSize, 0);
        bool centerOk = IsCenterAllowed(AimCenter);
        bool lockedInArea = AreaContainsLocked(AimCenter, areaSize);
        bool ok = pv.valid && centerOk && !lockedInArea;

        var overlay = board.GetComponent<SelectionFramesOverlay>();
        if (overlay != null) overlay.SetInnerOk(ok);
    }

    /// <summary>ゴースト消去（枠線表示へ移行のため無効）</summary>
    public void ClearGhost()
    { /* no-op */ }

    /// <summary>開発者設定を保存する。</summary>
    public void SaveDevModeSettings()
    {
        PlayerPrefs.SetInt("player_invincible", invincible ? 1 : 0);
        PlayerPrefs.SetInt("player_areaSize", areaSize);
        PlayerPrefs.Save();
    }

    /// <summary>開発者設定を読み込む。</summary>
    public void LoadDevModeSettings()
    {
        invincible = PlayerPrefs.GetInt("player_invincible", 0) == 1;
        areaSize = 3;
    }

    /// <summary>外部（FreeRotateController）からのゴースト表示更新（Overlay 中継）。</summary>
    public void ShowGhostExtern(bool on, Vector2Int center, int size, bool ok)
    {
        this.aimCenter = center;
        this._areaSize = 3;
        var overlay = board != null ? board.GetComponent<SelectionFramesOverlay>() : null;
        if (overlay != null) overlay.SetInnerOk(ok);
    }

    /// <summary>外部からのゴーストOK/NG更新（Overlay 中継）。</summary>
    public void UpdateGhostOkExtern(bool ok)
    {
        var overlay = board != null ? board.GetComponent<SelectionFramesOverlay>() : null;
        if (overlay != null) overlay.SetInnerOk(ok);
    }

    /// <summary>外部からのNGフラッシュ（Overlay 中継）。</summary>
    public void FlashNgGhostExtern(Vector2Int center, int size, float seconds)
    {
        var overlay = board != null ? board.GetComponent<SelectionFramesOverlay>() : null;
        if (overlay != null) overlay.FlashInnerNg(seconds);
    }

    public void AttachGhostTo(Transform parent, bool worldPositionStays = true)
    { /* no-op */ }

    public void DetachGhost(bool worldPositionStays = true)
    { /* no-op */ }

    /// <summary>
    /// ゲームパッドのボタン入力（視界トグル/回転）を処理する。
    /// </summary>
    private void HandleGamepadButtons()
    {
#if ENABLE_INPUT_SYSTEM
        if (!enableGamepad) return;
        var gp = Gamepad.current;
        if (gp == null) return;

        if (board != null && board.IsFreePreviewActive) return;

        // Lスティック押し込み＝視界トグル
        if (gp.leftStickButton.wasPressedThisFrame) board?.ToggleAllGuardVision();

        // 回転（右/左ショルダー）
        if (aiming)
        {
            if (gp.rightShoulder.wasPressedThisFrame) TryRotate(+1);
            if (gp.leftShoulder.wasPressedThisFrame) TryRotate(-1);
        }
#endif
    }

    /// <summary>
    /// エイム中心のゲームパッド操作（D-Padで移動、右スティックで中心移動）を処理する。
    /// </summary>
    private void HandleAimPadInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (!enableGamepad || !allowAimPadMove) return;
        if (board != null && (board.IsAnimating || board.IsFreePreviewActive)) return;

        var gp = Gamepad.current;
        if (gp == null) return;

        // D-Pad: プレイヤー移動（優先）
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

        // 右スティック: エイム中心移動（クランプ適用）
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

    /// <summary>
    /// エイム中心の長押し移動を開始する。
    /// </summary>
    private void AimStartHold(Vector2Int dir)
    {
        aimHoldDir = dir;
        MoveAimCenter(dir);
        aimHoldNextTime = Time.time + aimInitialDelay;
    }

    /// <summary>
    /// エイム中心を1マス移動（外枠・盤面境界にクランプ）。
    /// </summary>
    private void MoveAimCenter(Vector2Int dir)
    {
        if (board == null) return;
        var wanted = AimCenter + dir;
        wanted = ClampAimCenterToOuter(wanted, pos);
        if (!board.InBounds(wanted)) return;
        aimCenter = wanted;
        UpdateGhostVisual();
    }

    /// <summary>
    /// 左スティック相当のデジタル方向（上下左右）を算出する。
    /// </summary>
    private void GetPadDigitalDir(ref Vector2Int dir)
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

    /// <summary>
    /// エイム時のキーボード移動（WASD, 矢印）長押し処理。
    /// </summary>
    private void HandleKeyboardMoveInput()
    {
        if (board != null && board.IsFreePreviewActive)
        {
            holdDir = Vector2Int.zero;
            return;
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

            bool stillHeld =
                (holdDir == Vector2Int.up && upHeld) ||
                (holdDir == Vector2Int.down && downHeld) ||
                (holdDir == Vector2Int.left && leftHeld) ||
                (holdDir == Vector2Int.right && rightHeld);

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

    /// <summary>
    /// 左スティック＋D-Padの合成生値を取得する。
    /// </summary>
    private Vector2 GetPadMoveRaw()
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

    /// <summary>
    /// トリガーの立ち上がり検出など、ゲームパッド状態を更新する。
    /// </summary>
    private void UpdatePadState()
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

    /// <summary>
    /// AudioSource の用意と効果音のロード。
    /// </summary>
    private void Awake()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        walkAudio = Resources.Load<AudioClip>("Audio/walk");
        rotateAudio = Resources.Load<AudioClip>("Audio/rotate");
        goalAudio = Resources.Load<AudioClip>("Audio/goal");
        gameoverAudio = Resources.Load<AudioClip>("Audio/gameover");
    }

    /// <summary>
    /// 単発 SE を再生する。
    /// </summary>
    private void PlaySound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }

    /// <summary>
    /// エイム中心を外枠・盤面境界に収まるようクランプする（外枠R=3, 内枠3x3）。
    /// </summary>
    private Vector2Int ClampAimCenterToOuter(Vector2Int c, Vector2Int playerPos)
    {
        int k = (areaSize - 1) / 2; // =1
        int rout = k + 2;           // =3

        int minXByOuter = playerPos.x - (rout - k);
        int maxXByOuter = playerPos.x + (rout - k);
        int minYByOuter = playerPos.y - (rout - k);
        int maxYByOuter = playerPos.y + (rout - k);

        int cx = Mathf.Clamp(c.x, minXByOuter, maxXByOuter);
        int cy = Mathf.Clamp(c.y, minYByOuter, maxYByOuter);

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
