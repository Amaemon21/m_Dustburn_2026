using Pathfinding;
using UnityEngine;

public class RandomWalkablePointService
{
    private const int MAX_ATTEMPTS = 20;
    
    private readonly UnitConfig _config;
    private readonly Vector3 _wanderCenter;
    private readonly UnitFieldOfViewService _unitFieldOfViewService;
    
    public RandomWalkablePointService(UnitConfig config, Vector3 wanderCenter, UnitFieldOfViewService unitFieldOfViewService)
    {
        _config = config;
        _wanderCenter = wanderCenter;
        _unitFieldOfViewService = unitFieldOfViewService;
    }

    public Vector3 GetWalkablePoint()
    {
        for (int i = 0; i < MAX_ATTEMPTS; i++)
        {
            Vector3 randomPosition = GetRandomWorldPoint();
            
            if (!_unitFieldOfViewService.IsInside(randomPosition))
            {
                continue;
            }
            
            GraphNode node = AstarPath.active == null ? null : AstarPath.active.GetNearest(randomPosition).node;
            
            if (node != null && node.Walkable)
            {
                return (Vector3)node.position;
            }
        }
        
        return _wanderCenter;
    }
    
    private Vector3 GetRandomWorldPoint()
    {
        Vector3 center = _wanderCenter;
        float radius = _config.MaxDistance; 
        
        Vector2 random = Random.insideUnitCircle * radius;
        
        return center + new Vector3(random.x, 0f, random.y);
    }
}