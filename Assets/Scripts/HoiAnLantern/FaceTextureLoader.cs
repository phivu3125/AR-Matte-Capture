using UnityEngine;

namespace HoiAnLantern
{
    /// <summary>
    /// Applies a texture to all face GameObjects.
    /// Tracks instanced materials to prevent memory leaks.
    /// </summary>
    public class FaceTextureLoader : MonoBehaviour
    {
        public GameObject[] faces;
        public GameObject pointLightObject;

        private Material[] _instancedMaterials;

        /// <summary>
        /// Apply a single texture to all faces. Destroys previous instanced materials.
        /// </summary>
        public void ApplyTexture(Texture2D tex)
        {
            if (faces == null) return;

            if (_instancedMaterials == null || _instancedMaterials.Length != faces.Length)
                _instancedMaterials = new Material[faces.Length];

            for (int i = 0; i < faces.Length; i++)
            {
                Renderer rend = faces[i].GetComponent<Renderer>();
                if (rend == null) continue;

                // Destroy previous instanced material to prevent leak
                if (_instancedMaterials[i] != null)
                    Destroy(_instancedMaterials[i]);

                _instancedMaterials[i] = new Material(rend.sharedMaterial);
                _instancedMaterials[i].mainTexture = tex;
                rend.material = _instancedMaterials[i];
            }
        }

        void OnDestroy()
        {
            if (_instancedMaterials == null) return;
            foreach (var mat in _instancedMaterials)
            {
                if (mat != null) Destroy(mat);
            }
        }
    }
}
