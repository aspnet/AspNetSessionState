@ECHO OFF

setlocal

set logOptions=/flp:Summary;Verbosity=m;LogFile=msbuild.log /flp1:warningsonly;logfile=msbuild.wrn /flp2:errorsonly;logfile=msbuild.err

echo Please build from VS 2015(or newer version) Developer Command Prompt

:BUILD
msbuild "%~dp0\MicrosoftAspNetSessionState.msbuild" %logOptions% /v:m /maxcpucount /nodeReuse:false %*

endlocal
