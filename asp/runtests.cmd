@echo off
setlocal
pushd "%~dp0template"
dotnet test --solution TrellisAspTemplate.slnx
set "testExitCode=%ERRORLEVEL%"
popd
endlocal & exit /b %testExitCode%