using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Tabla en pantalla con target vs. valor real (jointPosition) de cada articulación.</summary>
public class PanelEstado : MonoBehaviour
{
    public GruaController grua;
    public ArticulationBody baseRaiz;
    public ArticulationBody[] eslabones;   // spherical pasivos
    public ArticulationBody cabezal;       // fixed

    public Text columnaNombre;
    public Text columnaTipo;
    public Text columnaTarget;
    public Text columnaReal;
    public Text columnaFuerza;

    readonly StringBuilder n = new StringBuilder(), t = new StringBuilder(), g = new StringBuilder(),
        r = new StringBuilder(), f = new StringBuilder();
    float proxima;

    void Update()
    {
        if (Application.isPlaying && Time.unscaledTime < proxima) return;
        proxima = Time.unscaledTime + 0.1f;

        n.Clear(); t.Clear(); g.Clear(); r.Clear(); f.Clear();
        Fila("<b>Articulación</b>", "<b>Tipo</b>", "<b>Target</b>", "<b>Real</b>", "<b>F drive</b>");
        Fila("Base (raíz)", "Immovable", "—", "fija", "—");

        foreach (var eje in grua.ejes)
        {
            string fmt = eje.angular ? "+0.0;-0.0" : "+0.000;-0.000";
            float err = Mathf.Abs(eje.objetivo - eje.Real);
            string color = grua.ModoPrueba ? "#ffb84d" : err < (eje.angular ? 1f : 0.02f) ? "#a0ffa0" : "#ffd27f";
            string target = grua.ModoPrueba ? "libre" : eje.objetivo.ToString(fmt) + eje.Unidad;
            string unidadF = eje.angular ? " N·m" : " N";
            Fila(eje.nombre, eje.tipo, target, $"<color={color}>{eje.Real.ToString(fmt)}{eje.Unidad}</color>",
                eje.Fuerza.ToString("0") + unidadF);
        }

        for (int i = 0; i < eslabones.Length; i++)
        {
            var b = eslabones[i];
            string real = "—";
            if (b.dofCount >= 2)
                real = $"{b.jointPosition[0] * Mathf.Rad2Deg:+0;-0}° / {b.jointPosition[1] * Mathf.Rad2Deg:+0;-0}°";
            Fila($"Cable {i + 1}", "Spherical", "pasivo", real, "—");
        }
        Fila("Cabezal pinza", "Fixed", "—", "fijo", "—");

        columnaNombre.text = n.ToString();
        columnaTipo.text = t.ToString();
        columnaTarget.text = g.ToString();
        columnaReal.text = r.ToString();
        columnaFuerza.text = f.ToString();
    }

    void Fila(string a, string b, string c, string d, string e)
    {
        n.AppendLine(a); t.AppendLine(b); g.AppendLine(c); r.AppendLine(d); f.AppendLine(e);
    }
}
