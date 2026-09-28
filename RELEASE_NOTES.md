# SharpCommander - Notas de versión

---

# SharpCommander v2.3.0

## 🚀 Resumen

La 2.3.0 cambia la cara de la aplicación: la barra de botones da paso a una **cinta de comandos** con grupos, las carpetas y los archivos estrenan **iconos a color**, y **SpaceAnalyzer** queda integrado para ver de un vistazo qué ocupa espacio. Además, la aplicación **te avisa de las versiones nuevas** cada día y la **web** se ha rehecho en español e inglés.

---

## 🎀 Cinta de comandos

- La barra de botones grises se sustituye por una cinta con los grupos **Abrir**, **Organizar**, **Paneles** y **Herramientas**: icono grande y etiqueta debajo, botones planos que se resaltan al pasar el ratón y separadores de verdad.
- El color tiene significado: eliminar en rojo, la estrella de favoritos, la carpeta nueva y SpaceAnalyzer en su morado.
- **Favoritos** se ve activo mientras el panel está abierto.
- Cabe entera en la ventana más pequeña que se permite (800 px), en español y en inglés; hay una prueba que lo comprueba.

## 📁 Iconos nuevos

- Carpetas a dos tonos con degradado y hoja interior, documentos con la esquina doblada y unidades con su luz, dibujados sobre una rejilla de 16 puntos para verse nítidos en las listas.
- Las carpetas que tiene todo usuario (Escritorio, Documentos, Descargas, Imágenes, Música, Vídeos y la carpeta personal) llevan su glifo, en los favoritos y también en las listas de archivos.

## 📊 SpaceAnalyzer integrado

