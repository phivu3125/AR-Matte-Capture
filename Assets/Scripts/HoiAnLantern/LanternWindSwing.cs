#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace HoiAnLantern
{
[RequireComponent(typeof(Transform))]
public class LanternWindSwing : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Điểm treo (pivot). Lồng đèn sẽ swing quanh đây)")]
    public Transform hangPoint;

    [Header("Swing Settings")]
    public Vector3 swingAxis = Vector3.forward;
    public float maxAngle = 20f;
    public float responsiveness = 2.5f;
    [Range(0f, 1f)]
    public float damping = 0.98f;

    [Header("Wind Settings")]
    public WindManager windManager;
    public float speedMultiplier = 1f;
    public float phaseOffset = 0f;

    // internal
    float currentAngle = 0f;
    float currentVelocity = 0f;
    Vector3 initialOffset;

    void Start()
    {
        if (hangPoint == null) hangPoint = transform;
        initialOffset = transform.position - hangPoint.position;
    }

    void Update()
    {
        if (windManager == null) return;

        float t = Time.time * speedMultiplier + phaseOffset;
        float targetAngle = windManager.currentWind * maxAngle * Mathf.Sin(t);

        float angleDiff = Mathf.DeltaAngle(currentAngle, targetAngle);
        float accel = angleDiff * responsiveness - currentVelocity * (1f - damping) * 10f;
        currentVelocity += accel * Time.deltaTime;
        currentVelocity *= damping;
        currentAngle += currentVelocity * Time.deltaTime;

        Quaternion rot = Quaternion.AngleAxis(currentAngle, swingAxis.normalized);
        transform.position = hangPoint.position + rot * initialOffset;
        transform.rotation = rot;
    }

    // ================= Gizmos =================
#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (hangPoint == null) hangPoint = transform;

        Gizmos.color = Color.yellow;
        Vector3 start = hangPoint.position;
        Vector3 dir = swingAxis.normalized * 1.0f; // scale để thấy rõ
        Gizmos.DrawLine(start, start + dir);

        Handles.color = Color.cyan;
        Handles.ArrowHandleCap(0, start, Quaternion.LookRotation(dir), 0.5f, EventType.Repaint);
    }
#endif
}
} // namespace HoiAnLantern
