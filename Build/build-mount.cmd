@echo off
rem builds mount at Build\mount\Sandbox.Mounting.CS2.
dotnet build "%~dp0MountPackage\cs2.csproj" -c Release -nologo -v q
if errorlevel 1 exit /b 1
echo.
echo "Built %~dp0mount\Sandbox.Mounting.CS2 - copy that folder into sbox\mount\"
