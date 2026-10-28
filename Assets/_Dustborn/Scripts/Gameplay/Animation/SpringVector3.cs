using UnityEngine;

public struct SpringVector3
{
    private const float MAX_STEP = 1f / 120f;

    public Vector3 Value;
    public Vector3 Velocity;

    public void Step(Vector3 target, float frequency, float dampingRatio, float deltaTime)
    {
        float omega = 2f * Mathf.PI * frequency;
        float stiffness = omega * omega;
        float damping = 2f * dampingRatio * omega;
        while (deltaTime > 0f)
        {
            float step = Mathf.Min(deltaTime, MAX_STEP);
            Velocity += (stiffness * (target - Value) - damping * Velocity) * step;
            Value += Velocity * step;
            deltaTime -= step;
        }
    }

    public bool IsResting(Vector3 target, float epsilon)
        => (Value - target).sqrMagnitude < epsilon * epsilon && Velocity.sqrMagnitude < epsilon * epsilon;
}
