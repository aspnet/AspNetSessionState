@ECHO OFF

setlocal
set EnableNuGetPackageRestore=true

set MSBUILDEXE=msbuild.exe

set cfgOption=/p:Configuration=Release
REM set cfgOption=/p:Configuration=Debug
REM set cfgOption=/p:Configuration=Debug;Release
if not "%1"=="" set cfgOption=/p:Configuration=

set logOptions=/v:n /flp:Summary;Verbosity=diag;LogFile=msbuild.log /flp1:warningsonly;logfile=msbuild.wrn /flp2:errorsonly;logfile=msbuild.err

echo Please build from VS 2015(or newer version) Developer Command Prompt

:BUILD
%MSBUILDEXE% "%~dp0\MicrosoftAspNetSessionState.msbuild" /t:BuildAll %logOptions% /maxcpucount /nodeReuse:false /p:SkipAdvancedTests=True %cfgOption%%*

endlocal
