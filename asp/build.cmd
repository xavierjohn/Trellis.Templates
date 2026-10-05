@echo off
setlocal
pushd "%~dp0"

dotnet build template\TrellisAspTemplate.slnx -c Release
set "exitCode=%ERRORLEVEL%"
IF NOT "%exitCode%"=="0" goto :exit

pushd template
dotnet test --solution TrellisAspTemplate.slnx -c Release --no-build
set "exitCode=%ERRORLEVEL%"
popd
IF NOT "%exitCode%"=="0" goto :exit

dotnet pack templatepack.csproj -c Release -o nupkg -p:PublicRelease=true
set "exitCode=%ERRORLEVEL%"

:exit
popd
endlocal & exit /b %exitCode%
