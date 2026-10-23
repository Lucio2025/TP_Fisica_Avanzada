# Decisiones tomadas durante el desarrollo

Registro de las decisiones que tomé sin consultar, con el motivo de cada una.

## Herramientas y flujo de trabajo

- **Unity MCP no disponible.** Al empezar, el servidor MCP de Unity respondió *"Connection revoked"*. Se rehabilita en *Project Settings > AI > Unity MCP*, pero no lo toqué porque es un permiso de seguridad tuyo. Para validar sin cerrar tu editor:
  - copié el proyecto (sin `Library/`) a `E:\UAI-Unity\_GruaPrueba`;
  - ahí ejecuté Unity 6000.3.19f1 en `-batchmode`: construye la escena (`GruaBuilder.ConstruirBatch`) y corre una prueba física automática (`PruebaGrua.Ejecutar`);
  - después copié al proyecto real la escena, los materiales y los `.meta` generados.
  - La carpeta `_GruaPrueba` se puede borrar.
- **Los `.meta` de los scripts los escribí a mano** con GUIDs fijos, para que la escena generada en la copia referencie los mismos scripts que el proyecto real.
- **No hice la build WebGL**, como pediste. WebGL queda configurado en Player Settings. Igual hace falta instalar el módulo para la 6000.3.19f1 (ver PENDIENTES.md).
- **Commits directo en `main`**, con mensajes en español.

## Escena y estructura

- **Escena nueva `Assets/Scenes/Grua.unity`**, generada por el menú *Tools > Construir Grúa*. Queda como única escena en Build Settings. `SampleScene` no se tocó.
- **Cantidad de eslabones de cable configurable (1–3, default 3).** Se elige desde el menú *Tools > Grúa > Construir con N eslabón(es)*, que recuerda la elección en EditorPrefs. En batchmode se pasa con `-eslabones N`. El total de ArticulationBodies es 7 + N, o sea entre 8 y 10. La carrera del izaje se recalcula según N, para que la pinza siempre llegue al suelo.
- **Cada joint arranca en 0, y el signo positivo significa "afuera / abajo / cerrar":**
  - giro 0° = pluma hacia +X;
  - carro 0 = estacionado a 1,5 m del mástil;
  - izaje 0 = gancho arriba, positivo = baja;
  - dedos 0 = abiertos, positivo = cierran.
  
  Así lo que se ve en pantalla es exactamente `jointPosition`, sin offsets.
- **Spherical con twist bloqueado y swing limitado a ±40°.** Un cable real casi no transmite torsión. Bloquearla evita que la pinza gire sobre sí misma y quede desalineada con las cajas. El límite de ±40° evita configuraciones absurdas.
- **Los eslabones son pasivos (sin drive), pero con disipación:**
  - **`jointFriction` = 0,3 en las rótulas.** Con 0 la cadena es numéricamente inestable en PhysX: lo medí en la prueba automática, con pendulados de 30–40° y velocidades de más de 1000°/s.
  - **`linearDamping` = 0,8 en cabezal y dedos**, para que la carga deje de pendular en unos 8 s. Con 0,4 tardaba unos 18 s.
  
  Ninguna de las dos es un drive: no tienen target ni rigidez. Las medidas están en DOCUMENTACION.md §3.4.
- **3 eslabones (el default) resultó estable**, así que no hizo falta bajar a 2. Probé 1, 2 y 3 y los tres completan la tarea.
- **El giro tiene `jointFriction` = 0.** La fricción de PhysX es proporcional a la reacción del joint, que en la torre es enorme, y producía ~0,5° de error estático.
- **Los cuerpos de la grúa no colisionan entre sí** (`Physics.IgnoreCollision` entre todos sus colliders al iniciar). Así la pinza no se traba con el mástil y no hay contactos espurios entre eslabones vecinos. Contra el suelo y las cajas sí colisionan.
- **Inercia de la torre calculada a mano** (`automaticInertiaTensor = false`), porque el cálculo automático de Unity asume densidad uniforme de los colliders y no representa el contrapeso. El cálculo está en DOCUMENTACION.md.

## Control

