@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if %errorlevel%==0 (
    echo publishing with dotnet...
    dotnet publish VoicePitchChanger.csproj -c Release -o dist
    goto :done
)

rem no dotnet, try visual studio msbuild
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo Could not find dotnet or Visual Studio.
    goto :fail
)
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"
if not defined MSBUILD (
    echo Could not find MSBuild.
    goto :fail
)
echo publishing with "%MSBUILD%"...
"%MSBUILD%" VoicePitchChanger.csproj -t:Publish -restore -p:Configuration=Release -p:PublishDir=dist\ -v:minimal -nologo

:done
if errorlevel 1 goto :fail
echo.
echo Build succeeded: dist\SimpleMicDistorter.exe
pause
exit /b 0

:fail
echo.
echo Build FAILED.
pause
exit /b 1
