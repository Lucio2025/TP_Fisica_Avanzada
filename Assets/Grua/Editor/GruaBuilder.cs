using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Construye la escena completa del simulador de grúa torre (Tools > Construir Grúa).
/// Unidades SI: metros, kilogramos, segundos. Todos los cuerpos articulados tienen escala (1,1,1);
/// la geometría visual/colisión vive en hijos escalados sin ArticulationBody.
/// </summary>
public static class GruaBuilder
{
    // ---------- Geometría (m) ----------
    const float AlturaPedestal = 1.0f;     // pivote del giro (corona)
    const float YPluma = 6.0f;             // altura del eje de la pluma
    const float LargoPluma = 8.3f;
    const float LargoContrapluma = 3.0f;
    const float YCarro = 5.7f;
    const float RadioCarroInicial = 1.5f;  // posición 0 del carro (estacionado junto a la torre)
    const float CarreraCarro = 6.2f;       // radio de trabajo 1.5 … 7.7 m
    const float CaidaPoleaInicial = 0.8f;  // polea a 0.8 m bajo el carro con izaje = 0
    const float OffsetEslabon = 0.25f;     // del centro de la polea al primer pivote del cable
    public const float LargoEslabon = 0.35f;
    const float OffsetDedo = 0.36f;        // del cabezal al centro de los dedos: deja 5 cm entre la tapa de la caja y la placa
    const float AltoDedo = 0.5f;
    const float AperturaDedo = 0.42f;      // x del centro de cada dedo con la pinza abierta
    const float CarreraDedo = 0.22f;
    const float LadoCaja = 0.5f;

    // ---------- Masas (kg) ----------
    const float MasaBase = 8000f, MasaTorre = 3000f, MasaCarro = 120f, MasaPolea = 40f,
        MasaEslabon = 3f, MasaCabezal = 30f, MasaDedo = 8f, MasaCaja = 25f;

    // ---------- Drives (ver DOCUMENTACION.md, sección "Cálculo de drives") ----------
    // Giro: N·m/rad, N·m·s/rad, N·m | Lineales: N/m, N·s/m, N
    static readonly DriveCfg DriveTorre = new DriveCfg(4.0e5f, 2.4e5f, 3.0e4f);
    static readonly DriveCfg DriveCarro = new DriveCfg(8000f, 2800f, 1500f);
    static readonly DriveCfg DrivePolea = new DriveCfg(6.0e4f, 5400f, 4500f);
    // Dedos: el apriete lo da el resorte (k·Δx ≈ 510 N al tocar la caja) y así la caja se autocentra;
    // forceLimit es el techo de seguridad.
    static readonly DriveCfg DriveDedo = new DriveCfg(6000f, 440f, 800f);

    // ---------- Tarea ----------
    const float AnguloZonaA = -50f, RadioZonaA = 5.0f;
    const float AnguloZonaB = 60f, RadioZonaB = 5.5f;

    // Arrastre aerodinámico equivalente del cabezal y dedos: sin él la carga pendula >20 s.
    const float LinDampPinza = 0.8f;

    const string Carpeta = "Assets/Grua";
    const string PrefEslabones = "Grua.Eslabones";

    struct DriveCfg
    {
        public float k, c, f;
        public DriveCfg(float k, float c, float f) { this.k = k; this.c = c; this.f = f; }
    }

    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static PhysicsMaterial matGoma, matCaja, matSuelo, matAcero;
    static Font fuente;

    public static int Eslabones
    {
        get => Mathf.Clamp(EditorPrefs.GetInt(PrefEslabones, 3), 1, 3);
        set => EditorPrefs.SetInt(PrefEslabones, Mathf.Clamp(value, 1, 3));
    }

    /// <summary>Carrera del izaje: los dedos llegan justo al suelo con el gancho abajo.</summary>
    public static float CarreraPolea(int n) => (YCarro - CaidaPoleaInicial) - (OffsetEslabon + n * LargoEslabon + OffsetDedo + AltoDedo / 2f);

    [MenuItem("Tools/Construir Grúa", priority = 0)]
    public static void ConstruirMenu() => Construir(Eslabones);

    [MenuItem("Tools/Grúa/Construir con 1 eslabón de cable")]
    static void Construir1() { Eslabones = 1; Construir(1); }

    [MenuItem("Tools/Grúa/Construir con 2 eslabones de cable")]
    static void Construir2() { Eslabones = 2; Construir(2); }

    [MenuItem("Tools/Grúa/Construir con 3 eslabones de cable (default)")]
    static void Construir3() { Eslabones = 3; Construir(3); }

    /// <summary>Entrada para batchmode: -executeMethod GruaBuilder.ConstruirBatch [-eslabones N]</summary>
    public static void ConstruirBatch()
    {
        int n = 3;
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-eslabones") int.TryParse(args[i + 1], out n);
        Construir(Mathf.Clamp(n, 1, 3));
    }

