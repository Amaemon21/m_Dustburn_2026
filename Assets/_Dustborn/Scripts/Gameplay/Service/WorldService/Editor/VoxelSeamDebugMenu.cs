using UnityEditor;

public static class VoxelSeamDebugMenu
{

    public static void Toggle()
    {
        VoxelColumnDebug.Visible = !VoxelColumnDebug.Visible;
        SceneView.RepaintAll();
    }

}
