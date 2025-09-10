using UnityEngine;

public partial class BoardManager
{
    // プレイヤーが p を踏んだときに呼ぶ（アイテム取得してUI更新）
    public bool TryPickupItemAt(Vector2Int p)
    {
        if (itemAt.TryGetValue(p, out var t) && t.go != null)
        {
            // 泥棒は取得不可（そもそも侵入できない想定）
            if (t.sym == 'd' || t.sym == 'e') return false;
            itemAt.Remove(p);
            SafeDestroy(t.go); // エディタ/実行の両対応破棄

            // ★ TurnManagerへ通知
            var turn = UnityCompat.FindFirst<TurnManager>();
            if (turn != null)
            {
                if (t.sym == 'i')
                {
                    // 鍵だけを必須アイテムとして扱う
                    turn.OnItemPicked('i');
                }
                else if (t.sym == 't')
                {
                    // 宝箱（コレクション）
                    turn.OnTreasurePicked();
                }
            }

            return true;
        }
        return false;
    }

    // ===== 泥棒ユーティリティ =====
    public bool IsThiefAt(Vector2Int p)
    {
        return itemAt.TryGetValue(p, out var t) && (t.sym == 'd' || t.sym == 'e');
    }

    public bool TransformThiefToTreasureAt(Vector2Int p)
    {
        if (!itemAt.TryGetValue(p, out var t) || t.sym != 'd') return false;

        // 泥棒見た目を消す
        if (t.go) SafeDestroy(t.go);
        itemAt.Remove(p);

        // 宝箱プレハブを検索
        GameObject chestPf = null;
        for (int i = 0; i < itemTypes.Count; i++)
        {
            if (!string.IsNullOrEmpty(itemTypes[i].symbol) && itemTypes[i].symbol[0] == 't')
            {
                chestPf = itemTypes[i].prefab;
                break;
            }
        }

        GameObject chestGo = null;
        if (chestPf != null)
        {
            chestGo = Instantiate(chestPf, GridToWorld(p), Quaternion.identity, itemsRoot);
            chestGo.name = $"Item_{p.x}_{p.y}_t";
            AutoAlign2DObject(chestGo, true, GetItemVisualScaleBySymbol('t'));
        }
        else
        {
            Debug.LogWarning("[Thief] Treasure prefab for symbol 't' is not assigned in BoardManager.itemTypes.");
        }

        // 位置 → 宝箱を登録
        itemAt[p] = ('t', chestGo);
        return true;
    }

    // 鍵泥棒（e）→ 鍵（i）に変換
    public bool TransformThiefToKeyAt(Vector2Int p)
    {
        if (!itemAt.TryGetValue(p, out var t) || t.sym != 'e') return false;

        // 泥棒見た目を消す
        if (t.go) SafeDestroy(t.go);
        itemAt.Remove(p);

        // 鍵プレハブを検索
        GameObject keyPf = null;
        for (int i = 0; i < itemTypes.Count; i++)
        {
            if (!string.IsNullOrEmpty(itemTypes[i].symbol) && itemTypes[i].symbol[0] == 'i')
            {
                keyPf = itemTypes[i].prefab;
                break;
            }
        }

        GameObject keyGo = null;
        if (keyPf != null)
        {
            keyGo = Instantiate(keyPf, GridToWorld(p), Quaternion.identity, itemsRoot);
            keyGo.name = $"Item_{p.x}_{p.y}_i";
            AutoAlign2DObject(keyGo, true, GetItemVisualScaleBySymbol('i'));
        }
        else
        {
            Debug.LogWarning("[Thief] Key prefab for symbol 'i' is not assigned in BoardManager.itemTypes.");
        }

        // 位置 → 鍵を登録（UIの必須進捗は拾得時に更新）
        itemAt[p] = ('i', keyGo);
        return true;
    }

    // 見た目スケール（アイテム記号別）
    private Vector2 GetItemVisualScaleBySymbol(char sym)
    {
        // 泥棒だけ1セルサイズ、その他は従来の小さめ表示
        if (sym == 'd') return new Vector2(0.2f, 0.2f);
        if (sym == 'e') return new Vector2(0.25f, 0.25f);
        return new Vector2(0.07f, 0.07f);
    }
}