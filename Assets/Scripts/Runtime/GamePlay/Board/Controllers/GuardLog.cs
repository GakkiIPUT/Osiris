#if UNITY_EDITOR || DEVELOPMENT_BUILD
#define GUARD_DEBUG
#endif

using System.Diagnostics;

namespace GamePlay.Board.Controllers
{
    internal static class GuardLog
    {
        [Conditional("GUARD_DEBUG")]
        public static void Log(string msg) => UnityEngine.Debug.Log(msg);

        [Conditional("GUARD_DEBUG")]
        public static void Warn(string msg) => UnityEngine.Debug.LogWarning(msg);

        [Conditional("GUARD_DEBUG")]
        public static void Error(string msg) => UnityEngine.Debug.LogError(msg);
    }
}
