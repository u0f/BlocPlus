# Changelog

## 1.4.0 (2026-10-28)

- **Barra de búsqueda** encima del texto (Ctrl+F, Ctrl+H) en lugar de la ventana aparte: resalta todas las coincidencias visibles, busca mientras escribes y cuenta los resultados ("3 de 12").
  - Desde su botón **⋯**: buscar o reemplazar en todas las pestañas (Ctrl+Shift+F) o en una carpeta y sus subcarpetas. Los resultados salen en un panel; doble clic para ir a cada uno.
  - Reemplazar en una carpeta no toca el disco: abre los archivos en pestañas con los cambios hechos para revisarlos y guardarlos con Guardar todo.
- **Paleta de comandos** (Ctrl+Shift+P): todos los comandos de los menús en una lista que se filtra al escribir.
- **Plegar bloques**: la flecha junto al número de línea oculta las líneas sangradas bajo ella; también con Alt+← y Alt+→.
- Se marcan el **paréntesis o llave que empareja** con el del cursor y las **otras apariciones de la palabra seleccionada**.
- **Mostrar espacios y tabulaciones** (Ver) y **cerrar comillas y paréntesis automáticamente** (opcional, en Ajustes).
- **Formatear y compactar JSON y XML** (Documento, Ctrl+Alt+F), sin reordenar claves ni cambiar números ni comentarios.
- **Comparar pestañas** (Documento > Comparar con): las diferencias se abren en una pestaña nueva con su vista previa en color.
- **Imprimir** (Ctrl+P).
- **Pestañas ancladas** y menú de pestaña con clic derecho: cerrar las demás, cerrar las de la derecha, copiar la ruta y abrir la carpeta.
- La barra de estado deja cambiar el lenguaje, la codificación y el fin de línea con un clic.
- **Una sola ventana**: abrir un archivo con BlocPlus ya abierto añade una pestaña en la misma ventana (se puede desactivar en Ajustes). `archivo.txt:42` o `archivo.txt:42:7` abre en esa línea y columna.
- **Seguridad**: las actualizaciones solo se instalan si su huella SHA-256 coincide con la que publica GitHub.
- El resaltado de sintaxis ya no vuelve a analizar el archivo entero en cada tecla ni al desplazarse, y la sesión solo reescribe las pestañas que han cambiado.
- Los archivos de más de 50 MB piden confirmación antes de abrirse.
- Los errores que el programa resuelve solo quedan en `%APPDATA%\BlocPlus\log.txt`.

## 1.3.0 (2026-10-26)

- **Vista previa de diffs y parches** (.diff, .patch): un recuadro por archivo con los números de línea antiguos y nuevos, las líneas añadidas en verde, las borradas en rojo y la cabecera del parche encima. Se pueden editar y la vista previa se actualiza al momento.
- **Abrir desde URL** (Archivo > Abrir desde URL, Ctrl+Shift+O): abre cualquier archivo de texto de internet en una pestaña para verlo o editarlo, por ejemplo `https://github.com/u0f/BlocPlus/commit/e98d4a6.diff`. Si la URL está en el portapapeles se rellena sola, y los enlaces a archivos de GitHub abren el archivo, no la página.
- **Seguridad**: lo que se abre desde una URL nunca ejecuta código. Las páginas HTML y SVG de internet no se previsualizan, el HTML dentro de un Markdown se muestra como texto, y los enlaces `javascript:` y similares se desactivan en todas las vistas previas. Las descargas tienen un máximo de 20 MB y se rechazan los archivos binarios.

## 1.2.2 (2026-10-25)

- Versión de Microsoft Store: "Editar con BlocPlus" aparece en el menú contextual de Windows 11 para cualquier archivo, y "Abrir con" ofrece BlocPlus para muchos más tipos de archivo (.lrc, .rst, .svg, .vue, .ts...).
- La versión de GitHub no cambia.

## 1.2.1 (2026-10-25)

- **BlocPlus en 33 idiomas**: usa el idioma de Windows (o inglés si no está disponible) y se puede cambiar en Ajustes > Idioma. Al cambiarlo, ofrece reiniciar el programa en ese momento.
  - Idiomas: alemán, búlgaro, catalán, checo, chino (simplificado y tradicional), coreano, danés, eslovaco, español, euskera, finés, francés, gallego, griego, hindi, húngaro, indonesio, inglés, italiano, japonés, neerlandés, noruego, polaco, portugués (Brasil y Portugal), rumano, ruso, sueco, tailandés, turco, ucraniano y vietnamita.
