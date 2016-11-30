@ECHO OFF

setlocal

set logOptions=/flp:Summary;Verbosity=diag;LogFile=msbuild.log /flp1:warningsonly;logfile=msbuild.wrn /flp2:errorsonly;logfile=msbuild.err

set MSBUILDEXE=
if exist "%SystemDrive%\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" (
set MSBUILDEXE="%SystemDrive%\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe"
GOTO BUILD
)

if exist "%SystemDrive%\Program Files (x86)\MSBuild\12.0\Bin\MSBuild.exe" (
set MSBUILDEXE="%SystemDrive%\Program Files (x86)\MSBuild\12.0\Bin\MSBuild.exe"
GOTO BUILD
)

if not defined MSBUILDEXE (
set MSBUILDEXE="C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
)

:BUILD
REM %MSBUILDEXE% "%~dp0\MicrosoftAspNetSessionState.msbuild" %logOptions% /v:d /maxcpucount /nodeReuse:false %*
%MSBUILDEXE% "%~dp0\MicrosoftAspNetSessionState.msbuild" %logOptions% /v:diag /maxcpucount /nodeReuse:false %*

endlocal
