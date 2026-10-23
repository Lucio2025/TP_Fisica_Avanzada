using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Un grado de libertad accionado por un drive de ArticulationBody.
/// El operador fija un "comando"; el controlador genera un "objetivo" con perfil
/// trapezoidal (velocidad y aceleración limitadas) y lo envía al drive en FixedUpdate.
/// </summary>
[Serializable]
public class EjeControlado
{
    public string nombre;
    public string tipo;            // texto para la UI: Revolute / Prismatic
    public string teclas;          // ayuda de teclado
    public ArticulationBody cuerpo;
    public bool angular;           // true: grados, false: metros
    public float minimo;
    public float maximo;
    public float velocidadMax;     // °/s o m/s
    public float aceleracionMax;   // °/s² o m/s²
    public Slider slider;
    public Text etiqueta;

    [NonSerialized] public float comando;
    [NonSerialized] public float objetivo;
    [NonSerialized] public float velocidadObjetivo;
    [NonSerialized] public ArticulationDrive driveOriginal;

    public string Unidad => angular ? "°" : " m";

    // Todos los ejes accionados usan el eje X del ancla (twist en revolute, X en prismatic).
    public ArticulationDrive Drive
    {
        get => cuerpo.xDrive;
        set => cuerpo.xDrive = value;
    }

    public float Real
    {
        get
        {
            if (cuerpo == null || cuerpo.dofCount < 1) return 0f;
            float v = cuerpo.jointPosition[0];
            return angular ? v * Mathf.Rad2Deg : v;
        }
    }

    public float Fuerza
    {
        get
        {
            if (cuerpo == null || cuerpo.dofCount < 1) return 0f;
            return cuerpo.driveForce[0];
        }
    }
}

public class GruaController : MonoBehaviour
{
    public const int Torre = 0, Carro = 1, Polea = 2, DedoIzq = 3, DedoDer = 4;

    public ArticulationBody raiz;
    public EjeControlado[] ejes;
    public Toggle toggleModoPrueba;
    public Text textoPinza;

    public bool PinzaCerrada { get; private set; }
    public bool ModoPrueba { get; private set; }
    public bool HuboEntrada { get; private set; }

    float[] entradaTeclado;
    bool inicializado;

    void Awake() => Inicializar();

    public void Inicializar()
    {
        if (inicializado) return;
        inicializado = true;
        entradaTeclado = new float[ejes.Length];

        foreach (var eje in ejes)
        {
            eje.driveOriginal = eje.Drive;
            eje.comando = eje.objetivo = eje.Drive.target;
            eje.velocidadObjetivo = 0f;
            if (eje.slider != null)
            {
                var e = eje;
                eje.slider.minValue = eje.minimo;
                eje.slider.maxValue = eje.maximo;
                eje.slider.SetValueWithoutNotify(eje.comando);
                eje.slider.onValueChanged.AddListener(v => { e.comando = v; HuboEntrada = true; });
            }
        }

        if (toggleModoPrueba != null)
        {
            toggleModoPrueba.SetIsOnWithoutNotify(false);
            toggleModoPrueba.onValueChanged.AddListener(SetModoPrueba);
        }

        IgnorarColisionesInternas();
        ActualizarUI();
    }

