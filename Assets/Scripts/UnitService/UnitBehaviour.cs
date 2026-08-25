using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

public class UnitBehaviour : MonoBehaviour
{
    [field: SerializeField] public UnitProperty Property { get; private set; }

    [SerializeField] private Transform _transform;
    
    private UnitMovementService _unitMovementService;
    private UnitFieldOfViewService _unitFieldOfViewService;
    private UnitDetectionService _detectionService;
    private UnitChaseService _unitChaseService;
    private UnitWanderService _unitWanderService;
    private UnitHealth _unitHealth;
    
    private CompositeDisposable _compositeDisposable;
    private CancellationTokenSource _behaviourCancellation;

    private readonly ReactiveProperty<UnitState> _state = new(UnitState.Wander);

    private void Awake()
    {
        _unitFieldOfViewService = new UnitFieldOfViewService(Property);
        _unitMovementService = new UnitMovementService(Property, _unitFieldOfViewService);
        _detectionService = new UnitDetectionService(Property, _unitFieldOfViewService, _transform);
        _unitChaseService = new UnitChaseService(_unitMovementService, _detectionService, _transform);
        _unitWanderService = new UnitWanderService(_unitMovementService);
        _unitHealth = new UnitHealth(Property);
    }

    private void OnEnable()
    {
        _compositeDisposable = new CompositeDisposable();
        _behaviourCancellation = new CancellationTokenSource();
        
        _compositeDisposable.Add(Observable
            .EveryUpdate()
            .Select(_ => _detectionService.CanSeePlayer())
            .DistinctUntilChanged()
            .Subscribe(canSee =>
            {
                _state.Value = canSee
                    ? UnitState.Chase
                    : UnitState.Wander;
            }));
        
        _compositeDisposable.Add(_state.Subscribe(SelectState));
    }

    private void SelectState(UnitState state)
    {
        CancellationToken token = RestartBehaviourCancellation();
        
        switch (state)
        {
            case UnitState.Wander:
                _unitWanderService.Wander(token).Forget();
                break;

            case UnitState.Chase:
                _unitChaseService.Chase(token).Forget();
                break;
        }
    }

    private CancellationToken RestartBehaviourCancellation()
    {
        _behaviourCancellation?.Cancel();
        _behaviourCancellation?.Dispose();

        _behaviourCancellation = new CancellationTokenSource();

        return _behaviourCancellation.Token;
    }
    
    private void OnDisable()
    {
        _compositeDisposable?.Dispose();
        
        _behaviourCancellation?.Cancel();
        _behaviourCancellation?.Dispose();
        _behaviourCancellation = null;
    }
}