# Changelog

## 1.2.1 (2026-10-25)

- **BlocPlus en 33 idiomas**: usa el idioma de Windows (o inglés si no está disponible) y se puede cambiar en Ajustes > Idioma. Al cambiarlo, ofrece reiniciar el programa en ese momento.
  - Idiomas: alemán, búlgaro, catalán, checo, chino (simplificado y tradicional), coreano, danés, eslovaco, español, euskera, finés, francés, gallego, griego, hindi, húngaro, indonesio, inglés, italiano, japonés, neerlandés, noruego, polaco, portugués (Brasil y Portugal), rumano, ruso, sueco, tailandés, turco, ucraniano y vietnamita.
- Botón GitHub en Ajustes, junto al nombre del programa, que abre la página del proyecto.

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
