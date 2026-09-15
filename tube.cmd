@echo off
rem Tube Runner's build commands: tube play, tube export, tube installer, tube icon. `tube help` for options.
rem The commands themselves are C#, in tools\Build.
dotnet run --project "%~dp0tools\Build" -c Release -v q -- %*
exit /b %errorlevel%
