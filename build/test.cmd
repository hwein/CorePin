@echo off
dotnet run -c Debug --project "%~dp0..\tests\CorePin.Tests" -- %*
