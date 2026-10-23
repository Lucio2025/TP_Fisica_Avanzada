using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Cámara orbital: clic derecho + arrastrar para orbitar, rueda para zoom, R para resetear.</summary>
public class CamaraOrbital : MonoBehaviour
{
    public Vector3 pivote = new Vector3(3f, 3f, 0f);
    public float distancia = 18.5f;
    public float yaw = 15f;
    public float pitch = 22f;
    public float sensibilidad = 0.25f;
    public float distanciaMin = 5f;
    public float distanciaMax = 40f;

    float distancia0, yaw0, pitch0;

    void Awake()
    {
        distancia0 = distancia;
        yaw0 = yaw;
        pitch0 = pitch;
        Aplicar();
    }

    void LateUpdate()
    {
        var m = Mouse.current;
        if (m != null)
        {
            if (m.rightButton.isPressed)
            {
                Vector2 d = m.delta.ReadValue();
                yaw += d.x * sensibilidad;
                pitch = Mathf.Clamp(pitch - d.y * sensibilidad, 5f, 85f);
            }
            // Solo el signo: la magnitud de la rueda varía mucho entre navegadores.
            float rueda = m.scroll.ReadValue().y;
            if (rueda != 0f)
                distancia = Mathf.Clamp(distancia * (rueda > 0f ? 0.9f : 1.1f), distanciaMin, distanciaMax);
        }
        var kb = Keyboard.current;
        if (kb != null && kb.rKey.wasPressedThisFrame)
        {
            distancia = distancia0;
            yaw = yaw0;
            pitch = pitch0;
        }
        Aplicar();
    }

    public void Aplicar()
    {
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        transform.SetPositionAndRotation(pivote - rot * Vector3.forward * distancia, rot);
    }
}
