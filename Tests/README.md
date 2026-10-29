# Pruebas del CRUD

Ejecutar desde la carpeta raíz:

```powershell
dotnet test Tests/techHiveBackend.Tests.csproj
```

Las pruebas utilizan un servidor en memoria y archivos SQLite temporales, independientes de `techhive.db`.
Cubren validaciones, CRUD, correos duplicados, concurrencia, bloqueo real de SQLite,
excepciones directas y envueltas, privacidad de errores y cancelación del cliente.
La concurrencia se reproduce mediante un interceptor que genera la excepción de EF Core.

Si una instancia en ejecución bloquea los binarios, se puede usar otra salida:

```powershell
dotnet test Tests/techHiveBackend.Tests.csproj -p:OutputPath=bin/verification/ -p:UseAppHost=false
```
