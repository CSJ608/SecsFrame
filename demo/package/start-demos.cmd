@echo off
setlocal
where dotnet >nul 2>nul
if errorlevel 1 (
    echo .NET 8 ASP.NET Core Runtime is required. Install it before starting the demos.
    exit /b 1
)

dotnet "%~dp0launcher\SecsFrame.DemoLauncher.dll" %*
exit /b %errorlevel%
