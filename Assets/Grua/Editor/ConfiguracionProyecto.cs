using UnityEditor;
using UnityEngine;

/// <summary>
/// Ajustes de proyecto que necesita el simulador (física, tiempo fijo, WebGL, escena de build).
/// Se aplican al construir la grúa y también al cargar el editor, solo si difieren.
/// </summary>
[InitializeOnLoad]
public static class ConfiguracionProyecto
{
    public const string RutaEscena = "Assets/Scenes/Grua.unity";

    public const float PasoFijo = 0.01f;          // 100 Hz
    public const float PasoMaximo = 0.05f;        // evita la "espiral de la muerte" en WebGL lento
    public const int IteracionesPosicion = 12;
    public const int IteracionesVelocidad = 4;
    public const int SolverTGS = 1;               // Temporal Gauss-Seidel

    static ConfiguracionProyecto()
    {
        EditorApplication.delayCall += () => Aplicar(false);
    }

    [MenuItem("Tools/Grúa/Aplicar configuración de proyecto (física + WebGL)")]
    static void AplicarMenu() => Aplicar(true);

    public static void Aplicar(bool forzarLog)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        bool cambio = false;

        var tiempo = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0]);
        cambio |= SetPasoFijo(tiempo, PasoFijo);
        cambio |= SetFloat(tiempo, "Maximum Allowed Timestep", PasoMaximo);
        tiempo.ApplyModifiedPropertiesWithoutUndo();

        var fisica = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
        cambio |= SetInt(fisica, "m_DefaultSolverIterations", IteracionesPosicion);
        cambio |= SetInt(fisica, "m_DefaultSolverVelocityIterations", IteracionesVelocidad);
        cambio |= SetInt(fisica, "m_SolverType", SolverTGS);
        cambio |= SetInt(fisica, "m_SimulationMode", 0); // FixedUpdate (las pruebas usan Script temporalmente)
        fisica.ApplyModifiedPropertiesWithoutUndo();

        if (PlayerSettings.defaultWebScreenWidth != 1280) { PlayerSettings.defaultWebScreenWidth = 1280; cambio = true; }
        if (PlayerSettings.defaultWebScreenHeight != 720) { PlayerSettings.defaultWebScreenHeight = 720; cambio = true; }
        if (PlayerSettings.WebGL.compressionFormat != WebGLCompressionFormat.Gzip) { PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip; cambio = true; }
        if (!PlayerSettings.WebGL.decompressionFallback) { PlayerSettings.WebGL.decompressionFallback = true; cambio = true; }
        if (!PlayerSettings.runInBackground) { PlayerSettings.runInBackground = true; cambio = true; }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(RutaEscena) != null)
        {
            var escenas = EditorBuildSettings.scenes;
            if (escenas.Length != 1 || escenas[0].path != RutaEscena || !escenas[0].enabled)
            {
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(RutaEscena, true) };
                cambio = true;
            }
        }

        if (cambio)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("[Grúa] Configuración de proyecto aplicada: paso fijo 0.01 s, solver TGS 12/4, WebGL 1280x720 Gzip + fallback, escena Grua en Build Settings.");
        }
        else if (forzarLog)
        {
            Debug.Log("[Grúa] La configuración de proyecto ya estaba aplicada.");
        }
    }

    // En Unity 6 "Fixed Timestep" se serializa como fracción (m_Count / m_Rate); en versiones previas, como float.
    static bool SetPasoFijo(SerializedObject so, float valor)
    {
        var p = so.FindProperty("Fixed Timestep");
        if (p == null) { Debug.LogWarning("[Grúa] No encontré Fixed Timestep"); return false; }
        if (p.propertyType == SerializedPropertyType.Float) return SetFloat(so, "Fixed Timestep", valor);
        var cuenta = p.FindPropertyRelative("m_Count");
        var num = p.FindPropertyRelative("m_Rate.m_Numerator");
        var den = p.FindPropertyRelative("m_Rate.m_Denominator");
        if (cuenta == null || num == null || den == null) { Debug.LogWarning("[Grúa] Formato de Fixed Timestep desconocido"); return false; }
        long objetivo = (long)System.Math.Round(valor * (double)num.longValue / System.Math.Max(1L, den.longValue));
        if (cuenta.longValue == objetivo) return false;
        cuenta.longValue = objetivo;
        return true;
    }

    static bool SetFloat(SerializedObject so, string nombre, float valor)
    {
        var p = so.FindProperty(nombre);
        if (p == null) { Debug.LogWarning($"[Grúa] No encontré la propiedad {nombre}"); return false; }
        if (Mathf.Approximately(p.floatValue, valor)) return false;
        p.floatValue = valor;
        return true;
    }

    static bool SetInt(SerializedObject so, string nombre, int valor)
    {
        var p = so.FindProperty(nombre);
        if (p == null) { Debug.LogWarning($"[Grúa] No encontré la propiedad {nombre}"); return false; }
        if (p.intValue == valor) return false;
        p.intValue = valor;
        return true;
    }
}
