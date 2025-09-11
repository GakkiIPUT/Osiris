using UnityEngine;

public partial class BoardManager
{
    /// <summary>
    /// 指定セルのアイテムを取得し、UI/進捗を更新する。
    /// </summary>
    public bool TryPickupItemAt(Vector2Int p)
    {
        if (itemAt.TryGetValue(p, out var t) && t.go != null)
        {
            if (t.sym == 'd' || t.sym == 'e') return false;
            itemAt.Remove(p);
            SafeDestroy(t.go);

            var turn = UnityCompat.FindFirst<TurnManager>();
            if (turn != null)
            {
                if (t.sym == 'i')
                {
                    turn.OnItemPicked('i');
                }
                else if (t.sym == 't')
                {
                    turn.OnTreasurePicked();
                }
            }

            return true;
        }
        return false;
    }

    /// <summary>
    /// 指定セルに泥棒（d/e）がいるか。
    /// </summary>
    public bool IsThiefAt(Vector2Int p)
    {
        return itemAt.TryGetValue(p, out var t) && (t.sym == 'd' || t.sym == 'e');
    }

    /// <summary>
    /// 泥棒（d）を宝箱（t）に変換する。
    /// </summary>
    public bool TransformThiefToTreasureAt(Vector2Int p)
    {
        if (!itemAt.TryGetValue(p, out var t) || t.sym != 'd') return false;

        if (t.go) SafeDestroy(t.go);
        itemAt.Remove(p);

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

        itemAt[p] = ('t', chestGo);
        return true;
    }

    /// <summary>
    /// 鍵泥棒（e）を鍵（i）に変換する。
    /// </summary>
    public bool TransformThiefToKeyAt(Vector2Int p)
    {
        if (!itemAt.TryGetValue(p, out var t) || t.sym != 'e') return false;

        if (t.go) SafeDestroy(t.go);
        itemAt.Remove(p);

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

        itemAt[p] = ('i', keyGo);
        return true;
    }

    /// <summary>
    /// アイテム記号ごとの見た目スケールを返す。
    /// </summary>
    private Vector2 GetItemVisualScaleBySymbol(char sym)
    {
        if (sym == 'd') return new Vector2(0.2f, 0.2f);
        if (sym == 'e') return new Vector2(0.25f, 0.25f);
        return new Vector2(0.07f, 0.07f);
    }
}
