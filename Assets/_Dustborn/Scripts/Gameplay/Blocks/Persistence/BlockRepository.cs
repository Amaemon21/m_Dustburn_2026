using System.Collections.Generic;

public sealed class BlockRepository
{
    private readonly ISaveService _saves;

    public BlocksData Origin { get; }

    public BlockRepository(ISaveService saves)
    {
        _saves = saves;
        Origin = saves.Get<BlocksData>();
        Origin.States ??= new Dictionary<string, BlockStateData>();
    }

    public int DamageOf(string id) => Origin.States.TryGetValue(id, out BlockStateData state) && state != null ? state.Damage : 0;

    public bool IsDestroyed(string id) => Origin.States.TryGetValue(id, out BlockStateData state) && state != null && state.Destroyed;

    public void SetDamage(string id, int damage)
    {
        BlockStateData state = StateOf(id);
        if (state.Damage == damage)
            return;

        state.Damage = damage;
        MarkDirty();
    }

    public void MarkDestroyed(string id)
    {
        BlockStateData state = StateOf(id);
        if (state.Destroyed)
            return;

        state.Destroyed = true;
        MarkDirty();
    }

    private BlockStateData StateOf(string id)
    {
        if (Origin.States.TryGetValue(id, out BlockStateData state) && state != null)
            return state;

        state = new BlockStateData();
        Origin.States[id] = state;
        return state;
    }

    private void MarkDirty() => _saves.MarkDirty<BlocksData>();
}