    public static void Construir(int nEslabones)
    {
        nEslabones = Mathf.Clamp(nEslabones, 1, 3);
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        ConfiguracionProyecto.Aplicar(false);
        CrearAssets();

        var escena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        ConfigurarAmbiente();
        CrearSuelo();
        var luz = new GameObject("Luz direccional");
        var l = luz.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.3f;
        l.shadows = LightShadows.Soft;
        l.color = new Color(1f, 0.96f, 0.9f);
        luz.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var grua = ConstruirGrua(nEslabones, out var refs);
        var cajas = CrearZonasYCajas(out Transform zonaB);
        var cam = CrearCamara();
        CrearUI(refs, cajas, zonaB, grua);

        Directory.CreateDirectory(Path.GetDirectoryName(ConfiguracionProyecto.RutaEscena));
        EditorSceneManager.SaveScene(escena, ConfiguracionProyecto.RutaEscena);
        ConfiguracionProyecto.Aplicar(false);
        Debug.Log($"[Grúa] Escena construida con {nEslabones} eslabón(es) de cable → {7 + nEslabones} ArticulationBodies. Guardada en {ConfiguracionProyecto.RutaEscena}.");
    }

    // ============================================================ Assets

    static void CrearAssets()
    {
        Directory.CreateDirectory(Carpeta + "/Materiales");
        Directory.CreateDirectory(Carpeta + "/Fisica");
        mats.Clear();
        Mat("Amarillo", new Color(0.98f, 0.76f, 0.08f), 0.35f);
        Mat("Hormigon", new Color(0.62f, 0.62f, 0.6f), 0.1f);
        Mat("MetalOscuro", new Color(0.22f, 0.24f, 0.27f), 0.5f);
        Mat("Rojo", new Color(0.78f, 0.13f, 0.1f), 0.4f);
        Mat("Goma", new Color(0.08f, 0.08f, 0.08f), 0.05f);
        Mat("Madera", new Color(0.72f, 0.5f, 0.28f), 0.15f);
        Mat("Suelo", new Color(0.42f, 0.47f, 0.4f), 0.05f);
        Mat("ZonaA", new Color(0.25f, 0.68f, 0.3f), 0.1f);
        Mat("ZonaB", new Color(0.2f, 0.45f, 0.9f), 0.1f);
        Mat("Vidrio", new Color(0.55f, 0.75f, 0.9f), 0.8f);
        Mat("Cable", new Color(0.08f, 0.08f, 0.08f), 0f, true);

        // Fricción: goma de la pinza (alta, combina con Maximum para dominar el contacto),
        // cajas de madera, suelo de hormigón/tierra compactada. Sin rebote en ningún caso.
        matGoma = PMat("Goma", 1.0f, 0.9f, PhysicsMaterialCombine.Maximum);
        matCaja = PMat("Caja", 0.6f, 0.5f, PhysicsMaterialCombine.Average);
        matSuelo = PMat("Suelo", 0.7f, 0.6f, PhysicsMaterialCombine.Average);
        matAcero = PMat("Acero", 0.4f, 0.3f, PhysicsMaterialCombine.Average);

        fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        AssetDatabase.SaveAssets();
    }