- Botón GitHub en Ajustes, junto al nombre del programa, que abre la página del proyecto.
- Preparado para Microsoft Store: la copia instalada desde la Store se actualiza desde la Store y no desde GitHub. La versión de GitHub funciona igual que antes.

## 1.2.0 (2026-10-25)

- **Varios cursores**, como en VS Code: Ctrl+clic añade un cursor y Ctrl+arrastrar añade otra selección. Se escribe, se borra y se mueve (flechas, Inicio, Fin, con Shift para seleccionar) en todos a la vez. Copiar y cortar juntan las selecciones, pegar reparte una línea por cursor y un solo Ctrl+Z lo deshace todo. Escape o un clic normal vuelven a un cursor.
- **Letras LRC con reproductor**: la vista previa de un archivo .lrc reproduce su audio o vídeo (MP3, M4A/AAC o MP4 H.264) y resalta la línea que suena. Clic en una línea para saltar a ella.
  - Si junto al .lrc hay un audio o vídeo con el mismo nombre se carga solo; si no, se elige desde la vista previa.
  - Reproductor con barra de progreso y de volumen (clic o arrastrar) y botón de silencio.
  - En los vídeos, la letra aparece como subtítulos.
  - Editar la letra no corta la reproducción, y ocultar la vista previa la para.
- El botón de la barra de estado se ve mucho más cuando el archivo tiene vista previa disponible.

## 1.1.1 (2026-10-24)

- Corregida la vista previa en blanco al abrir el programa con un archivo (por ejemplo un CSV): ya no hace falta modificarlo para que aparezca.

## 1.1.0 (2026-10-23)

- **Vista previa en tiempo real** (Ver > Vista previa, Ctrl+Shift+V, o el botón de la barra de estado): el editor se queda a la izquierda y la vista previa a la derecha, y se actualiza mientras escribes.
  - Formatos: HTML, Markdown, reStructuredText, SVG y CSV/TSV (como tabla).
  - Sigue el desplazamiento del editor y usa el tema claro u oscuro.
  - Las imágenes y enlaces relativos se cargan desde la carpeta del archivo. Los enlaces web se abren en el navegador.
- Resaltado de sintaxis para reStructuredText.

## 1.0.0 (2026-10-23)

Primera versión.

- Pestañas con botón **+**, botón de cerrar, punto ● en las que tienen cambios sin guardar, arrastrar para reordenar, clic central para cerrar y Ctrl+Shift+T para reabrir la última cerrada.
- Sesión como en el Bloc de notas moderno: al cerrar no pregunta nada, y las pestañas y el texto sin guardar vuelven al abrirlo. También se guarda una copia cada 30 segundos por si el programa se cuelga.
- Resaltado de sintaxis según la extensión del archivo para más de 20 lenguajes (C#, C/C++, Java, JavaScript/TypeScript, Python, Go, Rust, PHP, Ruby, PowerShell, Bash, Batch, SQL, Lua, HTML/XML, CSS, JSON, YAML, INI/TOML, Markdown, Dockerfile y Diff).
- Tema claro, oscuro o del sistema en toda la ventana: pestañas, menús, barras de desplazamiento, barra de título y diálogos.
- Números de línea, ajuste de línea, zoom y elección de fuente.
- Deshacer y rehacer sin límite.
- Buscar y reemplazar distinguiendo mayúsculas y minúsculas, por palabra completa o con expresiones regulares. Ir a línea.
- Operaciones de línea: duplicar, borrar, mover, ordenar, quitar espacios finales, cambiar entre mayúsculas y minúsculas, y sangrar o quitar la sangría de un bloque con Tab y Shift+Tab.
- Detecta y respeta la codificación (UTF-8 con o sin BOM, UTF-16 y ANSI) y el fin de línea (CRLF, LF o CR).
- Guardado seguro mediante un archivo temporal.
- Recarga los archivos que otro programa modifica.
- Archivos recientes.
- Ventana de Ajustes (Ctrl+,) con la versión y este registro de cambios.
- Instalador para tu usuario, sin permisos de administrador: añade el programa al menú Inicio, a "Abrir con" y a "Editar con BlocPlus", y lo registra para poder elegirlo como editor predeterminado. Se desinstala desde la configuración de Windows, y el desinstalador pregunta si quieres conservar o borrar tus ajustes y tu sesión.
- Actualizaciones: al abrir el programa busca una versión nueva en GitHub (se puede desactivar) y también desde Ayuda > Buscar actualizaciones. La actualización se descarga, se instala y reinicia el programa sin perder las pestañas.
- Logo e icono propios.