- **Perfil trapezoidal en el controlador.** El slider o el teclado fijan un *comando*. En `FixedUpdate`, el *target* del drive se acerca a ese comando con velocidad y aceleración limitadas, y se envía también `targetVelocity`. Así el drive solo corrige el error residual, no hay tirones y la carga pendula mucho menos, como con los variadores de una grúa real.
- **Velocidades del giro aumentadas a 18°/s.** En una grúa real se usan 4–5°/s, pero así la tarea se completa en pocos minutos. Las demás velocidades son realistas: carro 0,8 m/s, izaje 0,6 m/s y dedos 0,25 m/s.
- **La pinza alterna con Espacio**, como pediste. Hay un slider por dedo porque se pidió "un slider por drive". Espacio mueve los dos a la vez.
- **El apriete de la pinza lo da el resorte del drive:** k = 6000 N/m, con ≈ 510 N al tocar la caja, y un `forceLimit` de 800 N como techo.
  - Primero probé con el drive saturado (k = 20 000 N/m y límite de 500 N). Con dos fuerzas iguales y constantes, la caja quedaba en equilibrio neutro y se corría dentro de la pinza.
  - Con el resorte, la caja se autocentra. El techo de 800 N garantiza que no la atraviesa ni la hace saltar: en la prueba, la caja se movió ≤ 0,10 m/s al cerrar la pinza.
- **Los dedos se centran 0,36 m debajo del cabezal**, no 0,30 m. Con 0,30 m la placa del cabezal tocaba la tapa de la caja agarrada, doblaba el cable unos 4° y generaba vibración.
- **Después de cada clic se limpia la selección del EventSystem.** Si no, Espacio o Enter "pulsarían" el último botón clickeado; por ejemplo, Reset.
- **La entrada se lee con el Input System nuevo.** El proyecto tiene *Active Input Handling = Input System Package*, y el módulo de UI es `InputSystemUIInputModule`.

## Tarea

- **Una caja cuenta como entregada si:**
  - su centro está dentro del rectángulo de la zona B (2,5 × 2,5 m), a menos de 2 m de altura;
  - tiene velocidad < 0,05 m/s y velocidad angular < 0,1 rad/s;
  - **no toca ningún cuerpo de la grúa**, para que no cuente si está colgada quieta sobre la zona;
  - todo lo anterior se cumple durante 1 s seguido.
  
  No hace falta apilarlas. Con 3 entregadas la tarea queda completa y el cronómetro se detiene.
- **El cronómetro arranca con la primera acción del operador**, no al cargar la escena.
- **Reset** recarga la escena, que es la forma más confiable de reiniciar una articulación de PhysX.

## Física

- **Paso fijo de 0,01 s (100 Hz) y paso máximo de 0,05 s.** Con 100 Hz la cadena de cable es estable. El paso máximo evita que un navegador lento acumule demasiados pasos de física por frame.
  - En Unity 6, *Fixed Timestep* se guarda como fracción (`m_Count/m_Rate`). `ConfiguracionProyecto` lo escribe en ese formato.
  - `ConfiguracionProyecto` es un `[InitializeOnLoad]` que reaplica estos ajustes al abrir el editor, solo si difieren. Si querés cambiar uno a mano, también hay que cambiar la constante en `ConfiguracionProyecto.cs`.
- **La prueba automática simula con `Physics.simulationMode = Script`** y al terminar lo vuelve a `FixedUpdate`. Además, `ConfiguracionProyecto` fuerza `FixedUpdate`, por si una prueba se interrumpe.
- **Solver TGS (Temporal Gauss-Seidel), con 12 iteraciones de posición y 4 de velocidad por defecto.** La raíz de la articulación usa 20/4 y las cajas 12/4. TGS es el recomendado por PhysX para articulaciones y apilado.
- **`maxDepenetrationVelocity` = 2 m/s** en cajas y cuerpos articulados, para que una interpenetración no las haga "saltar".
- **Colisión `ContinuousSpeculative`** en cajas y articulaciones.
- **Materiales físicos:** la goma de la pinza tiene μ 1,0 / 0,9 con combinación *Maximum*; las cajas μ 0,6 / 0,5; el suelo μ 0,7 / 0,6. El rebote es 0 en todos.

## UI

- **uGUI con `Text` legacy** (fuente `LegacyRuntime.ttf`), para no depender de importar los recursos de TextMeshPro.
- **El canvas escala con la pantalla** (referencia 1280×720, *match* 0,5). Los paneles son:
  - **arriba a la izquierda:** sliders de los drives y el toggle de modo prueba;
  - **arriba a la derecha:** tabla target vs. real de cada articulación;
  - **arriba al centro:** tarea, con contador, cronómetro y Reset;
  - **abajo a la derecha:** instrucciones.