    static Material Mat(string nombre, Color color, float suavidad, bool unlit = false)
    {
        string ruta = $"{Carpeta}/Materiales/{nombre}.mat";
        var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, ruta);
        }
        m.shader = shader;
        m.SetColor("_BaseColor", color);
        m.color = color;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", suavidad);
        EditorUtility.SetDirty(m);
        mats[nombre] = m;
        return m;
    }

    static PhysicsMaterial PMat(string nombre, float estatica, float dinamica, PhysicsMaterialCombine combinar)
    {
        string ruta = $"{Carpeta}/Fisica/{nombre}.physicMaterial";
        var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(ruta);
        if (m == null)
        {
            m = new PhysicsMaterial(nombre);
            AssetDatabase.CreateAsset(m, ruta);
        }
        m.staticFriction = estatica;
        m.dynamicFriction = dinamica;
        m.bounciness = 0f;
        m.frictionCombine = combinar;
        m.bounceCombine = PhysicsMaterialCombine.Minimum;
        EditorUtility.SetDirty(m);
        return m;
    }

    static void ConfigurarAmbiente()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.78f);
        RenderSettings.ambientEquatorColor = new Color(0.45f, 0.45f, 0.45f);
        RenderSettings.ambientGroundColor = new Color(0.25f, 0.24f, 0.22f);
        var cielo = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
        if (cielo != null) RenderSettings.skybox = cielo;
    }

    // ============================================================ Geometría auxiliar

    static GameObject Vacio(string nombre, Transform padre, Vector3 posMundo)
    {
        var go = new GameObject(nombre);
        if (padre != null) go.transform.SetParent(padre, false);
        go.transform.position = posMundo;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go;
    }

    /// <summary>Pieza visual (con o sin collider) hija de un cuerpo. Posición y rotación locales.</summary>
    static GameObject Pieza(string nombre, Transform padre, PrimitiveType tipo, Vector3 pos, Vector3 escala,
        string material, PhysicsMaterial pm = null, bool collider = true, Vector3? euler = null)
    {
        var go = GameObject.CreatePrimitive(tipo);
        go.name = nombre;
        go.transform.SetParent(padre, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
        go.transform.localScale = escala;
        go.GetComponent<MeshRenderer>().sharedMaterial = mats[material];
        var col = go.GetComponent<Collider>();
        if (!collider) UnityEngine.Object.DestroyImmediate(col);
        else if (pm != null) col.sharedMaterial = pm;
        return go;
    }

    static ArticulationDrive Drive(float min, float max, DriveCfg cfg) => new ArticulationDrive
    {
        lowerLimit = min,
        upperLimit = max,
        stiffness = cfg.k,
        damping = cfg.c,
        forceLimit = cfg.f,
        target = 0f,
        targetVelocity = 0f,
        driveType = ArticulationDriveType.Force,
    };

    static ArticulationDrive DriveLibre(float min, float max) => new ArticulationDrive
    {
        lowerLimit = min,
        upperLimit = max,
        stiffness = 0f,
        damping = 0f,
        forceLimit = 0f,
        driveType = ArticulationDriveType.Force,
    };

    static ArticulationBody Cuerpo(GameObject go, float masa)
    {
        var ab = go.AddComponent<ArticulationBody>();
        ab.mass = masa;
        ab.useGravity = true;
        ab.linearDamping = 0.05f;
        ab.angularDamping = 0.05f;
        ab.jointFriction = 0.05f;
        ab.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        ab.maxDepenetrationVelocity = 2f;
        ab.matchAnchors = true;
        ab.anchorPosition = Vector3.zero;
        return ab;
    }

    // ============================================================ Grúa

    public class Refs
    {
        public ArticulationBody baseAB, torre, carro, polea, cabezal, dedoIzq, dedoDer;
        public List<ArticulationBody> eslabones = new List<ArticulationBody>();
        public int nEslabones;
    }

    static GameObject ConstruirGrua(int n, out Refs r)
    {
        r = new Refs { nEslabones = n };

        // --- Base (raíz, immovable): zapata de hormigón + pedestal ---
        var goBase = Vacio("Grua_Base (raiz)", null, Vector3.zero);
        Pieza("Zapata", goBase.transform, PrimitiveType.Cube, new Vector3(0, 0.2f, 0), new Vector3(2.6f, 0.4f, 2.6f), "Hormigon", matAcero);
        Pieza("Pedestal", goBase.transform, PrimitiveType.Cube, new Vector3(0, 0.7f, 0), new Vector3(1.0f, 0.6f, 1.0f), "MetalOscuro", matAcero);
        r.baseAB = Cuerpo(goBase, MasaBase);
        r.baseAB.immovable = true;
        r.baseAB.solverIterations = 20;
        r.baseAB.solverVelocityIterations = 4;

        // --- Torre giratoria (Revolute sobre Y) ---
        var goTorre = Vacio("Torre (revolute Y)", goBase.transform, new Vector3(0, AlturaPedestal, 0));
        float yP = YPluma - AlturaPedestal; // altura local de la pluma
        Pieza("Corona", goTorre.transform, PrimitiveType.Cylinder, new Vector3(0, 0.05f, 0), new Vector3(1.2f, 0.05f, 1.2f), "MetalOscuro", matAcero);
        Pieza("Mastil", goTorre.transform, PrimitiveType.Cube, new Vector3(0, 0.1f + (yP - 0.1f) / 2f, 0), new Vector3(0.6f, yP - 0.1f, 0.6f), "Amarillo", matAcero);
        for (int i = 1; i <= 4; i++) // riostras visuales del mástil
            Pieza("Riostra" + i, goTorre.transform, PrimitiveType.Cube, new Vector3(0, i * 1.0f, 0), new Vector3(0.66f, 0.06f, 0.66f), "MetalOscuro", null, false);
        Pieza("Cabina", goTorre.transform, PrimitiveType.Cube, new Vector3(0.04f, yP - 0.55f, 0.05f), new Vector3(0.6f, 0.5f, 0.62f), "Vidrio", null, false);
        Pieza("Pluma", goTorre.transform, PrimitiveType.Cube, new Vector3(LargoPluma / 2f, yP, 0), new Vector3(LargoPluma, 0.35f, 0.35f), "Amarillo", matAcero);
        Pieza("Contrapluma", goTorre.transform, PrimitiveType.Cube, new Vector3(-LargoContrapluma / 2f, yP, 0), new Vector3(LargoContrapluma, 0.3f, 0.5f), "Amarillo", matAcero);
        Pieza("Contrapeso", goTorre.transform, PrimitiveType.Cube, new Vector3(-2.6f, yP - 0.55f, 0), new Vector3(0.85f, 0.9f, 0.8f), "Hormigon", matAcero);
        Pieza("Torreta", goTorre.transform, PrimitiveType.Cube, new Vector3(0, yP + 0.8f, 0), new Vector3(0.25f, 1.3f, 0.25f), "Amarillo", null, false);
        Tirante(goTorre.transform, new Vector3(0, yP + 1.4f, 0), new Vector3(LargoPluma * 0.85f, yP + 0.17f, 0));
        Tirante(goTorre.transform, new Vector3(0, yP + 1.4f, 0), new Vector3(-LargoContrapluma * 0.9f, yP + 0.15f, 0));
        r.torre = Cuerpo(goTorre, MasaTorre);
        r.torre.jointType = ArticulationJointType.RevoluteJoint;
        r.torre.jointFriction = 0f; // rodamiento de giro: la fricción proporcional a la carga axial daría error estático
        r.torre.anchorRotation = Quaternion.Euler(0, 0, 90); // eje X del ancla → Y del mundo
        r.torre.twistLock = ArticulationDofLock.LimitedMotion;
        r.torre.xDrive = Drive(-170f, 170f, DriveTorre);
        // Inercia calculada a mano (ver documentación): pluma + contrapluma + contrapeso + mástil.
        r.torre.automaticCenterOfMass = false;
        r.torre.centerOfMass = new Vector3(-0.4f, 4.0f, 0f);
        r.torre.automaticInertiaTensor = false;
        r.torre.inertiaTensor = new Vector3(40000f, 26500f, 40000f);
        r.torre.inertiaTensorRotation = Quaternion.identity;

        // --- Carro (Prismatic a lo largo de la pluma, X) ---
        var goCarro = Vacio("Carro (prismatic X)", goTorre.transform, new Vector3(RadioCarroInicial, YCarro, 0));
        Pieza("Bastidor", goCarro.transform, PrimitiveType.Cube, new Vector3(0, 0.02f, 0), new Vector3(0.6f, 0.2f, 0.5f), "MetalOscuro", matAcero);
        for (int s = -1; s <= 1; s += 2)
            Pieza("Rueda" + s, goCarro.transform, PrimitiveType.Cylinder, new Vector3(0, 0.1f, s * 0.23f), new Vector3(0.16f, 0.0315f, 0.16f), "Rojo", null, false, new Vector3(90, 0, 0));
        r.carro = Cuerpo(goCarro, MasaCarro);
        r.carro.jointType = ArticulationJointType.PrismaticJoint;
        r.carro.anchorRotation = Quaternion.identity;
        r.carro.linearLockX = ArticulationDofLock.LimitedMotion;
        r.carro.linearLockY = ArticulationDofLock.LockedMotion;
        r.carro.linearLockZ = ArticulationDofLock.LockedMotion;
        r.carro.xDrive = Drive(0f, CarreraCarro, DriveCarro);

        // --- Polea / aparejo de izaje (Prismatic vertical, positivo = baja) ---
        float yPolea = YCarro - CaidaPoleaInicial;
        var goPolea = Vacio("Polea izaje (prismatic Y)", goCarro.transform, new Vector3(RadioCarroInicial, yPolea, 0));
        Pieza("Bloque", goPolea.transform, PrimitiveType.Cube, Vector3.zero, new Vector3(0.3f, 0.36f, 0.22f), "Rojo", matAcero);
        Pieza("Roldana", goPolea.transform, PrimitiveType.Cylinder, new Vector3(0, 0.08f, 0), new Vector3(0.3f, 0.13f, 0.3f), "MetalOscuro", null, false, new Vector3(90, 0, 0));
        r.polea = Cuerpo(goPolea, MasaPolea);
        r.polea.jointType = ArticulationJointType.PrismaticJoint;
        r.polea.anchorRotation = Quaternion.Euler(0, 0, -90); // eje X del ancla → -Y del mundo
        r.polea.linearLockX = ArticulationDofLock.LimitedMotion;
        r.polea.linearLockY = ArticulationDofLock.LockedMotion;
        r.polea.linearLockZ = ArticulationDofLock.LockedMotion;
        r.polea.xDrive = Drive(0f, CarreraPolea(n), DrivePolea);

        // Cable visual carro → polea
        var goCable = new GameObject("CableIzaje (visual)");
        goCable.transform.SetParent(goCarro.transform, false);
        var lr = goCable.AddComponent<LineRenderer>();
        lr.sharedMaterial = mats["Cable"];
        lr.widthMultiplier = 0.025f;
        lr.positionCount = 4;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        var cv = goCable.AddComponent<CableVisual>();
        var anclaArriba = Vacio("AnclaCable", goCarro.transform, new Vector3(RadioCarroInicial, YCarro - 0.05f, 0));
        var anclaAbajo = Vacio("AnclaCable", goPolea.transform, new Vector3(RadioCarroInicial, yPolea + 0.15f, 0));
        cv.desde = anclaArriba.transform;
        cv.hasta = anclaAbajo.transform;
        lr.SetPositions(new[] { anclaArriba.transform.position, anclaAbajo.transform.position, anclaAbajo.transform.position, anclaArriba.transform.position });

        // --- Eslabones de cable (Spherical, pasivos) ---
        Transform padre = goPolea.transform;
        float y = yPolea - OffsetEslabon;
        for (int i = 0; i < n; i++)
        {
            var go = Vacio($"Cable {i + 1} (spherical pasivo)", padre, new Vector3(RadioCarroInicial, y, 0));
            Pieza("Grillete", go.transform, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.08f, "MetalOscuro", null, false);
            Pieza("Tramo", go.transform, PrimitiveType.Cylinder, new Vector3(0, -LargoEslabon / 2f, 0), new Vector3(0.04f, LargoEslabon / 2f, 0.04f), "Cable", matAcero);
            var ab = Cuerpo(go, MasaEslabon);
            ab.jointType = ArticulationJointType.SphericalJoint;
            ab.anchorRotation = Quaternion.Euler(0, 0, -90); // twist = vertical
            ab.twistLock = ArticulationDofLock.LockedMotion;
            ab.swingYLock = ArticulationDofLock.LimitedMotion;
            ab.swingZLock = ArticulationDofLock.LimitedMotion;
            ab.yDrive = DriveLibre(-40f, 40f);
            ab.zDrive = DriveLibre(-40f, 40f);
            // Disipación pasiva (rozamiento interno del cable y en el grillete); no es un drive.
            ab.angularDamping = 0.6f;
            ab.jointFriction = 0.3f;
            ab.automaticInertiaTensor = false;
            ab.inertiaTensor = new Vector3(0.04f, 0.04f, 0.04f);
            ab.inertiaTensorRotation = Quaternion.identity;
            r.eslabones.Add(ab);
            padre = go.transform;
            y -= LargoEslabon;
        }

        // --- Cabezal de la pinza (Fixed) ---
        var goCab = Vacio("Cabezal pinza (fixed)", padre, new Vector3(RadioCarroInicial, y, 0));
        Pieza("Placa", goCab.transform, PrimitiveType.Cube, Vector3.zero, new Vector3(1.0f, 0.12f, 0.4f), "Amarillo", matAcero);
        Pieza("Guia", goCab.transform, PrimitiveType.Cube, new Vector3(0, -0.1f, 0), new Vector3(0.95f, 0.06f, 0.12f), "MetalOscuro", null, false);
        Pieza("Ojal", goCab.transform, PrimitiveType.Sphere, new Vector3(0, 0.08f, 0), Vector3.one * 0.1f, "MetalOscuro", null, false);
        r.cabezal = Cuerpo(goCab, MasaCabezal);
        r.cabezal.jointType = ArticulationJointType.FixedJoint;
        r.cabezal.linearDamping = LinDampPinza;

        // --- Dedos (Prismatic, positivo = cierra) ---
        r.dedoIzq = Dedo("Dedo izquierdo (prismatic)", goCab.transform, y, -1);
        r.dedoDer = Dedo("Dedo derecho (prismatic)", goCab.transform, y, +1);

        return goBase;
    }

    static ArticulationBody Dedo(string nombre, Transform cabezal, float yCabezal, int lado)
    {
        var go = Vacio(nombre, cabezal, new Vector3(RadioCarroInicial + lado * AperturaDedo, yCabezal - OffsetDedo, 0));
        float topeBrazo = OffsetDedo - 0.06f, pieBrazo = -AltoDedo / 2f + 0.1f; // el brazo sube hasta la placa
        Pieza("Brazo", go.transform, PrimitiveType.Cube, new Vector3(lado * 0.01f, (topeBrazo + pieBrazo) / 2f, 0), new Vector3(0.04f, topeBrazo - pieBrazo, 0.3f), "MetalOscuro", matAcero);
        // Almohadilla de goma en la cara interna: es la que toca la caja.
        Pieza("Almohadilla", go.transform, PrimitiveType.Cube, new Vector3(-lado * 0.02f, 0, 0), new Vector3(0.03f, AltoDedo, 0.35f), "Goma", matGoma);
        var ab = Cuerpo(go, MasaDedo);
        ab.jointType = ArticulationJointType.PrismaticJoint;
        // Dedo izquierdo cierra hacia +X; el derecho tiene el ancla girada 180° y cierra hacia -X.
        ab.anchorRotation = lado < 0 ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
        ab.linearLockX = ArticulationDofLock.LimitedMotion;
        ab.linearLockY = ArticulationDofLock.LockedMotion;
        ab.linearLockZ = ArticulationDofLock.LockedMotion;
        ab.xDrive = Drive(0f, CarreraDedo, DriveDedo);
        ab.linearDamping = LinDampPinza;
        return ab;
    }

    static void Tirante(Transform padre, Vector3 a, Vector3 b)
    {
        Vector3 medio = (a + b) / 2f;
        Vector3 dir = b - a;
        var go = Pieza("Tirante", padre, PrimitiveType.Cylinder, medio, new Vector3(0.04f, dir.magnitude / 2f, 0.04f), "MetalOscuro", null, false);
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
    }

    // ============================================================ Escenario y tarea

    static void CrearSuelo()
    {
        var suelo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        suelo.name = "Suelo";
        suelo.transform.position = new Vector3(0, -0.1f, 0);
        suelo.transform.localScale = new Vector3(40f, 0.2f, 40f);
        suelo.GetComponent<MeshRenderer>().sharedMaterial = mats["Suelo"];
        suelo.GetComponent<Collider>().sharedMaterial = matSuelo;
        suelo.isStatic = true;
    }

    /// <summary>Punto a radio r y ángulo θ (θ = posición del joint de giro de la torre, en grados).</summary>
    public static Vector3 Polar(float r, float anguloGrados) => Quaternion.Euler(0, anguloGrados, 0) * new Vector3(r, 0, 0);

    static Caja[] CrearZonasYCajas(out Transform zonaB)
    {
        var raiz = new GameObject("Tarea");

        var zA = Vacio("Zona A (origen)", raiz.transform, Polar(RadioZonaA, AnguloZonaA));
        zA.transform.rotation = Quaternion.Euler(0, AnguloZonaA, 0);
        Pieza("Marca", zA.transform, PrimitiveType.Cube, new Vector3(0, 0.005f, 0), new Vector3(1.6f, 0.01f, 3.6f), "ZonaA", null, false);

        var zB = Vacio("Zona B (destino)", raiz.transform, Polar(RadioZonaB, AnguloZonaB));
        zB.transform.rotation = Quaternion.Euler(0, AnguloZonaB, 0);
        Pieza("Marca", zB.transform, PrimitiveType.Cube, new Vector3(0, 0.005f, 0), new Vector3(2.5f, 0.01f, 2.5f), "ZonaB", null, false);
        zonaB = zB.transform;

        // Tres cajas en la zona A, lado a lado en dirección tangencial; cada una mirando al mástil,
        // para que los dedos (que cierran en dirección radial) apoyen de plano.
        var cajas = new Caja[3];
        for (int i = 0; i < 3; i++)
        {
            float off = (i - 1) * 1.1f;
            Vector3 p = Polar(RadioZonaA, AnguloZonaA) + Quaternion.Euler(0, AnguloZonaA, 0) * new Vector3(0, 0, off);
            float ang = Mathf.Atan2(-p.z, p.x) * Mathf.Rad2Deg;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Caja {i + 1}";
            go.transform.SetParent(raiz.transform, true);
            go.transform.position = new Vector3(p.x, LadoCaja / 2f + 0.002f, p.z);
            go.transform.rotation = Quaternion.Euler(0, ang, 0);
            go.transform.localScale = Vector3.one * LadoCaja;
            go.GetComponent<MeshRenderer>().sharedMaterial = mats["Madera"];
            go.GetComponent<Collider>().sharedMaterial = matCaja;
            for (int k = -1; k <= 1; k += 2) // flejes visuales
                Pieza("Fleje", go.transform, PrimitiveType.Cube, new Vector3(0, 0, k * 0.3f), new Vector3(1.01f, 1.01f, 0.08f), "MetalOscuro", null, false);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = MasaCaja;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.maxDepenetrationVelocity = 2f;
            rb.solverIterations = 12;
            rb.solverVelocityIterations = 4;
            var c = go.AddComponent<Caja>();
            c.rb = rb;
            cajas[i] = c;
        }
        return cajas;
    }

    static Camera CrearCamara()
    {
        var go = new GameObject("Main Camera");
        go.tag = "MainCamera";
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 50f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 300f;
        cam.clearFlags = CameraClearFlags.Skybox;
        go.AddComponent<AudioListener>();
        var orb = go.AddComponent<CamaraOrbital>();
        orb.Aplicar();
        return cam;
    }

    // ============================================================ UI (uGUI, referencia 1280x720)

    static readonly Color ColorPanel = new Color(0.04f, 0.06f, 0.09f, 0.82f);
    static readonly Color ColorTitulo = new Color(1f, 0.82f, 0.25f);

    static DefaultControls.Resources RecursosUI() => new DefaultControls.Resources
    {
        standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
        background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
        knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
        checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
        inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
        dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
        mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
    };

    static RectTransform Colocar(GameObject go, Transform padre, Vector2 ancla, Vector2 pos, Vector2 tam)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.SetParent(padre, false);
        rt.anchorMin = rt.anchorMax = ancla;
        rt.pivot = ancla;
        rt.anchoredPosition = pos;
        rt.sizeDelta = tam;
        return rt;
    }

    static RectTransform Panel(string nombre, Transform padre, Vector2 ancla, Vector2 pos, Vector2 tam)
    {
        var go = new GameObject(nombre, typeof(RectTransform), typeof(Image));
        var img = go.GetComponent<Image>();
        img.color = ColorPanel;
        img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        img.type = Image.Type.Sliced;
        return Colocar(go, padre, ancla, pos, tam);
    }

    /// <summary>Texto anclado arriba a la izquierda del padre.</summary>
    static Text Texto(string nombre, Transform padre, string contenido, int tam, Vector2 pos, Vector2 caja,
        TextAnchor alineacion = TextAnchor.UpperLeft, Color? color = null, FontStyle estilo = FontStyle.Normal)
    {
        var go = new GameObject(nombre, typeof(RectTransform), typeof(Text));
        Colocar(go, padre, new Vector2(0, 1), pos, caja);
        var t = go.GetComponent<Text>();
        t.font = fuente;
        t.fontSize = tam;
        t.fontStyle = estilo;
        t.text = contenido;
        t.alignment = alineacion;
        t.color = color ?? Color.white;
        t.supportRichText = true;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static void CrearUI(Refs r, Caja[] cajas, Transform zonaB, GameObject goGrua)
    {
        var res = RecursosUI();

        var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
        var modulo = es.AddComponent<InputSystemUIInputModule>();
        modulo.AssignDefaultActions();

        var goCanvas = new GameObject("UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = goCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = goCanvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        var raizUI = goCanvas.transform;

        var controlador = goGrua.AddComponent<GruaController>();
        controlador.raiz = r.baseAB;

        // ---- Panel de controles (arriba a la izquierda) ----
        var pc = Panel("Panel Controles", raizUI, new Vector2(0, 1), new Vector2(10, -10), new Vector2(400, 392));
        Texto("Titulo", pc, "CONTROL DE DRIVES", 17, new Vector2(12, -8), new Vector2(380, 24), color: ColorTitulo, estilo: FontStyle.Bold);

        float vMaxGiro = 18f, aGiro = 20f;
        var ejes = new[]
        {
            Eje("Giro torre", "Revolute", "A/D  ←/→", r.torre, true, -170f, 170f, vMaxGiro, aGiro),
            Eje("Carro", "Prismatic", "W/S  ↑/↓", r.carro, false, 0f, CarreraCarro, 0.8f, 1.0f),
            Eje("Izaje (polea)", "Prismatic", "E baja / Q sube", r.polea, false, 0f, CarreraPolea(r.nEslabones), 0.6f, 1.0f),
            Eje("Dedo izquierdo", "Prismatic", "Espacio", r.dedoIzq, false, 0f, CarreraDedo, 0.25f, 1.0f),
            Eje("Dedo derecho", "Prismatic", "Espacio", r.dedoDer, false, 0f, CarreraDedo, 0.25f, 1.0f),
        };
        float yFila = -38f;
        foreach (var eje in ejes)
        {
            eje.etiqueta = Texto("Etiqueta " + eje.nombre, pc, eje.nombre, 13, new Vector2(12, yFila), new Vector2(380, 36));
            var goS = DefaultControls.CreateSlider(res);
            goS.name = "Slider " + eje.nombre;
            Colocar(goS, pc, new Vector2(0, 1), new Vector2(14, yFila - 34), new Vector2(370, 24));
            var s = goS.GetComponent<Slider>();
            s.navigation = new Navigation { mode = Navigation.Mode.None };
            s.minValue = eje.minimo;
            s.maxValue = eje.maximo;
            s.value = 0f;
            EstiloSliders.Aplicar(s);
            eje.slider = s;
            yFila -= 60f;
        }
        controlador.ejes = ejes;

        controlador.textoPinza = Texto("Estado pinza", pc, "Pinza: ABIERTA", 13, new Vector2(12, yFila), new Vector2(380, 20));
        var goT = DefaultControls.CreateToggle(res);
        goT.name = "Toggle Modo prueba";
        Colocar(goT, pc, new Vector2(0, 1), new Vector2(12, yFila - 24), new Vector2(380, 22));
        var tog = goT.GetComponent<Toggle>();
        tog.isOn = false;
        tog.navigation = new Navigation { mode = Navigation.Mode.None };
        var lblT = goT.GetComponentInChildren<Text>();
        if (lblT != null)
        {
            lblT.font = fuente;
            lblT.fontSize = 13;
            lblT.color = new Color(1f, 0.75f, 0.35f);
            lblT.text = "Modo prueba sin drives (cae por gravedad, respeta límites)";
        }
        controlador.toggleModoPrueba = tog;

        // ---- Panel de estado: target vs. real (arriba a la derecha) ----
        var pe = Panel("Panel Estado", raizUI, new Vector2(1, 1), new Vector2(-10, -10), new Vector2(484, 212));
        Texto("Titulo", pe, "ARTICULACIONES — TARGET vs. REAL", 15, new Vector2(10, -8), new Vector2(460, 22), color: ColorTitulo, estilo: FontStyle.Bold);
        var panel = goGrua.AddComponent<PanelEstado>();
        panel.grua = controlador;
        panel.baseRaiz = r.baseAB;
        panel.eslabones = r.eslabones.ToArray();
        panel.cabezal = r.cabezal;
        float yc = -32f, alto = 200f;
        panel.columnaNombre = Texto("ColNombre", pe, "", 13, new Vector2(10, yc), new Vector2(118, alto));
        panel.columnaTipo = Texto("ColTipo", pe, "", 13, new Vector2(128, yc), new Vector2(84, alto));
        panel.columnaTarget = Texto("ColTarget", pe, "", 13, new Vector2(208, yc), new Vector2(80, alto), TextAnchor.UpperRight);
        panel.columnaReal = Texto("ColReal", pe, "", 13, new Vector2(290, yc), new Vector2(104, alto), TextAnchor.UpperRight);
        panel.columnaFuerza = Texto("ColFuerza", pe, "", 13, new Vector2(396, yc), new Vector2(80, alto), TextAnchor.UpperRight);

        // ---- Panel de tarea (arriba al centro) ----
        var pt = Panel("Panel Tarea", raizUI, new Vector2(0.5f, 1), new Vector2(-20, -10), new Vector2(330, 92));
        var tarea = goGrua.AddComponent<TareaManager>();
        tarea.grua = controlador;
        tarea.cajas = cajas;
        tarea.zonaB = zonaB;
        tarea.mitadTamano = new Vector2(1.25f, 1.25f);
        Texto("Titulo", pt, "TAREA: llevar las 3 cajas de la zona A (verde) a la B (azul)", 12, new Vector2(10, -6), new Vector2(310, 32), color: ColorTitulo);
        tarea.textoContador = Texto("Contador", pt, "Cajas entregadas: 0 / 3", 16, new Vector2(10, -38), new Vector2(210, 24));
        tarea.textoTiempo = Texto("Tiempo", pt, "Tiempo: 00:00.0", 14, new Vector2(10, -62), new Vector2(220, 24));
        var goB = DefaultControls.CreateButton(res);
        goB.name = "Boton Reset";
        Colocar(goB, pt, new Vector2(0, 1), new Vector2(232, -42), new Vector2(86, 38));
        var btn = goB.GetComponent<Button>();
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        var lblB = goB.GetComponentInChildren<Text>();
        if (lblB != null)
        {
            lblB.font = fuente;
            lblB.fontSize = 15;
            lblB.fontStyle = FontStyle.Bold;
            lblB.text = "Reset";
            lblB.color = new Color(0.1f, 0.1f, 0.1f);
        }
        tarea.botonReset = btn;

        var msg = Texto("Mensaje", raizUI, "", 30, Vector2.zero, new Vector2(700, 50), TextAnchor.MiddleCenter, new Color(0.5f, 1f, 0.5f), FontStyle.Bold);
        var rtm = msg.rectTransform;
        rtm.anchorMin = rtm.anchorMax = rtm.pivot = new Vector2(0.5f, 1f);
        rtm.anchoredPosition = new Vector2(0, -112);
        msg.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.8f);
        tarea.textoMensaje = msg;

        // ---- Instrucciones (abajo a la derecha) ----
        var pi = Panel("Panel Instrucciones", raizUI, new Vector2(1, 0), new Vector2(-10, 10), new Vector2(410, 178));
        Texto("Titulo", pi, "INSTRUCCIONES", 15, new Vector2(10, -8), new Vector2(360, 22), color: ColorTitulo, estilo: FontStyle.Bold);
        Texto("Cuerpo", pi,
            "<b>A/D</b> o <b>←/→</b>: girar la torre\n" +
            "<b>W/S</b> o <b>↑/↓</b>: carro hacia afuera / adentro\n" +
            "<b>E</b>: bajar gancho   <b>Q</b>: subir gancho\n" +
            "<b>Espacio</b>: abrir / cerrar la pinza\n" +
            "<b>Clic derecho + arrastrar</b>: orbitar cámara · <b>Rueda</b>: zoom · <b>R</b>: vista inicial\n" +
            "También podés usar los sliders (uno por drive).\n" +
            "Una caja cuenta cuando queda quieta 1 s dentro de la zona B y suelta.",
            13, new Vector2(10, -32), new Vector2(390, 140));
    }

    static EjeControlado Eje(string nombre, string tipo, string teclas, ArticulationBody ab, bool angular,
        float min, float max, float vMax, float aMax) => new EjeControlado
    {
        nombre = nombre,
        tipo = tipo,
        teclas = teclas,
        cuerpo = ab,
        angular = angular,
        minimo = min,
        maximo = max,
        velocidadMax = vMax,
        aceleracionMax = aMax,
    };
}
