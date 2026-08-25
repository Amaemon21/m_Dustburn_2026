using Pathfinding;
using UnityEngine;

public class RandomWalkablePointService
{
    private const int MAX_ATTEMPTS = 20;
    
    private readonly UnitProperty _unitProperty;
    private readonly UnitFieldOfViewService _unitFieldOfViewService;
    
    public RandomWalkablePointService(UnitProperty unitProperty, UnitFieldOfViewService unitFieldOfViewService)
    {
        _unitProperty = unitProperty;
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
            
            GraphNode node = AstarPath.active.GetNearest(randomPosition).node;
            
            if (node.Walkable)
            {
                return (Vector3)node.position;
            }
        }
        
        return _unitProperty.WanderCenter.position;
    }
    
    private Vector3 GetRandomWorldPoint()
    {
        Vector3 center = _unitProperty.WanderCenter.position;
        float radius = _unitProperty.Radius; 
        
        Vector2 random = Random.insideUnitCircle * radius;
        
        return center + new Vector3(random.x, 0f, random.y);
    }
}