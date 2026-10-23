# Pendientes (tareas manuales) y problemas conocidos

## Lo que tenés que hacer vos

### 1. Volver a Unity y dejar que importe
Todo lo nuevo (scripts, escena, materiales y ajustes) lo generé con Unity cerrado sobre este proyecto: usé una copia en batchmode, porque el MCP de Unity estaba revocado. Al volver al editor:

1. **Hacé clic en la ventana de Unity**, para que detecte los cambios, importe y compile. Esperá a que termine la barra de progreso.
2. **Revisá la consola.** Tiene que aparecer, como mucho, este mensaje y ningún error en rojo:

   `[Grúa] Configuración de proyecto aplicada: paso fijo 0.01 s, solver TGS 12/4, WebGL 1280x720 Gzip + fallback, escena Grua en Build Settings.`

   Puede aparecer porque el editor abierto todavía tenía en memoria los ajustes viejos y `ConfiguracionProyecto` los vuelve a poner.
3. Si Unity pregunta si querés recargar algo o guardar `SampleScene`, podés **descartar** los cambios de `SampleScene`; no se usa.
4. **Abrí `Assets/Scenes/Grua.unity`** con doble clic.

**Controles rápidos** (si algo no coincide, usá *Tools > Grúa > Aplicar configuración de proyecto*):

- *Edit > Project Settings > Time*: Fixed Timestep = 0,01.
- *Project Settings > Physics*: Solver = Temporal Gauss Seidel, iteraciones 12 / 4, Simulation Mode = Fixed Update.
- *File > Build Profiles*: la única escena es `Scenes/Grua`.
- *Player > Web*: resolución 1280×720, Compression Format = Gzip y Decompression Fallback activado.

### 2. Probar en Play (no lo pude hacer yo)
La física, el control y la tarea los verifiqué con la prueba automática (ver DOCUMENTACION.md §7), pero **no pude entrar en Play** para probar la entrada real de mouse y teclado ni la UI interactiva. Checklist:

- [ ] A/D, W/S, flechas, Q/E y Espacio mueven lo que dice el panel de instrucciones.
- [ ] Los sliders mueven cada drive. Al usar el teclado, el slider correspondiente acompaña.
- [ ] El panel "Articulaciones — target vs. real" se actualiza.
- [ ] El toggle "Modo prueba sin drives" hace caer el gancho hasta el límite; al desactivarlo, la grúa queda donde estaba, sin tirones.
- [ ] La cámara orbita con clic derecho + arrastrar, la rueda hace zoom y R vuelve a la vista inicial.
- [ ] Al dejar una caja quieta en la zona azul, el contador sube después de ~1 s. Con las 3 aparece "¡Tarea completa!".
- [ ] **Reset** recarga la escena. Espacio, después de haber clickeado Reset, **no** debe reiniciar.

### 3. Build WebGL (no la hice, como pediste)
1. **Instalar el módulo:** en Unity Hub, *Installs*, ⚙ de **6000.3.19f1**, *Add modules* y **Web Build Support**. Hoy solo la 6000.4.1f1 lo tiene instalado.
2. **Construir:** desde el menú **Tools > Grúa > Build WebGL**, que genera `Build/WebGL`. Esa carpeta está en el `.gitignore`. También podés usar *File > Build Profiles > Web > Switch Platform > Build*.
   - Alternativa por línea de comandos, con Unity cerrado:
     ```bash
     "/c/Program Files/Unity/Hub/Editor/6000.3.19f1/Editor/Unity.exe" -batchmode -quit -projectPath "E:/UAI-Unity/TP_Fisica_Avanzada" -buildTarget WebGL -executeMethod ConfiguracionProyecto.BuildWebGL -logFile build_webgl.log
     ```
     Esto compila, pero la build en sí no la pude ejecutar porque falta el módulo.
3. **Probar:** abrir `index.html` con doble clic **no funciona** (`file://`). Usá *Build And Run*, que levanta un servidor local, o subilo a itch.io o a GitHub Pages. Gracias al *Decompression Fallback*, funciona aunque el servidor no envíe `Content-Encoding: gzip`.
4. **Verificar en el navegador:**
   - que el clic derecho orbite y **no** abra el menú contextual (la plantilla de Unity lo bloquea sobre el canvas);
   - que se vean las flechas (←↑→↓) en los textos.

### 4. Otros
- **Completar el encabezado de DOCUMENTACION.md** (integrantes, comisión y docente).
- **Borrar la copia de pruebas `E:\UAI-Unity\_GruaPrueba`**, si no la vas a usar. Ocupa varios GB por su `Library/`.
- **(Opcional) Reactivar el MCP de Unity** en *Project Settings > AI > Unity MCP*, si querés que pueda operar el editor directamente.
- **(Opcional) Regenerar la escena** con *Tools > Construir Grúa*, o con *Tools > Grúa > Construir con N eslabones* para 1 o 2 eslabones. Sobrescribe `Assets/Scenes/Grua.unity`.

## Problemas conocidos y limitaciones

- **La carga pendula** después de cada movimiento del carro o del giro, y tarda ~8 s en asentarse. Es física real: conviene mover suave o esperar antes de bajar.
- **El giro es más rápido que en una grúa real** (18°/s contra 4–5°/s), para que la tarea no sea eterna.
- **La disipación del cable es mayor que la real:** fricción de rótula 0,3 y arrastre equivalente 0,8 en la pinza. Con fricción 0, la cadena de Spherical de PhysX se vuelve inestable. Está medido y documentado en DOCUMENTACION.md §3.4.
- **Con la carga descentrada**, el cable queda levemente inclinado en reposo (unos grados). Es el equilibrio estático correcto.
- **Los cuerpos de la grúa no chocan entre sí:** la pinza puede atravesar el mástil si se la lleva contra él. Lo decidí así para que no haya trabas ni contactos espurios.
- **Si se baja el gancho con la pinza apoyada sobre una caja**, la cadena se dobla (el cable "afloja"). Es esperable: hay que subir un poco antes de cerrar.
- **La UI usa la fuente `LegacyRuntime`.** Si en WebGL algún símbolo (flechas, "·", "²") no se ve, cambiá esos textos en `GruaBuilder.cs` y regenerá la escena.
- **La prueba automática corre sin entrar en Play** (`Physics.Simulate` en modo Script). No ejercita la entrada real ni las colisiones que dependen de los callbacks `OnCollision*`, por ejemplo la condición "no toca la grúa" de la entrega.
- **`Assets/TextMesh Pro/`** quedó agregado al repositorio en el primer commit, porque Unity lo importó mientras tanto. No se usa; se puede borrar si molesta.
