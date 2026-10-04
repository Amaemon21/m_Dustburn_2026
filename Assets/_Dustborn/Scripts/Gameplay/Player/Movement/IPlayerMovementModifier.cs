public interface IPlayerMovementModifier
{
    void Modify(ref PlayerMovementRequest request);
    void Apply(in PlayerMovementResult result, float deltaTime);
}
