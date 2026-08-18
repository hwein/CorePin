@echo off
rem SelfContained/RuntimeIdentifier/PublishSingleFile are set here, not in the project file:
rem in the project file they make a plain restore/build pull runtime packs from NuGet.
rem Only this publish path may download; for self-contained output that is unavoidable.
dotnet publish "%~dp0..\src\CorePin.App" -c Release -p:SelfContained=true -p:RuntimeIdentifier=win-x64 -p:PublishSingleFile=true -p:PublishTrimmed=false -p:PublishReadyToRun=false
