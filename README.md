# TechHive Backend

API .NET 8 con SQLite, JWT y logs de peticiones.

## Configuración y ejecución

La firma JWT requiere una clave aleatoria de al menos 32 bytes. No se incluye una clave por defecto.
En PowerShell, configura una clave para esta sesión y ejecuta la API:

```powershell
$env:Jwt__Key = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet run --launch-profile https
```

Para conservar la clave localmente entre sesiones, guárdala con user-secrets:

```powershell
dotnet user-secrets set "Jwt:Key" "$env:Jwt__Key"
```

En Git Bash puedes configurar y conservar una clave de desarrollo con:

```bash
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"
dotnet run --launch-profile https
```

En producción configura `Jwt__Key` mediante el gestor de secretos o entorno del despliegue.
Issuer, Audience y duración se configuran en `appsettings.json`. Cambiar la clave invalida los tokens anteriores.

## Autenticación

1. `POST /api/auth/register`: enviar `firstName`, `lastName`, `email` y `password` de 12 a 128 caracteres.
2. `POST /api/auth/login`: enviar `email` y `password`; devuelve `accessToken`, `tokenType` y `expiresAt`.
3. Enviar `Authorization: Bearer <accessToken>` en todas las operaciones de `/api/users`.
   En Swagger, usar **Authorize** e ingresar únicamente el token.

Los tokens duran 30 minutos. Se validan firma, algoritmo, emisor, audiencia, vencimiento y existencia
de la cuenta. Las contraseñas se almacenan con `PasswordHasher` de ASP.NET Core Identity.
Registro y login comparten un límite de 10 solicitudes por minuto por dirección IP, por instancia de la API.
Un proxy inverso requiere configurar sus direcciones de confianza antes de usar cabeceras reenviadas.

Todos los usuarios autenticados pueden administrar todo el CRUD. Esta versión no incluye roles,
refresh tokens, recuperación de contraseña ni verificación de correo. El registro es público.
La creación por `/api/users` conserva su contrato y crea un usuario sin credenciales.
El correo de login cambia si se actualiza el correo del usuario. Eliminarlo elimina las credenciales;
sus tokens dejan de dar acceso en la siguiente petición.

La base existente se conserva: al arrancar se agrega `UserCredentials` si falta. Los usuarios antiguos
no reciben contraseñas por defecto, y el registro no permite reclamar sus correos. Para probar, registra
una cuenta con un correo nuevo. Asignar acceso a usuarios antiguos requiere un flujo verificado posterior.

## Logs y errores

`RequestLoggingMiddleware` registra método, ruta, estado, duración y `traceId` mediante `ILogger`.
No registra query strings, cabeceras ni cuerpos. La salida usa los proveedores estándar de ASP.NET Core
(consola durante la ejecución); no hay archivo de logs configurado. Los 4xx usan nivel Warning, los 5xx
Error y las cancelaciones Information; 499 se utiliza solo como indicador en logs cuando la respuesta
todavía no comenzó. Si la respuesta ya comenzó, se conserva su estado HTTP y se registra el resultado
como Fallida o Interrumpida. El log incluye si la respuesta había comenzado.

El middleware de excepciones queda dentro del middleware de logs para registrar el estado final.
HTTPS precede a Swagger. Routing precede al limitador por IP; autenticación precede a autorización.
Los controladores se mapean al final. Los errores 401/403 y 429 usan `ProblemDetails` con `traceId`.

## Pruebas

```powershell
dotnet test Tests/techHiveBackend.Tests.csproj -p:OutputPath=bin/verification/ -p:UseAppHost=false
```

Las pruebas utilizan claves de prueba y bases SQLite temporales independientes de los datos de la API.
