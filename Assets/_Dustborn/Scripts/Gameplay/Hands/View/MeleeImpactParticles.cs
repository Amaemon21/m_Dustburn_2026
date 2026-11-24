using System;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class MeleeImpactParticles : IDisposable
{
    private const int PARTICLES_PER_HIT = 6;
    private ParticleSystem _particles;
    private ParticleSystemRenderer _renderer;

    public void Emit(Material material, in MeleeImpact impact)
    {
        if (material == null)
            return;

        if (_particles == null)
            Create();

        if (_renderer.sharedMaterial != material)
        {
            _particles.Clear();
            _renderer.sharedMaterial = material;
        }

        Vector3 normal = impact.Normal;
        Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, normal);
        ParticleSystem.EmitParams particle = new() { position = impact.Point + normal * 0.02f };

        for (int i = 0; i < PARTICLES_PER_HIT; i++)
        {
            float angle = i * (2f * Mathf.PI / PARTICLES_PER_HIT);
            Vector3 direction = new(Mathf.Cos(angle), Mathf.Sin(angle), 1f);
            particle.velocity = rotation * direction * (0.65f + 0.08f * i);
            _particles.Emit(particle, 1);
        }
    }

    private void Create()
    {
        GameObject root = new("Melee Impact Particles", typeof(ParticleSystem));
        _particles = root.GetComponent<ParticleSystem>();
        _renderer = root.GetComponent<ParticleSystemRenderer>();
        _particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = _particles.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 0.22f;
        main.startSize = 0.045f;
        main.startColor = (Color)new Color32(181, 155, 117, 192);
        main.gravityModifier = 0.18f;
        main.maxParticles = 64;

        ParticleSystem.EmissionModule emission = _particles.emission;
        emission.enabled = false;
        _particles.Play(false);
    }

    public void Dispose()
    {
        if (_particles != null)
            Object.Destroy(_particles.gameObject);
    }
}
