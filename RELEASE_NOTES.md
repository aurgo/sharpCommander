# SharpCommander v2.0.0 - Release Notes

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
- **Ver (F3)** - Abre archivos con el visor predeterminado
- **Editar (F4)** - Abre archivos con el editor predeterminado
- **Refrescar (F5)** - Actualiza el contenido del panel
- **Abrir en Explorador** - Abre la carpeta actual en el explorador del sistema
- **Drag & Drop** - Arrastra archivos entre paneles o desde aplicaciones externas
- **Selección múltiple** - Operaciones masivas sobre varios archivos a la vez
- **Navegación al directorio padre** - Acceso rápido al directorio superior
- **Re-selección automática** - Mantiene la selección tras operaciones de archivo
- **Gestión de foco mejorada** - Foco preservado correctamente entre paneles y diálogos

### 📋 Portapapeles
- **Copiar al portapapeles (Ctrl+C)** - Copia archivos al portapapeles del sistema
- **Cortar al portapapeles (Ctrl+X)** - Corta archivos para pegar después
- **Pegar (Ctrl+V)** - Pega archivos copiados o cortados
- Compatible con el portapapeles nativo del sistema operativo

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
- Algoritmos soportados: MD5, SHA1, SHA256, SHA512
- Cálculo asíncrono para no bloquear la interfaz
- Útil para verificación de integridad de archivos

### 📊 Propiedades de Archivo
- **Ventana de Propiedades** - Muestra información detallada de archivos
- Tamaño, fechas, atributos y permisos
- Información extendida del sistema de archivos

### 🗂️ Soporte de Pestañas (Tabs)
- Infraestructura base para múltiples pestañas por panel
- Comandos de gestión de pestañas integrados
- Permite navegar varios directorios en el mismo panel

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
- Barra de herramientas personalizable
- Barra de estado con información de archivos
- **Menú contextual** - Click derecho con acciones contextuales
- Diálogos modales nativos para entrada de usuario

### ⌨️ Atajos de Teclado
| Tecla | Acción |
|-------|--------|
| F2 | Renombrar |
| F3 | Ver archivo |
| F4 | Editar archivo |
| F5 | Copiar / Refrescar |
| F6 | Mover |
| F7 | Nueva carpeta |
| F8 | Eliminar |
| F9 | Intercambiar paneles |
| F10 | Salir |
| Ctrl+C | Copiar al portapapeles |
| Ctrl+X | Cortar al portapapeles |
| Ctrl+V | Pegar del portapapeles |
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
- **Seguridad de archivos** - Mejoras en validación y manejo de operaciones de archivo
- **Path de ejecución** - Resolución correcta del directorio de ejecución de la aplicación

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

- **.NET 8.0** - Framework multiplataforma
- **Avalonia UI 11.2** - Framework de UI multiplataforma
- **CommunityToolkit.Mvvm** - Patrón MVVM
- **System.Text.Json** - Serialización JSON (AOT compatible)

---

## 📄 Licencia

Este proyecto está bajo la licencia MIT.

---

## 🔗 Enlaces

- **Repositorio:** [github.com/aurgo/sharpCommander](https://github.com/aurgo/sharpCommander)
- **Issues:** [Reportar problemas](https://github.com/aurgo/sharpCommander/issues)

---

**¡Gracias por usar SharpCommander!** ⚡
