using System;
using UnityEngine;

/// <summary>Carga de la tarea: Rigidbody común (no articulado).</summary>
[RequireComponent(typeof(Rigidbody))]
public class Caja : MonoBehaviour
{
    public Rigidbody rb;

    // Lo marca la física (contacto con cualquier cuerpo de la grúa) y lo consume TareaManager.
    [NonSerialized] public bool tocandoGrua;
    [NonSerialized] public float tiempoQuieta;

    void OnCollisionEnter(Collision c) => Revisar(c);
    void OnCollisionStay(Collision c) => Revisar(c);

    void Revisar(Collision c)
    {
        if (c.articulationBody != null) tocandoGrua = true;
    }
}
