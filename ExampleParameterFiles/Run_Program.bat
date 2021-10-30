@echo off

set ExePath=MaxQuantParamFileModExtractor.exe

if exist %ExePath% goto DoWork
if exist ..\%ExePath% set ExePath=..\%ExePath% && goto DoWork
if exist ..\bin\%ExePath% set ExePath=..\bin\%ExePath% && goto DoWork

echo Executable not found: %ExePath%
goto Done

:DoWork
echo.
echo Processing with %ExePath%
echo.

%ExePath% *.xml

%ExePath% *.xml > ParamFileMods.txt

:Done

pause
