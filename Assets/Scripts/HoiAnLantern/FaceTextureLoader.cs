using UnityEngine;

namespace HoiAnLantern
{
public class FaceTextureLoader : MonoBehaviour
{
    public GameObject[] faces; // face1, face2, ... gán trong Inspector
    public GameObject pointLightObject;
    // Apply 1 texture cho tất cả face
    public void ApplyTexture(Texture2D tex)
    {
        foreach (var face in faces)
        {
            Renderer rend = face.GetComponent<Renderer>();
            // Clone material riêng cho face
            rend.material = new Material(rend.sharedMaterial);
            rend.material.mainTexture = tex;
        }
    }
}
} // namespace HoiAnLantern