    // Los cuerpos de la grúa no chocan entre sí (la estructura real tampoco se autointerfiere);
    // solo interactúan con el suelo y las cajas.
    void IgnorarColisionesInternas()
    {
        var cols = raiz.GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++)
            for (int j = i + 1; j < cols.Length; j++)
                Physics.IgnoreCollision(cols[i], cols[j]);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            entradaTeclado[Torre] = Eje(kb.dKey.isPressed || kb.rightArrowKey.isPressed, kb.aKey.isPressed || kb.leftArrowKey.isPressed);
            entradaTeclado[Carro] = Eje(kb.wKey.isPressed || kb.upArrowKey.isPressed, kb.sKey.isPressed || kb.downArrowKey.isPressed);
            entradaTeclado[Polea] = Eje(kb.eKey.isPressed, kb.qKey.isPressed);
            if (kb.spaceKey.wasPressedThisFrame) AlternarPinza();
        }
        ActualizarUI();
    }

    void LateUpdate()
    {
        // Evita que Espacio/Enter "pulse" el último botón clickeado (p. ej. Reset).
        var es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null) es.SetSelectedGameObject(null);
    }

    static float Eje(bool pos, bool neg) => (pos ? 1f : 0f) - (neg ? 1f : 0f);

    void FixedUpdate() => Paso(Time.fixedDeltaTime);

    /// <summary>Un paso de control: aplica la entrada y escribe los targets de los drives.</summary>
    public void Paso(float dt)
    {
        for (int i = 0; i < ejes.Length; i++)
        {
            var eje = ejes[i];
            if (entradaTeclado != null && entradaTeclado[i] != 0f && !ModoPrueba)
            {
                eje.comando = Mathf.Clamp(eje.comando + entradaTeclado[i] * eje.velocidadMax * dt, eje.minimo, eje.maximo);
                if (eje.slider != null) eje.slider.SetValueWithoutNotify(eje.comando);
                HuboEntrada = true;
            }
            if (ModoPrueba) continue;

            Perfil(eje, dt);
            var d = eje.Drive;
            d.target = eje.objetivo;
            d.targetVelocity = eje.velocidadObjetivo;
            eje.Drive = d;
        }
    }

    // Perfil trapezoidal: acelera hasta velocidadMax y frena a tiempo para llegar al comando sin pasarse.
    static void Perfil(EjeControlado eje, float dt)
    {
        float dist = eje.comando - eje.objetivo;
        if (Mathf.Abs(dist) < 1e-4f && Mathf.Abs(eje.velocidadObjetivo) < eje.aceleracionMax * dt)
        {
            eje.objetivo = eje.comando;
            eje.velocidadObjetivo = 0f;
            return;
        }
        float vDeseada = Mathf.Sign(dist) * Mathf.Min(eje.velocidadMax, Mathf.Sqrt(2f * eje.aceleracionMax * Mathf.Abs(dist)));
        eje.velocidadObjetivo = Mathf.MoveTowards(eje.velocidadObjetivo, vDeseada, eje.aceleracionMax * dt);
        float nuevo = eje.objetivo + eje.velocidadObjetivo * dt;
        if (Mathf.Sign(eje.comando - nuevo) != Mathf.Sign(dist))
        {
            nuevo = eje.comando;
            eje.velocidadObjetivo = 0f;
        }
        eje.objetivo = nuevo;
    }

    public void AlternarPinza() => SetPinza(!PinzaCerrada);

    public void SetPinza(bool cerrada)
    {
        if (ModoPrueba) return;
        PinzaCerrada = cerrada;
        foreach (int i in new[] { DedoIzq, DedoDer })
        {
            var eje = ejes[i];
            eje.comando = cerrada ? eje.maximo : eje.minimo;
            if (eje.slider != null) eje.slider.SetValueWithoutNotify(eje.comando);
        }
        HuboEntrada = true;
    }

    /// <summary>Modo prueba: drives sin rigidez ni amortiguamiento; el mecanismo queda librado a gravedad y límites.</summary>
    public void SetModoPrueba(bool activo)
    {
        ModoPrueba = activo;
        foreach (var eje in ejes)
        {
            ArticulationDrive d;
            if (activo)
            {
                d = eje.Drive;
                d.stiffness = 0f;
                d.damping = 0f;
                d.targetVelocity = 0f;
            }
            else
            {
                // Al volver, el target arranca donde quedó el joint para no dar un tirón.
                float real = Mathf.Clamp(eje.Real, eje.minimo, eje.maximo);
                eje.comando = eje.objetivo = real;
                eje.velocidadObjetivo = 0f;
                d = eje.driveOriginal;
                d.target = real;
                d.targetVelocity = 0f;
                if (eje.slider != null) eje.slider.SetValueWithoutNotify(real);
            }
            eje.Drive = d;
            if (eje.slider != null) eje.slider.interactable = !activo;
        }
        if (!activo) PinzaCerrada = ejes[DedoIzq].comando > 0.5f * ejes[DedoIzq].maximo;
        if (toggleModoPrueba != null) toggleModoPrueba.SetIsOnWithoutNotify(activo);
    }

    public void ActualizarUI()
    {
        foreach (var eje in ejes)
        {
            if (eje.etiqueta == null) continue;
            string f = eje.angular ? "+0.0;-0.0" : "+0.00;-0.00";
            eje.etiqueta.text = $"<b>{eje.nombre}</b>  <color=#9fd3ff>[{eje.teclas}]</color>\n" +
                                $"target {eje.objetivo.ToString(f)}{eje.Unidad}   real {eje.Real.ToString(f)}{eje.Unidad}";
        }
        if (textoPinza != null)
            textoPinza.text = ModoPrueba ? "Pinza: <color=#ffb84d>MODO PRUEBA (drives apagados)</color>"
                : PinzaCerrada ? "Pinza: <color=#ff8080>CERRADA</color>  (Espacio para abrir)"
                : "Pinza: <color=#80ff80>ABIERTA</color>  (Espacio para cerrar)";
    }
}