- Botón **SpaceAnalyzer** en la cinta y *Herramientas → SpaceAnalyzer*: abre [SpaceAnalyzer](https://aurgo.github.io/SpaceAnalyzer/) sobre la carpeta del panel activo; desde la vista Equipo o un servidor se abre en su lista de unidades.
- **La primera vez lo descarga** de su última versión en GitHub (la de tu sistema, unos 2 MB), comprueba el tamaño y el **SHA-256** publicados y lo guarda. Después se abre al instante, también sin conexión.
- Una vez por semana, al usarlo, busca una versión nueva en segundo plano y la deja lista para la próxima vez, sin tocar la copia que está abierta.
- Se guarda en `%LOCALAPPDATA%\SharpCommander\tools` (Windows), `~/Library/Application Support/SharpCommander/tools` (macOS) o `~/.local/share/SharpCommander/tools` (Linux).

## 🔔 Aviso de versiones nuevas

- Comprueba **una vez al día** (antes, una vez por semana), también con la ventana abierta días enteros.
- Pregunta **una sola vez** por cada versión y deja un botón **Nueva versión** en la barra de menús que lleva a su página de descarga hasta que actualices.
- *Ayuda → Comprobar automáticamente* sustituye a «Comprobar al iniciar».
- **Corregido:** el botón **Abrir** del aviso daba un error en lugar de abrir la página de descarga en el navegador. La 2.2.0 todavía lo tiene: para pasar de la 2.2.0 a esta versión, descárgala desde la web o desde esta página.

## 🌐 Web nueva

- [aurgo.github.io/sharpCommander](https://aurgo.github.io/sharpCommander/), en español y en [inglés](https://aurgo.github.io/sharpCommander/en/): capturas reales de la aplicación, atajos de teclado, descargas por sistema y preguntas frecuentes.
- Pensada para buscadores: páginas separadas por idioma con `hreflang`, datos estructurados (aplicación y preguntas frecuentes), Open Graph, mapa del sitio y `llms.txt`. Herramientas **WebMCP** para que un asistente en el navegador encuentre la descarga o los atajos.
- Se genera con `dotnet run tools/SiteGen.cs` a partir de `site/index.html`, y las imágenes salen de la propia aplicación con `SC_SITE=1 dotnet test --filter GenerateSiteImages`.

## 🛠️ Correcciones y mantenimiento

- En Windows, una clave indicada como `~/.ssh/clave` ya no se muestra con separadores mezclados.
- Pruebas: las clases se ejecutan una detrás de otra, porque al arrancar varias a la vez la plataforma sin ventana se quedaba a veces colgada. El CI falla en minutos si algo se cuelga, en lugar de esperar seis horas, y se han corregido dos pruebas que fallaban solo en Windows.
- **541 pruebas automatizadas** (434 en la 2.2.0).

---

## 📥 Descargas

| Plataforma | Archivo |
|---|---|
| Windows x64 | `SharpCommander-v2.3.0-win-x64.zip` |
| Windows x86 | `SharpCommander-v2.3.0-win-x86.zip` |
| Windows ARM64 | `SharpCommander-v2.3.0-win-arm64.zip` |
| Linux x64 | `SharpCommander-v2.3.0-linux-x64.zip` |
| Linux ARM64 | `SharpCommander-v2.3.0-linux-arm64.zip` |
| macOS Intel | `SharpCommander-v2.3.0-osx-x64.zip` |
| macOS Apple Silicon | `SharpCommander-v2.3.0-osx-arm64.zip` |

Autocontenidos: no requieren .NET instalado.

### macOS

El ZIP contiene `SharpCommander.app` y un **`install.sh`**. La aplicación va firmada ad-hoc pero **sin notarizar**, así que macOS la pone en cuarentena al descargarla. Abre el Terminal en la carpeta extraída y ejecuta `./install.sh`: comprueba la firma, copia la aplicación a `/Applications` y quita la cuarentena. Si prefieres no usarlo, ábrela, deja que macOS la bloquee y pulsa **Abrir de todos modos** en *Ajustes del Sistema → Privacidad y seguridad*.

### Windows

SmartScreen puede avisar de que es un editor desconocido: *Más información → Ejecutar de todas formas*.

---

# SharpCommander v2.2.0

## 🚀 Resumen

La 2.1.0 dejó la aplicación segura y completa como gestor local. La 2.2.0 rompe esa frontera: **un panel puede apuntar a un servidor SFTP y comportarse como cualquier otro**. Alrededor de eso llega el resto de herramientas que faltaban — deshacer, archivos comprimidos, comparación de carpetas, permisos — y la interfaz en español.

---

## 🌐 Paneles remotos por SFTP

- **Un panel, un servidor** — El botón **Servidor** de la barra, o *Herramientas → Conectar a servidor…*, apuntan el panel activo a `sftp://usuario@host/ruta`. A partir de ahí es un panel normal: F5, F6, F7, F8, renombrar y calcular tamaño funcionan igual, y copiar al panel contrario transfiere los ficheros en la dirección que toque.
- **Transferencias en ambos sentidos** — Ficheros y carpetas completas, subiendo y bajando. Dentro del mismo servidor los datos no salen a la red. Descargar algo que ya existe no sobrescribe: deja los dos.
- **Lo que un servidor no puede hacer, lo dice** — Abrir terminal, mostrar en el gestor del sistema o comparar byte a byte avisan en vez de fingir. Abrir un fichero remoto sí funciona: se descarga y se abre con la aplicación local.
- **Credenciales en el llavero del sistema**, nunca en `settings.json`. La contraseña llega a la herramienta del llavero por entrada estándar, así que no aparece en la lista de procesos. Autenticación por clave privada de `~/.ssh` o por contraseña.
- **Clave de host** — Se acepta la primera vez y se recuerda durante la sesión; si cambia a mitad de sesión, la conexión **se rechaza** en vez de confiar.

## 🌍 Español

- Interfaz completa en español, **con cambio en caliente**: se elige en *Ver → Idioma* y no hace falta reiniciar. Por defecto sigue al sistema.
- La mayoría de mensajes de estado del motor siguen en inglés; es lo que queda por traducir.

## 🔄 Actualizaciones

- **Comprobación semanal al arrancar** contra las releases de GitHub, y a demanda desde *Ayuda → Buscar actualizaciones*. La automática se calla salvo que haya algo nuevo; la manual siempre responde.
- Solo lee y te lleva a la página de descargas: reemplazar la aplicación en marcha exige firma, permisos y reemplazo atómico distintos por plataforma, y hecho a medias deja una instalación irreparable.
- Se puede desactivar en *Ayuda → Comprobar al iniciar*.

## ↩️ Deshacer (Ctrl+Z)

- Revierte la última copia, movimiento, renombrado o carpeta creada. Deshacer una copia solo borra lo que esa copia creó, y pregunta antes.
- **Borrar no se puede deshacer**, y no se ofrece: la papelera del sistema no expone forma de restaurar, así que prometerlo sería mentir.

## 📦 Archivos comprimidos

- **Empaquetar y extraer** zip, tar y tar.gz, hacia el panel contrario.
- Ambos formatos **rechazan una entrada que escaparía de la carpeta destino**, de modo que un archivo malicioso no puede escribir en otro sitio del disco.
- Empaquetar nunca sobrescribe, y un archivo a medias se borra si algo falla.

## 🔍 Comparar y sincronizar

- **Comparar carpetas** por tamaño y fecha, seleccionando en cada panel lo que falta o difiere. Cancelable.
- **Sincronizar** copia en un sentido dejando intactos los extras del destino. **Espejo** además borra lo que sobra, con su propia confirmación.
- **Comparar dos ficheros** byte a byte, indicando en qué posición difieren.

## 🔐 Atributos y permisos

- Diálogo para cambiar atributos y permisos Unix, opcionalmente recursivo. Con varios elementos las casillas empiezan indeterminadas: dejar como está es una opción, porque no tienen por qué coincidir.

## 🗂️ Pestañas por panel

- **Cada pestaña pertenece a un panel**: las abiertas desde el izquierdo se agrupan a la izquierda de la barra, las del derecho a la derecha, cada grupo con su propio **+**. Cambiar de pestaña mueve solo su panel; el contrario se queda donde estaba.
- **Sobreviven al reinicio**, incluido cuál mostraba cada panel.
- Clic central sobre una carpeta la abre en pestaña nueva. Duplicar (Ctrl+Mayús+T) y **fijar**: una pestaña fijada conserva su carpeta y no se cierra.

## ⚡ Selección y utilidades

- **Selección por patrón** — `+` y `-` del teclado numérico con máscaras tipo `*.cs;*.md`, acumulables; `*` invierte la selección.
- **Tamaño de carpeta** (Alt+Espacio), que sobrevive a un refresco.
- **Copiar ruta** (Ctrl+Mayús+C) y **abrir terminal aquí** (Ctrl+Alt+T).

---

## 📥 Descargas

| Plataforma | Archivo |
|---|---|
| Windows x64 | `SharpCommander-v2.2.0-win-x64.zip` |
| Windows x86 | `SharpCommander-v2.2.0-win-x86.zip` |
| Windows ARM64 | `SharpCommander-v2.2.0-win-arm64.zip` |
| Linux x64 | `SharpCommander-v2.2.0-linux-x64.zip` |
| Linux ARM64 | `SharpCommander-v2.2.0-linux-arm64.zip` |
| macOS Intel | `SharpCommander-v2.2.0-osx-x64.zip` |
| macOS Apple Silicon | `SharpCommander-v2.2.0-osx-arm64.zip` |

Autocontenidos: no requieren .NET instalado.

### macOS

El ZIP contiene `SharpCommander.app` y un **`install.sh`** al lado. La aplicación va firmada ad-hoc pero **sin notarizar**, así que macOS la pone en cuarentena al descargarla y se niega a abrirla. Para instalarla, abre el Terminal en la carpeta extraída y ejecuta:

```bash
./install.sh
```

El script comprueba la firma, copia la aplicación a `/Applications` y quita la marca de cuarentena. Si prefieres no usarlo: ábrela, deja que macOS la bloquee, y en *Ajustes del Sistema → Privacidad y seguridad* pulsa **Abrir de todos modos**.

### Windows

SmartScreen puede avisar de que es un editor desconocido: *Más información → Ejecutar de todas formas*.

## ⚠️ Notas

- **SFTP no se ha probado contra un servidor real.** El enrutado local/remoto está cubierto por pruebas automatizadas con un servidor en memoria, pero el protocolo contra un host de verdad no se ha ejercitado. Si lo usas, hazlo primero con datos que no te importe perder.
- Copiar directamente **entre dos servidores** no está soportado; hay que pasar por una carpeta local.
- **434 pruebas automatizadas** (245 en la 2.1.0).

---

# SharpCommander v2.1.0

## 🚀 Resumen

SharpCommander es un gestor de archivos moderno de doble panel, multiplataforma, inspirado en los clásicos Total Commander y Norton Commander, construido con Avalonia UI y .NET 10.

La versión 2.1.0 es una versión de consolidación: cierra los casos en los que la 2.0.0 podía perder datos, hace visibles los errores y el progreso de las operaciones, completa las funciones que la 2.0.0 tenía a medias (pestañas, portapapeles del sistema, arrastrar y soltar, visor, propiedades) y deja el repositorio con pruebas automatizadas e integración continua en Windows, Linux y macOS.

---

## 🛡️ Operaciones de archivo seguras

- **Sin pérdida de datos al mover sobre la misma carpeta** - Mover un archivo o carpeta a su propia ubicación (F6 con ambos paneles en la misma carpeta, Ctrl+X y Ctrl+V en el mismo sitio) o una carpeta dentro de sí misma se detecta antes de tocar el disco y se informa; nunca se borra nada antes de que su reemplazo esté completo. `docs` puede copiarse dentro de `docs2`, pero no dentro de `docs/sub`.
- **Diálogo de conflicto** - Al copiar o mover sobre un nombre existente se pregunta qué hacer: **Sobrescribir**, **Omitir**, **Renombrar** (con nombre propuesto y validado) o **Cancelar**, con la opción *aplicar a todos los conflictos restantes*. La sobrescritura es atómica (archivo temporal y reemplazo) y mover una carpeta sobre otra existente las fusiona en vez de borrar el destino.
- **Confirmación y papelera al borrar** - F8, Supr y el menú contextual muestran cuántos elementos se van a eliminar y los envían por defecto a la **papelera** (Papelera de reciclaje en Windows, Trash en macOS, papelera freedesktop en Linux). Mayús+Supr elimina de forma permanente, siempre tras confirmar; si la papelera rechaza un elemento se ofrece eliminarlo permanentemente.
- **Mover entre volúmenes** - Cuando el destino está en otro disco se copia archivo a archivo y cada origen se borra solo después de que su copia haya terminado.
- **Renombrar y nueva carpeta con validación** - Se rechazan nombres vacíos, con separadores o caracteres inválidos, `.` y `..`, los nombres reservados de Windows y los nombres que ya existen (salvo cambios de mayúsculas). El error se muestra bajo el cuadro de texto y Aceptar queda deshabilitado mientras el nombre no sea válido.

## 📊 Errores visibles, progreso y cancelación

- **Barra de estado** con el último mensaje, el nombre de la operación, el porcentaje real por bytes de todo el lote, el archivo en curso y un botón **Cancelar**. Cancelar elimina el archivo parcial y deja el resto intacto.
- **Errores en diálogo** - Cada comando captura sus fallos y los muestra (con detalles copiables) en vez de cerrar la aplicación; los fallos por elemento de un lote se muestran una sola vez en una lista con ruta y motivo.
- **Registro en disco** - `logs/app.log` en el directorio de configuración, con rotación a `app.log.1` al superar 1 MiB. Manejadores globales para el hilo de interfaz, las tareas no observadas y el dominio de aplicación.
- **Búsqueda, hash, propiedades y visor cancelables** - Botón Cancelar en la búsqueda avanzada y en el cálculo de hash; cerrar la ventana cancela el trabajo en curso.

## 🗂️ Pestañas con interfaz

- Barra de pestañas con botón de cierre y botón **+**; nueva pestaña (Ctrl+T) con las dos carpetas actuales, cerrar (Ctrl+W, nunca la última), siguiente/anterior (Ctrl+Tab / Ctrl+Mayús+Tab).
- El título de la pestaña sigue la carpeta del panel activo; los favoritos se sincronizan entre todas las pestañas.

## 📁 Listado

- **Ordenación** - Carpetas primero y orden natural (`archivo2` antes que `archivo10`); clic en las cabeceras de columna o submenú *Ordenar por* para nombre, tamaño o fecha, ascendente o descendente. La elección se guarda en los ajustes.
- **Archivos ocultos** - Ocultos por defecto (atributo Hidden y archivos con punto en Unix) y alternables con Ctrl+H.
- **Refresco en sitio** - El refresco (Ctrl+R o cambios externos) actualiza la lista por diferencias, conservando la selección y la posición de desplazamiento, y ya no toca el historial ni los ajustes. Una navegación nueva cancela la anterior y descarta resultados obsoletos.
- **Búsqueda incremental** con cualquier distribución de teclado (evento TextInput); repetir la letra recorre las coincidencias; Esc limpia.
- **Ruta escrita a mano** - Una ruta inexistente muestra un error y mantiene la carpeta actual; la ruta de un archivo lo abre sin salir de la carpeta.
- **Vista Equipo** - Solo volúmenes reales (se filtran los sistemas de archivos virtuales y los puntos de montaje del sistema en Linux y macOS), con nombre útil y espacio libre/total.

## 🧰 Herramientas

- **Visor interno (F3)** - Nuevo visor de solo lectura para archivos de texto con detección de codificación (BOM, UTF-8, Latin-1), ajuste de línea, búsqueda (Ctrl+F, Intro/F3 para la siguiente coincidencia) y volcado hexadecimal para binarios; los archivos de más de 16 MiB muestran sus primeros 16 MiB. F4 abre con la aplicación predeterminada y pide confirmación antes de ejecutar programas o scripts.
- **Búsqueda avanzada** - Comodines `*` y `?` convertidos a una expresión regular compilada una sola vez, o regex directa; la expresión inválida se informa antes de buscar; búsqueda en contenido fuera del hilo de interfaz (archivos de hasta 10 MiB, binarios omitidos); doble clic, Intro o *Ir a* navegan el panel activo al resultado y lo seleccionan.
- **Renombrado masivo** - La vista previa muestra el estado de cada elemento (correcto, sin cambios, nombre inválido, duplicado en el lote, ya existe); *Aplicar* se deshabilita mientras haya conflictos, los ciclos de nombres se resuelven en dos fases y al terminar se informa de cuántos se renombraron y cuáles fallaron. La tabla vuelve a verse (tema del DataGrid incluido).
- **Hash** - MD5, SHA-1, SHA-256 y SHA-512 en una sola pasada con búfer de 1 MiB, barra de progreso, cancelación y botones *Copiar* por algoritmo y *Copiar todo*.
- **Propiedades** - Tamaño formateado y exacto, tamaño y recuento de carpetas calculados en segundo plano, fechas de creación, modificación y acceso, atributos, permisos Unix, espacio del volumen e icono según el tipo.

## 📋 Portapapeles del sistema y arrastrar y soltar

- Ctrl+C / Ctrl+X ponen los archivos en el portapapeles del sistema (API DataTransfer de Avalonia 11.3) y Ctrl+V acepta archivos copiados desde el Finder o el Explorador; el modo cortar se recuerda y pegar tras cortar mueve.
- Arrastrar entre paneles mueve (con Ctrl copia); arrastrar desde otras aplicaciones copia; los archivos pueden arrastrarse hacia otras aplicaciones. El arrastre empieza tras un desplazamiento mínimo sobre una fila ya seleccionada.

## ⭐ Favoritos

- Menú contextual con **Abrir**, **Abrir en el otro panel**, **Renombrar**, **Quitar** (también con Supr) y **Restaurar favoritos predeterminados**.
- La estrella del panel añade o quita la carpeta actual (Ctrl+D) e informa en la barra de estado; reordenar por arrastre ya no navega al elemento arrastrado.

## ⌨️ Atajos de teclado

Los atajos usan Ctrl en todas las plataformas, también en macOS.

| Tecla | Acción |
|-------|--------|
| F2 | Renombrar |
| F3 | Ver (visor interno) |
| F4 | Editar (aplicación predeterminada) |
| F5 | Copiar al otro panel |
| F6 | Mover al otro panel |
| F7 | Nueva carpeta |
| F8 / Supr | Eliminar (papelera por defecto, con confirmación) |
| Mayús+Supr | Eliminar permanentemente (con confirmación) |
| F9 | Intercambiar paneles |
| F10 | Salir |
| Ctrl+A | Seleccionar todo |
| Ctrl+C / Ctrl+X / Ctrl+V | Copiar / cortar / pegar (portapapeles del sistema) |
| Ctrl+R | Refrescar ambos paneles |
| Ctrl+F | Mostrar u ocultar el cuadro de filtro |
| Ctrl+H | Mostrar u ocultar archivos ocultos |
| Ctrl+B | Mostrar u ocultar el panel de favoritos |
| Ctrl+D | Añadir o quitar la carpeta actual de favoritos |
| Ctrl+T / Ctrl+W | Nueva pestaña / cerrar pestaña |
| Ctrl+Tab / Ctrl+Mayús+Tab | Pestaña siguiente / anterior |
| Ctrl+Mayús+F | Búsqueda avanzada |
| Ctrl+M | Renombrado masivo |
| Ctrl+Mayús+H | Calcular hash |
| Intro | Abrir el elemento seleccionado |
| Retroceso | Directorio padre |
| Esc | Limpiar el filtro y la búsqueda incremental |

Los cuadros de texto conservan sus teclas: Supr, Retroceso y Ctrl+A/C/X/V editan el texto mientras el cuadro de ruta o de filtro tiene el foco.

## 💾 Ajustes y cierre

- Los ajustes se escriben de forma atómica (archivo temporal y renombrado), los guardados se agrupan y se vuelcan antes de cerrar la ventana; un `settings.json` corrupto se conserva como `settings.json.bak` en lugar de sobrescribirse.
- El tema (Sistema, Claro, Oscuro), la visibilidad del panel de favoritos y el tamaño y estado de la ventana se recuerdan entre sesiones.
- Comparaciones de ruta según la plataforma: distinguen mayúsculas en Linux y no en Windows ni macOS.

## 🛠️ Plataforma y repositorio

- **.NET 10** y **Avalonia UI 11.3.20** (con la corrección de la dependencia vulnerable Tmds.DBus.Protocol), **CommunityToolkit.Mvvm 8.4.2**; analizadores de recorte y AOT activos sin advertencias.
- **Versión única** en `Directory.Build.props`, leída en tiempo de ejecución desde el ensamblado y por los scripts de publicación.
- **Publicación unificada** - La configuración de publicación vive en el csproj; `publish.sh`, `publish.ps1` y `publish.bat` son envoltorios finos que producen los mismos binarios. En macOS se genera un `SharpCommander.app` firmado ad hoc y el zip contiene el bundle.
- **Nueva solución** `SharpCommander.sln` con los proyectos de `src/` y `tests/`; el proyecto WinForms de 2009 se conserva en `legacy/` sin compilarse; archivos generados fuera del control de versiones.
- **Pruebas automatizadas** - Más de 200 pruebas (xunit + Avalonia.Headless) que ejecutan los servicios y las ventanas reales, incluidas las que reproducen cada uno de los fallos corregidos.
- **Integración continua** - GitHub Actions compila y prueba en Ubuntu, Windows y macOS; una etiqueta `vX.Y.Z` publica los siete identificadores de runtime y adjunta los zips a la release.

## 🐛 Correcciones

- Mover con ambos paneles en la misma carpeta borraba el archivo (ahora se detecta y se informa).
- Copiar o mover sobre un nombre existente sobrescribía sin preguntar; mover una carpeta sobre otra existente borraba el destino.
- Eliminar no pedía confirmación y era siempre permanente.
- Ctrl+A y otros cambios de selección desde el código no se reflejaban en la lista.
- La comprobación de copia circular rechazaba carpetas hermanas con prefijo común (`docs` dentro de `docs2`).
- La vista previa del renombrado masivo no se veía por falta del tema del DataGrid.
- Los errores del vigilante de cambios detenían el refresco automático hasta reiniciar.
- La búsqueda incremental dependía de un mapa de teclas fijo y fallaba con otras distribuciones.
- El tema elegido no se guardaba; cerrar la ventana podía perder los últimos ajustes.
- Los paneles creados fuera de las pestañas quedaban huérfanos con su vigilante activo.
- Los atajos Ctrl+D, Ctrl+F, F9 y F10 se anunciaban pero no existían; F3 y F4 hacían lo mismo.
- Renombrar aceptaba nombres inválidos o existentes.
- "Abrir en el explorador" en Linux lanzaba varios procesos `which`; ahora usa `xdg-open`.

---

## 🖥️ Plataformas Soportadas

| Plataforma | Arquitectura | Archivo |
|------------|--------------|---------|
| Windows | x64 | `SharpCommander-v2.1.0-win-x64.zip` |
| Windows | x86 | `SharpCommander-v2.1.0-win-x86.zip` |
| Windows | ARM64 | `SharpCommander-v2.1.0-win-arm64.zip` |
| Linux | x64 | `SharpCommander-v2.1.0-linux-x64.zip` |
| Linux | ARM64 | `SharpCommander-v2.1.0-linux-arm64.zip` |
| macOS | Intel x64 | `SharpCommander-v2.1.0-osx-x64.zip` |
| macOS | Apple Silicon | `SharpCommander-v2.1.0-osx-arm64.zip` |

---

## 📦 Instalación

1. Descarga el archivo ZIP correspondiente a tu plataforma
2. Extrae el contenido en la ubicación deseada
3. Ejecuta la aplicación:
   - **Windows:** `SharpCommander.Desktop.exe`
   - **Linux:** `./SharpCommander.Desktop` (si el permiso de ejecución se perdió al extraer: `chmod +x SharpCommander.Desktop`)
   - **macOS:** el ZIP contiene `SharpCommander.app`; arrástralo a Aplicaciones. El bundle está firmado ad hoc y no notarizado, así que la primera vez macOS puede pedir confirmación (clic derecho > Abrir, o Ajustes del Sistema > Privacidad y seguridad)

> **Nota:** La aplicación es **self-contained** - no requiere instalar .NET por separado.

---

## 🔧 Requisitos del Sistema

- **Windows:** Windows 10 o superior
- **Linux:** distribución de 64 bits con glibc reciente (Ubuntu 22.04+, Debian 12+, Fedora 40+ o equivalente)
- **macOS:** macOS 14 (Sonoma) o superior

---

## 📝 Configuración

Los ajustes y el registro se guardan automáticamente en:
- **Windows:** `%APPDATA%\SharpCommander\settings.json` y `%APPDATA%\SharpCommander\logs\app.log`
- **Linux/macOS:** `~/.config/SharpCommander/settings.json` y `~/.config/SharpCommander/logs/app.log`

---

## 🛠️ Tecnologías

- **.NET 10** - Framework multiplataforma
- **Avalonia UI 11.3** - Framework de UI multiplataforma
- **CommunityToolkit.Mvvm 8.4** - Patrón MVVM con generadores de código
- **System.Text.Json** - Serialización JSON generada en compilación (compatible con recorte y AOT)
- **xunit + Avalonia.Headless** - Pruebas automatizadas

---

## 📄 Licencia

Este proyecto está bajo la licencia MIT.

---

## 🔗 Enlaces

- **Repositorio:** [github.com/aurgo/sharpCommander](https://github.com/aurgo/sharpCommander)
- **Issues:** [Reportar problemas](https://github.com/aurgo/sharpCommander/issues)

---

**¡Gracias por usar SharpCommander!** ⚡

---

# SharpCommander v2.0.0

> **Nota de corrección (añadida en la 2.1.0):** la redacción original de estas notas anunciaba funciones que la 2.0.0 no incluía. Se han corregido las entradas afectadas: el hash solo calculaba MD5, SHA1 y SHA256 (SHA512 llega en la 2.1.0); la ventana de propiedades no mostraba atributos ni permisos; el portapapeles era interno a la aplicación y no el del sistema; no existía arrastrar y soltar entre paneles ni desde otras aplicaciones; las pestañas no tenían interfaz; la barra de herramientas no era personalizable; F5 copiaba y no refrescaba (el refresco era Ctrl+R); F9 y F10 solo funcionaban con clic; y F3 y F4 abrían ambos con la aplicación predeterminada. Todo ello está implementado en la 2.1.0.

## 🚀 Nueva Versión Mayor

SharpCommander es un gestor de archivos moderno de doble panel, multiplataforma, inspirado en los clásicos Total Commander y Norton Commander, construido con Avalonia UI y .NET 8.

Esta versión marca la migración completa desde la aplicación original Windows Forms hacia una arquitectura multiplataforma moderna, sumando un conjunto completo de operaciones avanzadas de gestión de archivos.

---

## ✨ Nuevas Funcionalidades

### 📁 Gestión de Archivos
- **Doble panel** - Navegación simultánea en dos directorios independientes
- **Copiar (F5)** - Copia archivos/carpetas entre paneles con soporte multi-selección
- **Mover (F6)** - Mueve archivos/carpetas entre paneles
- **Eliminar (F8/Delete)** - Elimina archivos y carpetas seleccionados
- **Nueva Carpeta (F7)** - Crea nuevos directorios mediante diálogo interactivo
- **Renombrar (F2)** - Renombra archivos individualmente con diálogo dedicado
- **Ver (F3) / Editar (F4)** - Abren el archivo con la aplicación predeterminada *(corregido: no había visor ni editor propios)*
- **Refrescar (Ctrl+R)** - Actualiza el contenido del panel *(corregido: F5 copia, no refresca)*
- **Abrir en Explorador** - Abre la carpeta actual en el explorador del sistema
- **Selección múltiple** - Operaciones masivas sobre varios archivos a la vez
- **Navegación al directorio padre** - Acceso rápido al directorio superior
- **Re-selección automática** - Mantiene la selección tras operaciones de archivo
- **Gestión de foco mejorada** - Foco preservado correctamente entre paneles y diálogos

*(corregido: la entrada "Drag & Drop" se ha retirado; no estaba implementado en esta versión)*

### 📋 Portapapeles
- **Copiar al portapapeles (Ctrl+C)** - Marca archivos para pegar después
- **Cortar al portapapeles (Ctrl+X)** - Corta archivos para pegar después
- **Pegar (Ctrl+V)** - Pega archivos copiados o cortados
- *(corregido: el portapapeles era interno a la aplicación, no el del sistema operativo)*

### ✏️ Renombrado Masivo
- Renombrado por lotes con búsqueda y reemplazo
- Máscaras independientes para nombre y extensión
- Vista previa antes de aplicar los cambios
- Soporte para múltiples archivos a la vez

### 🔍 Búsqueda Avanzada
- **Búsqueda incremental** - Filtra archivos al escribir, en tiempo real
- **Diálogo de búsqueda** - Búsqueda completa con opciones avanzadas
- **Soporte de Regex** - Expresiones regulares para búsquedas potentes
- **Búsqueda recursiva** - Busca dentro de subdirectorios
- Filtro de búsqueda persistente con toggle dedicado

### 🔐 Cálculo de Hashes
- **Diálogo de Hash** - Calcula hashes de archivos seleccionados
- Algoritmos soportados: MD5, SHA1, SHA256 *(corregido: SHA512 no estaba implementado)*
- Cálculo asíncrono para no bloquear la interfaz
- Útil para verificación de integridad de archivos

### 📊 Propiedades de Archivo
- **Ventana de Propiedades** - Muestra información básica de archivos
- Tamaño en bytes y fecha de modificación *(corregido: no mostraba atributos ni permisos)*

### 🗂️ Soporte de Pestañas (Tabs)
- Infraestructura base para múltiples pestañas (ViewModel y comandos), sin interfaz de usuario *(corregido: la barra de pestañas llega en la 2.1.0)*

### ⭐ Sistema de Favoritos
- Favoritos del sistema (Escritorio, Documentos, Descargas, etc.)
- Agregar carpetas personalizadas a favoritos
- **Reordenamiento de favoritos** - Organiza tus favoritos por arrastre
- Panel de favoritos colapsable (Ctrl+B)
- Persistencia automática en el perfil de usuario

### 🕐 Historial de Navegación
- ComboBox con historial de carpetas visitadas
- Acceso rápido a ubicaciones recientes
- Contador de visitas por carpeta
- Historial persistente entre sesiones

### 🎨 Interfaz de Usuario
- Barra de funciones F3-F10 estilo clásico
- Temas: Claro, Oscuro y Sistema
- Iconos modernos con PathIcon
- Barra de herramientas *(corregido: no era personalizable)*
- Barra de estado con información de archivos
- **Menú contextual** - Click derecho con acciones contextuales
- Diálogos modales nativos para entrada de usuario

### ⌨️ Atajos de Teclado
| Tecla | Acción |
|-------|--------|
| F2 | Renombrar |
| F3 | Abrir con la aplicación predeterminada |
| F4 | Abrir con la aplicación predeterminada |
| F5 | Copiar *(corregido: no refresca)* |
| F6 | Mover |
| F7 | Nueva carpeta |
| F8 | Eliminar |
| F9 | Intercambiar paneles *(solo con clic en la barra de funciones)* |
| F10 | Salir *(solo con clic en la barra de funciones)* |
| Ctrl+C | Copiar al portapapeles interno |
| Ctrl+X | Cortar al portapapeles interno |
| Ctrl+V | Pegar del portapapeles interno |
| Ctrl+R | Refrescar |
| Ctrl+B | Toggle panel de favoritos |
| Delete | Eliminar |
| Backspace | Directorio padre |

---

## 🐛 Correcciones

- **Búsqueda incremental** - Solucionado el problema donde el foco saltaba al menú principal durante la búsqueda incremental
- **Modificadores de teclas** - Las teclas con Ctrl/Alt ahora se procesan correctamente como atajos
- **Operaciones multi-archivo** - Vinculación correcta de la selección múltiple con el ViewModel
- **Manejo de procesos** - Validación de procesos null y timeout en operaciones externas
- **Path de ejecución** - Resolución correcta del directorio de ejecución de la aplicación

*(corregido: la entrada "Seguridad de archivos" se ha retirado; las validaciones de las operaciones de archivo llegan en la 2.1.0)*

---

## 🖥️ Plataformas Soportadas

| Plataforma | Arquitectura | Archivo |
|------------|--------------|---------|
| Windows | x64 | `SharpCommander-v2.0.0-win-x64.zip` |
| Windows | x86 | `SharpCommander-v2.0.0-win-x86.zip` |
| Windows | ARM64 | `SharpCommander-v2.0.0-win-arm64.zip` |
| Linux | x64 | `SharpCommander-v2.0.0-linux-x64.zip` |
| Linux | ARM64 | `SharpCommander-v2.0.0-linux-arm64.zip` |
| macOS | Intel x64 | `SharpCommander-v2.0.0-osx-x64.zip` |
| macOS | Apple Silicon | `SharpCommander-v2.0.0-osx-arm64.zip` |

---

## 📦 Instalación

1. Descarga el archivo ZIP correspondiente a tu plataforma
2. Extrae el contenido en la ubicación deseada
3. Ejecuta `SharpCommander.Desktop.exe` (Windows) o `SharpCommander.Desktop` (Linux/macOS)

> **Nota:** La aplicación es **self-contained** - no requiere instalar .NET por separado.

---

## 🔧 Requisitos del Sistema

- **Windows:** Windows 10 o superior
- **Linux:** Ubuntu 18.04+, Debian 10+, Fedora 33+, o equivalente
- **macOS:** macOS 10.15 (Catalina) o superior

---

## 📝 Configuración

Los ajustes se guardan automáticamente en:
- **Windows:** `%APPDATA%\SharpCommander\settings.json`
- **Linux/macOS:** `~/.config/SharpCommander/settings.json`

---

## 🛠️ Tecnologías

- **.NET 8.0** - Framework multiplataforma *(la 2.1.0 pasa a .NET 10)*
- **Avalonia UI 11.2** - Framework de UI multiplataforma *(la 2.1.0 pasa a Avalonia 11.3)*
- **CommunityToolkit.Mvvm** - Patrón MVVM
- **System.Text.Json** - Serialización JSON (AOT compatible)

---

## 📄 Licencia

Este proyecto está bajo la licencia MIT.
