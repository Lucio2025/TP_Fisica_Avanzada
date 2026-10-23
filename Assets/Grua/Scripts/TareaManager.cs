using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tarea: llevar las cajas de la zona A a la zona B.
/// Una caja cuenta como entregada si su centro está dentro de la zona B, no toca la grúa
/// y está quieta (v ≈ 0) durante <see cref="segundosQuieta"/> segundos.
/// </summary>
public class TareaManager : MonoBehaviour
{
    public GruaController grua;
    public Caja[] cajas;
    public Transform zonaB;              // sin escala; el área es mitadTamano en XZ local
    public Vector2 mitadTamano = new Vector2(1.25f, 1.25f);
    public float alturaMaxima = 2f;
    public float segundosQuieta = 1f;
    public float velocidadQuieta = 0.05f;      // m/s
    public float velocidadAngularQuieta = 0.1f; // rad/s

    public Text textoContador;
    public Text textoTiempo;
    public Text textoMensaje;
    public Button botonReset;

    public int Entregadas { get; private set; }
    public bool Completa { get; private set; }
    public float Cronometro { get; private set; }
    bool corriendo;

    void Awake()
    {
        if (botonReset != null) botonReset.onClick.AddListener(Reiniciar);
        if (textoMensaje != null) textoMensaje.text = "";
    }

    void FixedUpdate() => Paso(Time.fixedDeltaTime);

    public void Paso(float dt)
    {
        if (!Completa && !corriendo && grua != null && grua.HuboEntrada) corriendo = true;

        int n = 0;
        foreach (var caja in cajas)
        {
            bool ok = DentroDeZonaB(caja.rb.position)
                      && caja.rb.linearVelocity.magnitude < velocidadQuieta
                      && caja.rb.angularVelocity.magnitude < velocidadAngularQuieta
                      && !caja.tocandoGrua;
            caja.tiempoQuieta = ok ? caja.tiempoQuieta + dt : 0f;
            caja.tocandoGrua = false;
            if (caja.tiempoQuieta >= segundosQuieta) n++;
        }
        Entregadas = n;

        if (!Completa && n == cajas.Length)
        {
            Completa = true;
            corriendo = false;
        }
        if (corriendo) Cronometro += dt;
    }

    public bool DentroDeZonaB(Vector3 p)
    {
        Vector3 l = zonaB.InverseTransformPoint(p);
        return Mathf.Abs(l.x) <= mitadTamano.x && Mathf.Abs(l.z) <= mitadTamano.y && l.y < alturaMaxima;
    }

    void Update()
    {
        if (textoContador != null) textoContador.text = $"Cajas entregadas: <b>{Entregadas} / {cajas.Length}</b>";
        if (textoTiempo != null)
        {
            int min = (int)(Cronometro / 60f);
            float s = Cronometro - min * 60f;
            textoTiempo.text = $"Tiempo: {min:00}:{s:00.0}" + (corriendo || Completa ? "" : "  (arranca al mover)");
        }
        if (textoMensaje != null)
            textoMensaje.text = Completa ? $"¡Tarea completa en {(int)(Cronometro / 60f):00}:{Cronometro % 60f:00.0}!" : "";
    }

    public void Reiniciar()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
