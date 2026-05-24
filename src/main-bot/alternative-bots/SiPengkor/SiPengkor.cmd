@echo off
cd /d "%~dp0"

set SERVER_URL=ws://localhost:7654
set SERVER_SECRET=tjFVMYAJ1rSlihXbp5ONiGgfUcR+fzcbumyOyPBm75

dotnet run --no-build
