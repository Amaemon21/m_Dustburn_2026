using System.Collections;
using UnityEngine;

public class WorldGenBenchRuntime : MonoBehaviour
{
    private WorldGenPerfRunner _runner;
    private static WorldGenBenchRuntime _instance;

    public static bool Finished { get; private set; }

    public static string Result { get; private set; }

    public static void Start(string scenario, string profile, string args)
    {
        Stop();

        var host = new GameObject("WorldGenBenchRuntime");
        DontDestroyOnLoad(host);

        _instance = host.AddComponent<WorldGenBenchRuntime>();
        Finished = false;
        Result = null;

        _instance.Begin(scenario, profile, args);
    }

    public static void Stop()
    {
        if (_instance == null)
            return;

        Destroy(_instance.gameObject);
        _instance = null;
    }

    private void Begin(string scenario, string profile, string args)
    {
        _runner = gameObject.AddComponent<WorldGenPerfRunner>();
        StartCoroutine(Drive(scenario, profile, args));
    }

    private IEnumerator Drive(string scenario, string profile, string args)
    {
        yield return _runner.Run(scenario, profile, args);

        Result = _runner.Result;
        Finished = true;
    }
}
