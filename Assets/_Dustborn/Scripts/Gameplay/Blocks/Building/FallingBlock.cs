using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class FallingBlock : MonoBehaviour
{
    private const float MAX_FALL_TIME = 8f;
    private const float REST_TIME = 2f;
    private const float SINK_TIME = 1.15f;
    private const float SINK_DEPTH = BlockGrid.CELL_SIZE * 1.8f;
    private const float SINK_CLEARANCE = 0.06f;
    private const int IGNORE_RAYCAST_LAYER = 2;

    private Rigidbody _body;
    private Vector3 _sinkOrigin;
    private float _createdAt;
    private float _landedAt = -1f;
    private float _sinkAt = -1f;
    private float _sinkDepth = SINK_DEPTH;

    private void Awake()
    {
        gameObject.layer = IGNORE_RAYCAST_LAYER;
        _body = GetComponent<Rigidbody>();
        _body.isKinematic = false;
        _body.useGravity = true;
        _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _createdAt = Time.time;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_landedAt >= 0f)
            return;

        if (collision.rigidbody != null && collision.rigidbody.TryGetComponent(out FallingBlock other) && other._landedAt < 0f)
            return;

        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y <= 0.35f)
                continue;

            _landedAt = Time.time;
            return;
        }
    }

    private void Update()
    {
        float now = Time.time;

        if (_landedAt < 0f)
        {
            if (now - _createdAt >= MAX_FALL_TIME)
                Destroy(gameObject);

            return;
        }

        if (now - _landedAt < REST_TIME)
            return;

        if (_sinkAt < 0f)
            BeginSink(now);

        float progress = (now - _sinkAt) / (SINK_TIME * (_sinkDepth / SINK_DEPTH));

        if (progress >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        transform.position = _sinkOrigin + Vector3.down * Mathf.SmoothStep(0f, _sinkDepth, progress);
    }

    private void BeginSink(float now)
    {
        _sinkAt = now;
        _sinkOrigin = transform.position;
        Bounds bounds = new(_sinkOrigin, Vector3.zero);

        foreach (BoxCollider box in GetComponent<BlockView>().PlacementColliders)
            bounds.Encapsulate(box.bounds);

        _sinkDepth = Mathf.Max(SINK_DEPTH, bounds.size.y + SINK_CLEARANCE);
        _body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        _body.isKinematic = true;
        _body.detectCollisions = false;
    }
}
