using UnityEngine;
using VContainer.Unity;

public sealed class BlockBuildingController : ITickable, IGameplayActivatable
{
    private readonly PlayerSpawnService _player;
    private readonly HeldItemSelection _selection;
    private readonly ItemCatalog _catalog;
    private readonly PlayerInputService _input;
    private readonly BlockPlacementProbe _probe;
    private readonly BlockPlacementService _placement;
    private readonly BlockPlacementPreview _preview;
    private bool _running;
    private int _rotation;
    private float _nextPlacement;
    private bool _secondaryHeld;

    public BlockBuildingController(PlayerSpawnService player, HeldItemSelection selection, ItemCatalog catalog,
        PlayerInputService input, BlockPlacementProbe probe, BlockPlacementService placement, BlockPlacementPreview preview)
    {
        _player = player;
        _selection = selection;
        _catalog = catalog;
        _input = input;
        _probe = probe;
        _placement = placement;
        _preview = preview;
    }

    public void Activate() => _running = true;

    public void Tick()
    {
        HeldItem held = _selection.Held.CurrentValue;
        bool rotate = _input.SecondaryHeld && !_secondaryHeld;
        _secondaryHeld = _input.SecondaryHeld;
        if (!_running || !_input.IsEnabled || _player.Character == null ||
            _catalog.GetItem(held.ItemId) is not BlockInventoryItem item || item.Prefab == null)
        {
            _preview.Hide();
            return;
        }
        if (rotate)
            _rotation = (_rotation + 1) & 3;
        Transform pivot = _player.Character.View.CameraPivot;
        Ray ray = new(pivot.position, pivot.forward);
        if (!_probe.TryFind(item, ray, _player.Player, _rotation,
                out Vector3Int cell, out float originY, out bool allowed, out _))
        {
            _preview.Hide();
            return;
        }
        _preview.Show(item, BlockGrid.Center(cell, originY), BlockGrid.Rotation(_rotation), allowed);
        if (!allowed || !_input.PrimaryHeld || Time.time < _nextPlacement)
            return;
        if (_placement.TryPlace(held, item, ray, _player.Player, _rotation))
            _nextPlacement = Time.time + item.PlacementInterval;
    }
}
