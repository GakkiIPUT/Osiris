using System.Linq;
using UnityEngine;

public class StageManager : MonoBehaviour
{
    public StageSet stageSet;
    public int currentIndex = 0;
    public BoardManager board;

    void Awake()
    {
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();
        if (stageSet != null) Load(currentIndex);
    }

    public void Load(int index)
    {
        if (stageSet == null || stageSet.stages == null || stageSet.stages.Count == 0) return;
        currentIndex = Mathf.Clamp(index, 0, stageSet.stages.Count - 1);

        var entry = stageSet.stages[currentIndex];
        var rows = ParseAscii(entry.asciiLevel);
        if (rows != null && rows.Length > 0)
        {
            board.SetLevel(rows); // BoardManager‚ªBuild‚Ü‚Å–Ê“|‚ðŒ©‚Ü‚·
        }
    }

    public void ReloadCurrent() => Load(currentIndex);
    public void Next() => Load(currentIndex + 1);
    public void Prev() => Load(currentIndex - 1);

    public string GetDisplayName()
    {
        if (stageSet == null || stageSet.stages == null || stageSet.stages.Count == 0) return "-";
        return stageSet.stages[Mathf.Clamp(currentIndex, 0, stageSet.stages.Count - 1)].displayName;
    }

    public string[] GetAllNames()
    {
        if (stageSet == null || stageSet.stages == null) return new string[0];
        return stageSet.stages.Select(s => s.displayName).ToArray();
    }

    static string[] ParseAscii(TextAsset ta)
    {
        if (ta == null) return null;
        var lines = ta.text
            .Replace("\r", "")
            .Split('\n')
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        return lines;
    }
}
