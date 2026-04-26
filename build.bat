@echo off
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" ^
    HTPCAVRVolume.sln /p:Configuration=Release /m /nologo /verbosity:minimal
if %errorlevel% neq 0 (
    echo.
    echo BUILD FAILED
    exit /b %errorlevel%
)
echo.
echo Build succeeded -^> bin\Release\HTPC-AVR-sync.exe
