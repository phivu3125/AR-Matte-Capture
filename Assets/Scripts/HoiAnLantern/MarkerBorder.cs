using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HoiAnLantern
{
public class MarkerBorder : MonoBehaviour
{
    [SerializeField] Color colorWhenDetected = Color.green;
    [SerializeField] Color colorWhenMissing = Color.red;

    [SerializeField] Color colorNeutral = Color.white;

    Renderer _renderer;

    Coroutine currentCoroutine = null;

    void Start()
    {
        _renderer = GetComponent<Renderer>();
        if (_renderer == null) Debug.LogError("No renderer found in MarkerBorder");
    }

    IEnumerator gradientColor(Color color, bool gradientBack = true)
    {
        if (_renderer == null) yield break;
        Color initialColor = _renderer.material.color;
        float duration = MarkerManager.instance.GetGradientDuration(); // duration of the gradient
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _renderer.material.color = Color.Lerp(initialColor, color, elapsed / duration);
            yield return null;
        }
        _renderer.material.color = color;
        if (!gradientBack) yield break;
        // gradient back to neutral
        elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _renderer.material.color = Color.Lerp(color, colorNeutral, elapsed / duration);
            yield return null;
        }
        _renderer.material.color = colorNeutral;
        // endless loop
        currentCoroutine = StartCoroutine(gradientColor(color, gradientBack));
    }

    public void SetDetected()
    {
        if (currentCoroutine != null) StopCoroutine(currentCoroutine);
        currentCoroutine = StartCoroutine(gradientColor(colorWhenDetected, false));
    }
    
    public void SetMissing()
    {
        if (currentCoroutine != null) StopCoroutine(currentCoroutine);
        currentCoroutine = StartCoroutine(gradientColor(colorWhenMissing, true));
    }
}
} // namespace HoiAnLantern
