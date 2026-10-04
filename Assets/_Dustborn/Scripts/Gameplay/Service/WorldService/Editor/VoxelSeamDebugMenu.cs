using UnityEditor;

public static class VoxelSeamDebugMenu
{
    private const string MENU = "Мир/Отладка стыков LOD";

    [MenuItem(MENU)]
    private static void Toggle()
    {
        VoxelColumnDebug.Visible = !VoxelColumnDebug.Visible;
        SceneView.RepaintAll();
    }

    [MenuItem(MENU, true)]
    private static bool Validate()
    {
        Menu.SetChecked(MENU, VoxelColumnDebug.Visible);
        return true;
    }
}
