@echo off
setlocal

REM ============================================================================
REM  Datamart Analyzer - iniciar / reiniciar backend y frontend
REM  Uso: doble click en iniciar.bat, o desde cmd: .\iniciar.bat
REM ============================================================================

set "ROOT=%~dp0"
set "BACKEND_DIR=%ROOT%backend\DatamartAnalyzer.Api"
set "FRONTEND_DIR=%ROOT%frontend"
set "BACKEND_TITLE=Datamart-Backend"
set "FRONTEND_TITLE=Datamart-Frontend"
set "BACKEND_PORT=5000"
set "BACKEND_PORT_HTTPS=7001"
set "FRONTEND_PORT=5173"

echo.
echo ============================================================
echo   Datamart Analyzer  -  iniciar / reiniciar
echo ============================================================
echo   Raiz: %ROOT%
echo.

REM ---- 1. Validaciones --------------------------------------------------------
if not exist "%BACKEND_DIR%\DatamartAnalyzer.Api.csproj" (
    echo [ERROR] No encuentro: %BACKEND_DIR%\DatamartAnalyzer.Api.csproj
    goto :end
)
if not exist "%FRONTEND_DIR%\package.json" (
    echo [ERROR] No encuentro: %FRONTEND_DIR%\package.json
    goto :end
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] 'dotnet' no esta en PATH. Instala el .NET 9 SDK.
    goto :end
)
where npm >nul 2>&1
if errorlevel 1 (
    echo [ERROR] 'npm' no esta en PATH. Instala Node.js.
    goto :end
)

REM ---- 2. Cerrar instancias anteriores ----------------------------------------
echo [1/3] Cerrando instancias previas...
taskkill /FI "WindowTitle eq %BACKEND_TITLE%"  /F /T >nul 2>&1
taskkill /FI "WindowTitle eq %FRONTEND_TITLE%" /F /T >nul 2>&1
call :kill_port %BACKEND_PORT%       "backend http"
call :kill_port %BACKEND_PORT_HTTPS% "backend https"
call :kill_port %FRONTEND_PORT%      "frontend"

REM Pequena pausa para que los puertos se liberen antes de relanzar
ping 127.0.0.1 -n 2 >nul

REM ---- 3. Lanzar servicios ----------------------------------------------------
REM  Patron seguro: start "Titulo" /D "carpeta" cmd /k comando
REM  - "Titulo"    : primer argumento de `start` -> titulo de la ventana
REM  - /D "carpeta": working directory (sin cd anidado)
REM  - cmd /k ...  : deja la ventana abierta para que se vean los logs

echo [2/3] Iniciando backend  en puerto %BACKEND_PORT% (http)...
REM El frontend consume el backend via el proxy de Vite hacia http://localhost:5000.
REM Se fuerza ASPNETCORE_ENVIRONMENT=Development para que no haya redireccion a HTTPS
REM (evita el 307 a :7001 y el error ERR_CERT_AUTHORITY_INVALID en el navegador).
start "%BACKEND_TITLE%" /D "%BACKEND_DIR%" cmd /k "set ASPNETCORE_ENVIRONMENT=Development&& dotnet run"

echo [3/3] Iniciando frontend en puerto %FRONTEND_PORT%...
start "%FRONTEND_TITLE%" /D "%FRONTEND_DIR%" cmd /k npm run dev

echo.
echo ------------------------------------------------------------
echo   Backend :  http://localhost:%BACKEND_PORT%   (https://localhost:%BACKEND_PORT_HTTPS%)
echo   Frontend:  http://localhost:%FRONTEND_PORT%   ^<-- abre esta
echo   Swagger :  http://localhost:%BACKEND_PORT%/swagger
echo ------------------------------------------------------------
echo   Los logs se ven en las ventanas "%BACKEND_TITLE%"
echo   y "%FRONTEND_TITLE%". Cierralas para detener los servicios.
echo   Vuelve a correr este script para reiniciar.
echo ------------------------------------------------------------

:end
echo.
echo Presiona cualquier tecla para cerrar esta ventana...
pause >nul
endlocal
exit /b 0

REM ---- Subrutina: liberar puerto ---------------------------------------------
:kill_port
set "port=%~1"
set "label=%~2"
for /f "tokens=5" %%a in ('netstat -ano ^| findstr ":%port% " ^| findstr "LISTENING"') do (
    echo     liberando puerto %port% [%label%] PID %%a
    taskkill /PID %%a /F >nul 2>&1
)
exit /b 0
