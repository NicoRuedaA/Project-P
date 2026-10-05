using UnityEngine;

namespace BotwVfx
{
    /// <summary>
    /// Orienta el objeto hacia la cámara manteniendo el "arriba" del mundo.
    /// Se usa en los emisores de medio toroide de la explosión (truco del artículo
    /// de 80.lv): el semicírculo siempre se ve de frente y las lenguas de fuego
    /// salen hacia arriba y hacia los lados.
    /// </summary>
    [ExecuteAlways]
    public class FaceCamera : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null)
                Face(cam.transform.position);
        }

        public void Face(Vector3 cameraPosition)
        {
            Vector3 d = cameraPosition - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }
    }
}
