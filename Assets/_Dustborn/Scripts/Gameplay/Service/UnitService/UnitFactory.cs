using Pathfinding;
using Pathfinding.ECS.RVO;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public class UnitFactory
{
    private readonly IObjectResolver _resolver;
    private readonly Transform _playerTransform;

    public UnitFactory(IObjectResolver resolver, Transform playerTransform)
    {
        _resolver = resolver;
        _playerTransform = playerTransform;
    }

    public UnitView Create(Transform spawnTransform, UnitConfig config)
    {
        UnitView view = _resolver.Instantiate(config.UnitPrefab, spawnTransform.position, spawnTransform.rotation, parent: null);
        
        ApplyMovementSettings(view, config);
        
        var fovService = new UnitFieldOfViewService(config, view.UnitCenter);
        var randomWalkablePointService = new RandomWalkablePointService( config, spawnTransform, fovService);
        var movementService = new UnitMovementService(config, view, fovService, randomWalkablePointService);
        var detectionService = new UnitDetectionService(config, view, fovService, _playerTransform);
        var chaseService = new UnitChaseService(config, movementService, detectionService, _playerTransform);
        var wanderService = new UnitWanderService(movementService);
        var health = new UnitHealth(config);

        var controller = new UnitController(movementService, detectionService, chaseService, wanderService, health, _playerTransform);

        view.Controller = controller;
        controller.Initialize();

        return view;
    }
    
    private void ApplyMovementSettings(UnitView view, UnitConfig config)
    {
        FollowerEntity follower = view.FollowerEntity;
        follower.maxSpeed = config.MoveSpeed;
        follower.enableLocalAvoidance = config.EnableLocalAvoidance;

        if (!config.EnableLocalAvoidance) return;

        RVOAgent rvo = follower.rvoSettings;
        rvo.agentTimeHorizon = config.AgentTimeHorizon;
        rvo.obstacleTimeHorizon = config.ObstacleTimeHorizon;
        rvo.maxNeighbours = config.MaxNeighbours;
        rvo.priority = config.AvoidancePriority;
        rvo.layer = config.AvoidanceLayer;
        rvo.collidesWith = config.CollidesWith;
        follower.rvoSettings = rvo;
    }
}
