# SharpCommander v2.0.0 - Release Notes

## 🚀 Nueva Versión Mayor

SharpCommander es un gestor de archivos moderno de doble panel, multiplataforma, inspirado en los clásicos Total Commander y Norton Commander, construido con Avalonia UI y .NET 8.

---

## ✨ Nuevas Funcionalidades

### 📁 Gestión de Archivos
- **Doble panel** - Navegación simultánea en dos directorios
- **Copiar (F5)** - Copia archivos/carpetas entre paneles
- **Mover (F6)** - Mueve archivos/carpetas entre paneles
- **Eliminar (F8)** - Elimina archivos y carpetas seleccionados
- **Nueva Carpeta (F7)** - Crea nuevos directorios
- **Ver (F3)** - Abre archivos con el visor predeterminado
- **Editar (F4)** - Abre archivos con el editor predeterminado

### ⭐ Sistema de Favoritos
- Favoritos del sistema (Escritorio, Documentos, Descargas, etc.)
- Agregar carpetas personalizadas a favoritos
- Panel de favoritos colapsable (Ctrl+B)
- Persistencia automática en el perfil de usuario

### 🕐 Historial de Navegación
- ComboBox con historial de carpetas visitadas
- Acceso rápido a ubicaciones recientes
- Contador de visitas por carpeta
- Historial persistente entre sesiones

### 🔍 Búsqueda Rápida
- Filtro de búsqueda en tiempo real
- Búsqueda por nombre de archivo/carpeta
- Toggle de búsqueda con botón dedicado

### 🎨 Interfaz de Usuario
- Barra de funciones F3-F10 estilo clásico
- Temas: Claro, Oscuro y Sistema
- Iconos modernos con PathIcon
- Barra de herramientas personalizable
- Barra de estado con información de archivos

### ⌨️ Atajos de Teclado
| Tecla | Acción |
|-------|--------|
| F3 | Ver archivo |
| F4 | Editar archivo |
| F5 | Copiar |
| F6 | Mover |
| F7 | Nueva carpeta |
| F8 | Eliminar |
| F9 | Intercambiar paneles |
| F10 | Salir |
| Ctrl+R | Refrescar |
| Ctrl+B | Toggle panel de favoritos |
| Delete | Eliminar |

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
