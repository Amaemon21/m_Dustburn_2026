namespace UnityEngine
{
    public static class Debug
    {
        public static void LogWarning(object message) => System.Console.WriteLine(message);
        public static void LogError(object message) => System.Console.WriteLine(message);
    }
}
