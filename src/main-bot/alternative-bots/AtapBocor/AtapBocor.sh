#!/bin/sh
cd "$(dirname "$0")"

export SERVER_URL="${SERVER_URL:-ws://localhost:7654}"
export SERVER_SECRET="${SERVER_SECRET:-tjFVMYAJ1rSlihXbp5ONiGgfUcR+fzcbumyOyPBm75}"

rm -rf bin obj
dotnet build
dotnet run --no-build
