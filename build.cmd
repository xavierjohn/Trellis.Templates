@echo off
setlocal

REM Build the scaffolded template content + pack the template NuGet package.
REM Mirrors what .github/workflows/build.yml does in CI so you can iterate locally.

pushd "%~dp0"

echo === Restore + build + test template content ===
dotnet restore template\ProjectTrackerTemplate.slnx
if errorlevel 1 goto :fail
dotnet build template\ProjectTrackerTemplate.slnx -c Release --no-restore
if errorlevel 1 goto :fail
dotnet test  template\ProjectTrackerTemplate.slnx -c Release --no-build
if errorlevel 1 goto :fail

echo === Pack template package ===
dotnet pack templatepack.csproj -c Release -o nupkg -p:PublicRelease=true
if errorlevel 1 goto :fail

echo === Done. Output: nupkg\Trellis.Microservices.Templates.*.nupkg ===
popd
endlocal
exit /b 0

:fail
echo BUILD FAILED
popd
endlocal
exit /b 1
