using UnityEngine;

public struct SpringFloat
{
    private const float MAX_STEP = 1f / 120f;

    public float Value;
    public float Velocity;

    public SpringFloat(float value)
    {
        Value = value;
        Velocity = 0f;
    }

    public void Step(float target, float frequency, float dampingRatio, float deltaTime)
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

    public bool IsResting(float target, float epsilon)
        => Mathf.Abs(Value - target) < epsilon && Mathf.Abs(Velocity) < epsilon;
}
