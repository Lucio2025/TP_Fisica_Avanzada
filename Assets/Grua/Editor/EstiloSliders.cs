using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Aspecto de los sliders con los sprites de Assets/Grua/Sprites:
/// riel gris (fondo), barra naranja (relleno) y manija con estado presionado.
/// Lo usa el constructor y también se puede aplicar sobre la escena abierta sin regenerarla.
/// </summary>
public static class EstiloSliders
{
    const string Carpeta = "Assets/Grua/Sprites/";
    const string Riel = Carpeta + "slider_grua1.png";
    const string Relleno = Carpeta + "slider_bar_grua1.png";
    const string Manija = Carpeta + "slider_button-idle_grua1.png";
    const string ManijaPresionada = Carpeta + "slider_button-pressed_grua1.png";

    const float BordeBarra = 8f;   // px de los extremos redondeados (no se estiran)
    const float AltoBarra = 10f;   // px de UI que ocupa la barra (el sprite mide 17)
    const float AnchoManija = 14f;

    [MenuItem("Tools/Grúa/Aplicar sprites a los sliders (escena abierta)")]
    static void AplicarEscenaAbierta()
    {
        int n = AplicarATodos();
        var escena = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
        Debug.Log($"[Grúa] Sprites aplicados a {n} slider(s) y escena guardada.");
    }

    /// <summary>Batchmode: -executeMethod EstiloSliders.AplicarBatch (abre Grua.unity, aplica y guarda).</summary>
    public static void AplicarBatch()
    {
        var escena = EditorSceneManager.OpenScene(ConfiguracionProyecto.RutaEscena);
        int n = AplicarATodos();
        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
        Debug.Log($"[Grúa] Sprites aplicados a {n} slider(s) en {ConfiguracionProyecto.RutaEscena}.");
    }

    static int AplicarATodos()
    {
        var sliders = Object.FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var s in sliders) Aplicar(s);
        return sliders.Length;
    }

    /// <summary>Aplica los sprites a un slider con la estructura de DefaultControls.CreateSlider.</summary>
    public static void Aplicar(Slider s)
    {
        var riel = Cargar(Riel, true);
        var relleno = Cargar(Relleno, true);
        var manija = Cargar(Manija, false);
        var presionada = Cargar(ManijaPresionada, false);
        if (riel == null || relleno == null || manija == null)
        {
            Debug.LogWarning("[Grúa] Faltan sprites en " + Carpeta + "; el slider queda con el estilo por defecto.");
            return;
        }
        Undo.RecordObject(s, "Estilo slider");

        // La barra ocupa el centro vertical del slider; la manija, todo el alto.
        float mitad = AltoBarra / 2f;
        var fondo = s.transform.Find("Background") as RectTransform;
        if (fondo != null)
        {
            CentrarVertical(fondo, mitad);
            fondo.offsetMin = new Vector2(0, fondo.offsetMin.y);
            fondo.offsetMax = new Vector2(0, fondo.offsetMax.y);
            Pintar(fondo.GetComponent<Image>(), riel);
        }

        if (s.fillRect != null)
        {
            var area = s.fillRect.parent as RectTransform;
            CentrarVertical(area, mitad);
            area.offsetMin = new Vector2(0, area.offsetMin.y);
            area.offsetMax = new Vector2(0, area.offsetMax.y);
            s.fillRect.anchoredPosition = Vector2.zero;
            s.fillRect.sizeDelta = Vector2.zero;
            Pintar(s.fillRect.GetComponent<Image>(), relleno);
        }

        if (s.handleRect != null)
        {
            var img = s.handleRect.GetComponent<Image>();
            img.sprite = manija;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.color = Color.white;
            s.handleRect.sizeDelta = new Vector2(AnchoManija, 0f);
            // Mismo recorrido que el relleno: el borde del naranja queda siempre bajo el centro de la manija.
            var area = s.handleRect.parent as RectTransform;
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;
            s.targetGraphic = img;
            s.transition = Selectable.Transition.SpriteSwap;
            s.spriteState = new SpriteState
            {
                highlightedSprite = manija,
                pressedSprite = presionada != null ? presionada : manija,
                selectedSprite = manija,
                disabledSprite = manija,
            };
        }
        EditorUtility.SetDirty(s);
    }

    static void CentrarVertical(RectTransform rt, float mitad)
    {
        rt.anchorMin = new Vector2(rt.anchorMin.x, 0.5f);
        rt.anchorMax = new Vector2(rt.anchorMax.x, 0.5f);
        rt.offsetMin = new Vector2(rt.offsetMin.x, -mitad);
        rt.offsetMax = new Vector2(rt.offsetMax.x, mitad);
    }

    static void Pintar(Image img, Sprite sprite)
    {
        if (img == null) return;
        Undo.RecordObject(img, "Estilo slider");
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
        img.fillCenter = true;
        img.color = Color.white;
        // El sprite mide 17 px de alto y se dibuja en AltoBarra: se escalan también los bordes.
        img.pixelsPerUnitMultiplier = sprite.rect.height / AltoBarra;
        EditorUtility.SetDirty(img);
    }

    /// <summary>Carga el sprite asegurando el tipo de importación (y el borde para 9-slice en las barras).</summary>
    static Sprite Cargar(string ruta, bool barra)
    {
        var imp = AssetImporter.GetAtPath(ruta) as TextureImporter;
        if (imp == null) return null;
        bool cambio = false;
        if (imp.textureType != TextureImporterType.Sprite) { imp.textureType = TextureImporterType.Sprite; cambio = true; }
        if (imp.spriteImportMode != SpriteImportMode.Single) { imp.spriteImportMode = SpriteImportMode.Single; cambio = true; }
        if (imp.mipmapEnabled) { imp.mipmapEnabled = false; cambio = true; }
        if (imp.textureCompression != TextureImporterCompression.Uncompressed) { imp.textureCompression = TextureImporterCompression.Uncompressed; cambio = true; }
        var borde = barra ? new Vector4(BordeBarra, 0, BordeBarra, 0) : Vector4.zero;
        if (imp.spriteBorder != borde) { imp.spriteBorder = borde; cambio = true; }
        if (cambio) imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(ruta);
    }
}
