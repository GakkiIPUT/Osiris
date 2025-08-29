using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    [Tooltip("追従する対象。未設定なら自動検出（PlayerController）")]
    public Transform target;

    [Tooltip("マップ情報（クランプ用）。未設定なら自動検出（BoardManager）")]
    public BoardManager board;

    [Header("Follow")]
    [Tooltip("追従のスムーズさ（0で即追従）")]
    public float smoothTime = 0.15f;

    [Header("Clamp (Orthographic専用)")]
    public bool clampToBoard = true;
    [Tooltip("画面端の余白（ユニット）")]
    public Vector2 screenPadding = new Vector2(0.5f, 0.5f);

    Vector3 velocity;

    void Start()
    {
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();
        if (target == null)
        {
            var pl = UnityCompat.FindFirst<PlayerController>();
            if (pl != null) target = pl.transform;
        }
        // 初期位置をスナップ
        Snap();
    }

    void LateUpdate()
    {
        if (target == null)
        {
            var pl = UnityCompat.FindFirst<PlayerController>();
            if (pl != null) target = pl.transform;
            if (target == null) return;
        }

        Vector3 desired = GetDesiredPosition();
        transform.position = (smoothTime > 0f)
            ? Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime)
            : desired;
    }

    Vector3 GetDesiredPosition()
    {
        // 上から見下ろす前提：Yは現状維持、X/Zのみ追従
        Vector3 desired = new Vector3(target.position.x, transform.position.y, target.position.z);

        var cam = GetComponent<Camera>();
        if (clampToBoard && cam != null && cam.orthographic && board != null)
        {
            float halfH = cam.orthographicSize + screenPadding.y;
            float halfW = halfH * cam.aspect + screenPadding.x;

            // マップがカメラより小さい場合は中央固定
            float minX = halfW;
            float maxX = (board.Width - 1) - halfW;
            float minZ = halfH;
            float maxZ = (board.Height - 1) - halfH;

            if (minX <= maxX) desired.x = Mathf.Clamp(desired.x, minX, maxX);
            else desired.x = (board.Width - 1) * 0.5f;

            if (minZ <= maxZ) desired.z = Mathf.Clamp(desired.z, minZ, maxZ);
            else desired.z = (board.Height - 1) * 0.5f;
        }
        return desired;
    }

    public void Snap()
    {
        if (target == null) return;
        velocity = Vector3.zero;
        transform.position = GetDesiredPosition();
    }
}
