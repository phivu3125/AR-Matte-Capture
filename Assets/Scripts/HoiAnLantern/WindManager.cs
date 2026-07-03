using UnityEngine;

namespace HoiAnLantern
{
public class WindManager : MonoBehaviour
{
    [Header("Wind Settings")]
    public float windStrength = 1f;       // ảnh hưởng góc swing
    public float windSpeed = 0.5f;        // tốc độ thay đổi Perlin noise
    public float gustFrequency = 8f;      // trung bình 1 cơn gió mỗi bao nhiêu giây
    public float gustStrength = 2.5f;     // multiplier cơn gió
    public float gustDuration = 0.6f;     // thời gian cơn gió kéo dài (s)

    [HideInInspector]
    public float currentWind = 0f;        // giá trị gió hiện tại (-1..1)

    private float noiseOffset;
    private float gustTimer = 0f;
    private float currentGust = 0f;

    void Start()
    {
        noiseOffset = Random.Range(0f, 1000f);
    }

    void Update()
    {
        // Gust logic
        if (gustTimer <= 0f)
        {
            if (Random.value < Time.deltaTime / Mathf.Max(0.01f, gustFrequency))
            {
                currentGust = 1f;
                gustTimer = gustDuration;
            }
        }
        else
        {
            gustTimer -= Time.deltaTime;
            currentGust = Mathf.Clamp01(gustTimer / gustDuration);
        }

        // Perlin noise wind (-1..1)
        float n = Mathf.PerlinNoise(Time.time * windSpeed + noiseOffset, 0f) * 2f - 1f;
        float gustFactor = (Random.value - 0.5f) * gustStrength * currentGust;
        currentWind = Mathf.Clamp(n + gustFactor, -1f, 1f);
    }
}
} // namespace HoiAnLantern
