using UnityEngine;

/// <summary>Dibuja el cable de izaje (carro → polea) con un LineRenderer. Solo visual.</summary>
[RequireComponent(typeof(LineRenderer))]
public class CableVisual : MonoBehaviour
{
    public Transform desde;
    public Transform hasta;
    public float separacion = 0.12f; // dos ramales, como un aparejo real

    LineRenderer linea;

    void Awake()
    {
        linea = GetComponent<LineRenderer>();
        linea.positionCount = 4;
    }

    void LateUpdate()
    {
        if (desde == null || hasta == null) return;
        Vector3 lado = desde.right * separacion;
        linea.SetPosition(0, desde.position - lado);
        linea.SetPosition(1, hasta.position - lado);
        linea.SetPosition(2, hasta.position + lado);
        linea.SetPosition(3, desde.position + lado);
    }
}
