# Radial

Launcher personalizable basado en menús radiales para Windows 11.

Mantén **Ctrl**, pulsa el **botón central** del ratón, mueve el cursor hacia un sector y suelta el botón para ejecutar una aplicación, archivo, carpeta, URL o atajo de teclado. Los sectores pueden abrir submenús.

> **Estado:** en desarrollo temprano. Completados el esqueleto (fase 0) y el núcleo lógico (fase 1: geometría, motor, submenús y validación). Aún no hay funcionalidad de usuario.

## Requisitos

- Windows 11 x64, versiones estables 25H2 o posteriores.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.403 o compatible) para compilar.

## Compilar y probar

```
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/Radial.App
```

## Estructura

| Ruta | Contenido |
|---|---|
| `src/Radial.Core` | Lógica pura, independiente de Windows y WPF. |
| `src/Radial.Windows` | Integración con Windows: hooks, DWM, DPI, ejecución de acciones, almacenamiento. |
| `src/Radial.App` | Aplicación WPF. |
| `tests/` | Pruebas unitarias (xUnit). |
| `docs/` | Documentación técnica. |

Detalles en [docs/technical-design.md](docs/technical-design.md).

## Principios

Todo es local: sin cuentas, sin servidores, sin servicios de pago. La configuración vive en `%LOCALAPPDATA%\Radial\`.
