using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Prueba automática del simulador (sin entrar en Play): simula la física a mano con Physics.Simulate,
/// usando el mismo GruaController y TareaManager de la escena.
///   1) Modo prueba sin drives: el izaje debe caer hasta su límite y nada debe pasarse de los límites.
///   2) Respuesta de cada drive (error de seguimiento, sobrepaso).
///   3) Tarea completa: mueve las 3 cajas de A a B con una secuencia de comandos.
/// Uso (batchmode): -executeMethod PruebaGrua.Ejecutar [-reporte ruta.txt] [-capturas carpeta]
/// No guarda la escena: la simulación mueve los objetos.
/// </summary>
public static class PruebaGrua
{
    const float Dt = 0.01f;
    static readonly StringBuilder rep = new StringBuilder();
    static GruaController grua;
    static TareaManager tarea;
    static float t;
    static float[] errMax;
    static float vCajaMaxAgarre;
    static bool midiendoAgarre;
    static int nEslabones;
    static string carpetaCapturas;

    [MenuItem("Tools/Grúa/Ejecutar prueba automática (no guarda la escena)")]
    static void EjecutarMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Ejecutar();
        EditorSceneManager.OpenScene(ConfiguracionProyecto.RutaEscena);
    }

    public static void Ejecutar()
    {
        rep.Clear();
        string ruta = Arg("-reporte") ?? "Logs/prueba_grua.txt";
        carpetaCapturas = Arg("-capturas");
        Physics.simulationMode = SimulationMode.Script;
        bool ok = true;
        try
        {
            ok &= PruebaGravedad();
            ok &= PruebaRespuesta();
            ok &= PruebaTarea();
        }
        catch (Exception e)
        {
            ok = false;
            Linea("EXCEPCIÓN: " + e);
        }
        finally
        {
            Physics.simulationMode = SimulationMode.FixedUpdate;
        }
        Linea(ok ? "RESULTADO GLOBAL: OK" : "RESULTADO GLOBAL: FALLA");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ruta)));
        File.WriteAllText(ruta, rep.ToString());
        Debug.Log("[PRUEBA]\n" + rep);
    }

    static string Arg(string nombre)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == nombre) return a[i + 1];
        return null;
    }

    static void Linea(string s) => rep.AppendLine(s);

    static void Abrir()
    {
        EditorSceneManager.OpenScene(ConfiguracionProyecto.RutaEscena);
        grua = Object.FindFirstObjectByType<GruaController>();
        tarea = Object.FindFirstObjectByType<TareaManager>();
        grua.Inicializar();
        nEslabones = Object.FindFirstObjectByType<PanelEstado>().eslabones.Length;
        t = 0f;
        errMax = new float[grua.ejes.Length];
    }

    static void Simular(float segundos, Func<bool> hasta = null)
    {
        int pasos = Mathf.RoundToInt(segundos / Dt);
        for (int i = 0; i < pasos; i++)
        {
            grua.Paso(Dt);
            tarea.Paso(Dt);
            Physics.Simulate(Dt);
            t += Dt;
            for (int k = 0; k < grua.ejes.Length; k++)
            {
                var e = grua.ejes[k];
                float err = Mathf.Abs(e.objetivo - e.Real);
                if (float.IsNaN(err)) throw new Exception($"NaN en {e.nombre} a t={t:0.00}");
                if (!grua.ModoPrueba && k < 3) errMax[k] = Mathf.Max(errMax[k], err);
            }
            if (midiendoAgarre)
                foreach (var c in tarea.cajas) vCajaMaxAgarre = Mathf.Max(vCajaMaxAgarre, c.rb.linearVelocity.magnitude);
            if (hasta != null && hasta()) return;
        }
    }

    static bool EnObjetivo(int i, float tol)
    {
        var e = grua.ejes[i];
        return Mathf.Abs(e.comando - e.objetivo) < 1e-4f && Mathf.Abs(e.Real - e.comando) < tol;
    }

    static float SwingMax()
    {
        float m = 0f;
        foreach (var b in Object.FindFirstObjectByType<PanelEstado>().eslabones)
            if (b.dofCount >= 2) m = Mathf.Max(m, Mathf.Abs(b.jointPosition[0]), Mathf.Abs(b.jointPosition[1]));
        return m * Mathf.Rad2Deg;
    }

    static float VelSwingMax()
    {
        float m = 0f;
        foreach (var b in Object.FindFirstObjectByType<PanelEstado>().eslabones)
            if (b.dofCount >= 2) m = Mathf.Max(m, Mathf.Abs(b.jointVelocity[0]), Mathf.Abs(b.jointVelocity[1]));
        return m * Mathf.Rad2Deg; // °/s
    }

    // ------------------------------------------------------------------ 1
    static bool PruebaGravedad()
    {
        Abrir();
        Linea("=== 1) MODO PRUEBA SIN DRIVES ===");
        Simular(1f);
        grua.SetModoPrueba(true);
        Simular(4f);
        bool ok = true;
        foreach (var e in grua.ejes)
        {
            float real = e.Real;
            bool dentro = real >= e.minimo - 0.01f * (e.angular ? 10 : 1) && real <= e.maximo + 0.01f * (e.angular ? 10 : 1);
            ok &= dentro;
            Linea($"  {e.nombre,-16} real={real,8:0.000}{e.Unidad}  límites [{e.minimo}, {e.maximo}]  {(dentro ? "dentro" : "FUERA DE LÍMITE")}");
        }
        var polea = grua.ejes[GruaController.Polea];
        bool cayo = polea.Real > polea.maximo - 0.05f;
        Linea($"  Izaje cayó hasta su límite inferior: {(cayo ? "sí" : "NO")} (swing cable máx {SwingMax():0.0}°)");
        ok &= cayo;

        grua.SetModoPrueba(false);
        Simular(1f);
        float p0 = polea.Real;
        Simular(2f);
        float deriva = Mathf.Abs(polea.Real - p0);
        Linea($"  Al reactivar drives, deriva del izaje en 2 s: {deriva * 1000f:0.0} mm");
        ok &= deriva < 0.01f;
        Linea(ok ? "  OK" : "  FALLA");
        return ok;
    }

    // ------------------------------------------------------------------ 2
    static bool PruebaRespuesta()
    {
        Abrir();
        Linea("=== 2) RESPUESTA DE LOS DRIVES ===");
        bool ok = true;
        ok &= Escalon(GruaController.Torre, 90f, 1.0f);
        ok &= Escalon(GruaController.Carro, 5.0f, 0.02f);
        ok &= Escalon(GruaController.Polea, 2.5f, 0.03f);
        ok &= Escalon(GruaController.Torre, -60f, 1.0f);
        ok &= Escalon(GruaController.Polea, 0.3f, 0.03f);
        ok &= Escalon(GruaController.Carro, 0.5f, 0.02f);
        Linea($"  Error de seguimiento máx (target vs real, en movimiento): giro {errMax[0]:0.00}°, carro {errMax[1] * 100:0.0} cm, izaje {errMax[2] * 100:0.0} cm");
        Linea(ok ? "  OK" : "  FALLA");
        return ok;
    }

    static bool Escalon(int i, float valor, float tol)
    {
        var e = grua.ejes[i];
        float t0 = t, sobrepaso = 0f, inicio = e.Real;
        e.comando = valor;
        float signo = Mathf.Sign(valor - inicio);
        Simular(40f, () =>
        {
            sobrepaso = Mathf.Max(sobrepaso, signo * (e.Real - valor));
            return EnObjetivo(i, tol);
        });
        bool llego = EnObjetivo(i, tol);
        Simular(1f);
        float final = e.Real;
        Linea($"  {e.nombre,-14} {inicio,7:0.00} → {valor,7:0.00}{e.Unidad}: {(llego ? "llegó" : "NO LLEGÓ")} en {t - t0 - 1f:0.0} s, sobrepaso {Mathf.Max(0, sobrepaso):0.000}, error final {Mathf.Abs(final - valor):0.0000}");
        return llego && Mathf.Abs(final - valor) < tol;
    }

    // ------------------------------------------------------------------ 3
    static bool PruebaTarea()
    {
        Abrir();
        Linea($"=== 3) TAREA COMPLETA ({nEslabones} eslabones de cable) ===");
        Captura("1_inicio");
        var zonaB = tarea.zonaB;
        float r0 = 1.5f;
        float yPolea0 = grua.ejes[GruaController.Polea].cuerpo.transform.position.y;
        float dedoDebajoPolea = yPolea0 - grua.ejes[GruaController.DedoIzq].cuerpo.transform.position.y;
        bool ok = true;

        for (int i = 0; i < tarea.cajas.Length; i++)
        {
            var caja = tarea.cajas[i];
            Vector3 p = caja.rb.position;
            float off = (i - 1) * 0.75f;
            Vector3 destino = zonaB.TransformPoint(new Vector3(0, 0, off));

            Ir(GruaController.Polea, 0.6f);
            IrA(p, r0);
            Asentar();
            Vector3 cab = grua.ejes[GruaController.DedoIzq].cuerpo.transform.parent.position;
            Vector3 delta = caja.rb.position - cab;
            delta.y = 0f;
            Ir(GruaController.Polea, yPolea0 - (p.y + dedoDebajoPolea));
            Asentar(false);
            midiendoAgarre = true;
            vCajaMaxAgarre = 0f;
            grua.SetPinza(true);
            Simular(2f);
            midiendoAgarre = false;
            CapturaCerca($"agarre_{i + 1}", caja.rb.position);
            Linea($"    caja {i + 1}: desalineación horizontal pinza-caja antes de bajar {delta.magnitude * 100:0.0} cm, " +
                  $"dedos al cerrar {grua.ejes[GruaController.DedoIzq].Real:0.000}/{grua.ejes[GruaController.DedoDer].Real:0.000} m, " +
                  $"izaje {grua.ejes[GruaController.Polea].Real:0.00}/{grua.ejes[GruaController.Polea].comando:0.00} m, caja en {caja.rb.position}");
            float vAgarre = vCajaMaxAgarre;
            float yAntes = caja.rb.position.y;
            Ir(GruaController.Polea, 0.6f);
            float alzada = caja.rb.position.y - yAntes;
            if (i == 0) Captura("2_caja_izada");
            IrA(destino, r0);
            Asentar();
            float yDedo = grua.ejes[GruaController.DedoIzq].cuerpo.transform.position.y;
            float deslizamiento = (yDedo - caja.rb.position.y);
            Ir(GruaController.Polea, yPolea0 - (0.27f + dedoDebajoPolea + deslizamiento) - 0.0f);
            Asentar(false);
            grua.SetPinza(false);
            Simular(1.5f);
            Ir(GruaController.Polea, 0.6f);
            Simular(1.5f);
            bool enZona = tarea.DentroDeZonaB(caja.rb.position);
            ok &= enZona && alzada > 1.5f;
            Linea($"  Caja {i + 1}: izada {alzada:0.00} m, v máx al cerrar la pinza {vAgarre:0.00} m/s, " +
                  $"offset vertical del agarre {deslizamiento:+0.000;-0.000} m, quedó en zona B: {(enZona ? "sí" : "NO")} (t={t:0} s)");
        }
        Simular(2f);
        Captura("3_final");
        Linea($"  Mayor espera (en el aire) hasta que el cable deja de pendular: {tiempoAsentar:0.0} s");
        Linea($"  Entregadas: {tarea.Entregadas}/{tarea.cajas.Length}, tarea completa: {tarea.Completa}, cronómetro {tarea.Cronometro:0.0} s");
        Linea($"  Error de seguimiento máx durante la tarea: giro {errMax[0]:0.00}°, carro {errMax[1] * 100:0.0} cm, izaje {errMax[2] * 100:0.0} cm");
        ok &= tarea.Completa;
        Linea(ok ? "  OK" : "  FALLA");
        return ok;
    }

    static void Ir(int i, float valor, float tol = -1f)
    {
        var e = grua.ejes[i];
        if (tol < 0) tol = e.angular ? 1f : 0.04f;
        e.comando = Mathf.Clamp(valor, e.minimo, e.maximo);
        Simular(40f, () => EnObjetivo(i, tol));
    }

    /// <summary>Giro y carro a la vez para quedar encima del punto p.</summary>
    static void IrA(Vector3 p, float r0)
    {
        var giro = grua.ejes[GruaController.Torre];
        var carro = grua.ejes[GruaController.Carro];
        giro.comando = Mathf.Atan2(-p.z, p.x) * Mathf.Rad2Deg;
        carro.comando = Mathf.Clamp(new Vector2(p.x, p.z).magnitude - r0, carro.minimo, carro.maximo);
        Simular(40f, () => EnObjetivo(GruaController.Torre, 0.5f) && EnObjetivo(GruaController.Carro, 0.02f));
    }

    /// <summary>Espera a que el cable deje de pendular (como haría un operador).</summary>
    static void Asentar(bool registrar = true)
    {
        // Quieto = el ángulo de swing varía menos de 0,5° durante 1 s. (Con la carga descentrada
        // el cable queda algo inclinado en equilibrio, por eso no se exige ángulo cero.)
        var ventana = new System.Collections.Generic.Queue<float>();
        float t0 = t;
        Simular(20f, () =>
        {
            ventana.Enqueue(SwingMax());
            if (ventana.Count > Mathf.RoundToInt(1f / Dt)) ventana.Dequeue();
            if (ventana.Count < Mathf.RoundToInt(1f / Dt)) return false;
            float mn = float.MaxValue, mx = float.MinValue;
            foreach (var a in ventana) { mn = Mathf.Min(mn, a); mx = Mathf.Max(mx, a); }
            return mx - mn < 0.5f;
        });
        if (registrar) tiempoAsentar = Mathf.Max(tiempoAsentar, t - t0);
    }

    static float tiempoAsentar;

    /// <summary>Batchmode: mide cómo decae el pendulado (con una caja agarrada) con distintos amortiguamientos pasivos.</summary>
    public static void Pendulo()
    {
        Physics.simulationMode = SimulationMode.Script;
        var cfgs = new (string nombre, float linCab, float fricEsl)[]
        {
            ("actual (fric 0.3, lin 0.8)", -1, -1),
            ("fric 0.05", -1, 0.05f),
            ("fric 0 (se vuelve inestable)", -1, 0f),
            ("lin 0.4", 0.4f, -1),
        };
        foreach (var c in cfgs)
        {
            Abrir();
            var panel = Object.FindFirstObjectByType<PanelEstado>();
            foreach (var e in panel.eslabones)
            {
                if (c.fricEsl >= 0) e.jointFriction = c.fricEsl;
            }
            if (c.linCab >= 0)
                foreach (var b in panel.cabezal.GetComponentsInChildren<ArticulationBody>()) b.linearDamping = c.linCab;
            float yPolea0 = grua.ejes[GruaController.Polea].cuerpo.transform.position.y;
            float dedoDebajoPolea = yPolea0 - grua.ejes[GruaController.DedoIzq].cuerpo.transform.position.y;
            var caja = tarea.cajas[1];
            IrA(caja.rb.position, 1.5f);
            Simular(10f);
            Ir(GruaController.Polea, yPolea0 - (caja.rb.position.y + dedoDebajoPolea));
            Simular(2f);
            grua.SetPinza(true);
            Simular(2f);
            Ir(GruaController.Polea, 0.6f);
            Ir(GruaController.Carro, grua.ejes[GruaController.Carro].comando + 2f);
            var s = new StringBuilder($"  {c.nombre,-28} ángulo máx / vel máx por ventana de 2 s:");
            for (int k = 0; k < 8; k++)
            {
                float m = 0f, v = 0f, mn = 999f;
                Simular(2f, () => { m = Mathf.Max(m, SwingMax()); mn = Mathf.Min(mn, SwingMax()); v = Mathf.Max(v, VelSwingMax()); return false; });
                s.Append($" {mn:0.0}-{m:0.0}°/{v:0}");
            }
            Linea(s.ToString());
        }
        File.WriteAllText(Arg("-reporte") ?? "Logs/pendulo.txt", rep.ToString());
        Physics.simulationMode = SimulationMode.FixedUpdate;
    }

    // ------------------------------------------------------------------ capturas
    /// <summary>Batchmode: renderiza la escena desde varios ángulos de cámara (para elegir la vista inicial).</summary>
    public static void Vistas()
    {
        carpetaCapturas = Arg("-capturas") ?? "Logs/vistas";
        Physics.simulationMode = SimulationMode.Script;
        Abrir();
        Simular(1f);
        var orb = Camera.main.GetComponent<CamaraOrbital>();
        foreach (var yaw in new[] { -30f, 0f, 15f, 30f })
        {
            orb.yaw = yaw;
            orb.Aplicar();
            Captura($"vista_yaw{yaw}");
        }
        Physics.simulationMode = SimulationMode.FixedUpdate;
    }

    static void CapturaCerca(string nombre, Vector3 foco)
    {
        var orb = Camera.main.GetComponent<CamaraOrbital>();
        Vector3 piv = orb.pivote;
        float d = orb.distancia, yaw = orb.yaw;
        orb.pivote = foco + Vector3.up * 0.5f;
        orb.distancia = 4f;
        orb.yaw = Mathf.Atan2(-foco.z, foco.x) * Mathf.Rad2Deg + 90f; // de costado a la dirección radial
        orb.Aplicar();
        Captura(nombre);
        orb.pivote = piv;
        orb.distancia = d;
        orb.yaw = yaw;
        orb.Aplicar();
    }

    static void Captura(string nombre)
    {
        if (string.IsNullOrEmpty(carpetaCapturas)) return;
        try
        {
            var cam = Camera.main;
            var canvas = Object.FindFirstObjectByType<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 0.5f;
            grua.ActualizarUI();
            var panel = Object.FindFirstObjectByType<PanelEstado>();
            typeof(PanelEstado).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(panel, null);
            typeof(TareaManager).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(tarea, null);
            Canvas.ForceUpdateCanvases();
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            cam.targetTexture = null;
            RenderTexture.active = null;
            Directory.CreateDirectory(carpetaCapturas);
            File.WriteAllBytes(Path.Combine(carpetaCapturas, nombre + ".png"), tex.EncodeToPNG());
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
        catch (Exception e)
        {
            Linea("  (captura falló: " + e.Message + ")");
        }
    }
}
