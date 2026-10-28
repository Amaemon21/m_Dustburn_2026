//Copyright 2025 Kinemation

using UnityEngine;
using System.Collections;

public class Cloud : MonoBehaviour
{
    private CloudSpawner spawner;
    private Material cloudMat;
    private float moveSpeed;
    private float lifetime;
    private float fadeDuration;
    private Vector3 moveDir;

    private void Awake()
    {
        Renderer rend = GetComponentInChildren<Renderer>();
        cloudMat = rend.material;
    }

    public void Initialize(CloudSpawner spawner, float moveSpeed, float lifetime, bool prewarm = false, bool isLowCloud = false)
    {
        this.spawner = spawner;
        this.moveSpeed = moveSpeed;
        this.lifetime = lifetime;

        fadeDuration = Random.Range(1.5f, 3f);
        moveDir = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;

        if (prewarm)
        {
            float alpha = isLowCloud ? Random.Range(0.2f, 0.5f) : Random.Range(0.05f, 0.6f);
            SetCloudAlpha(alpha);

            if (isLowCloud)
            {
                Color darker = new Color(0.6f, 0.6f, 0.6f, alpha) * 0.5f;
                SetCloudColor(darker);
            }
            else
            {
                SetCloudColor(new Color(1f, 1f, 1f, alpha));
            }

            StartCoroutine(FadeOutOnlyRoutine());
        }
        else
        {
            StartCoroutine(FadeInOutRoutine());
        }
    }

    private void Update()
    {
        transform.position += moveDir * moveSpeed * Time.deltaTime;
    }

    private IEnumerator FadeInOutRoutine()
    {
        yield return StartCoroutine(LerpAlpha(0f, 0.6f, fadeDuration));
        yield return new WaitForSeconds(lifetime - fadeDuration * 2f);
        yield return StartCoroutine(LerpAlpha(0.6f, 0f, fadeDuration));
        spawner.OnCloudDestroyed();
        Destroy(gameObject);
    }

    private IEnumerator FadeOutOnlyRoutine()
    {
        float visibleTime = lifetime - fadeDuration;
        yield return new WaitForSeconds(visibleTime);
        yield return StartCoroutine(LerpAlpha(cloudMat.color.a, 0f, fadeDuration));
        spawner.OnCloudDestroyed();
        Destroy(gameObject);
    }

    private IEnumerator LerpAlpha(float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            SetCloudAlpha(Mathf.Lerp(from, to, t / duration));
            yield return null;
        }
        SetCloudAlpha(to);
    }

    private void SetCloudAlpha(float a)
    {
        if (cloudMat.HasProperty("_Color"))
        {
            Color c = cloudMat.color;
            c.a = a;
            cloudMat.color = c;
        }
    }

    private void SetCloudColor(Color color)
    {
        if (cloudMat.HasProperty("_Color"))
            cloudMat.color = color;
    }

    private void OnDestroy()
    {
        if (cloudMat != null)
            Destroy(cloudMat);
    }
}
