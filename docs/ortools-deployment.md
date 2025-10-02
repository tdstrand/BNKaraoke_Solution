# Google OR-Tools Deployment Guide

This guide explains how to install the native Google OR-Tools runtime libraries that back the `Google.OrTools` NuGet package used by `BNKaraoke.Api`.

## Prerequisites

The API targets .NET 8.0 and depends on the `Google.OrTools` solver packages, which automatically fetch the managed assemblies. Native binaries (`libortools`/`ortools.dll`) are restored per runtime identifier (RID). The following sections cover how to prepare build agents or servers on Debian-based Linux and Windows so the runtime assets are available during publish and deployment.

## Debian/Ubuntu (linux-x64)

1. **Install the .NET 8 SDK and runtime**
   ```bash
   sudo apt-get update
   sudo apt-get install -y dotnet-sdk-8.0 aspnetcore-runtime-8.0
   ```
2. **Restore the solution with the linux-x64 RID**
   ```bash
   dotnet restore BNKaraoke.Api/BNKaraoke.Api.csproj --runtime linux-x64 --locked-mode
   ```
   The locked restore ensures the `Google.OrTools.runtime.linux-x64` package (which contains `libortools.so`) is downloaded into the local NuGet cache without checking modified dependency versions.
3. **Publish or build**
   ```bash
   dotnet publish BNKaraoke.Api/BNKaraoke.Api.csproj -c Release -r linux-x64 --self-contained false
   ```
   Publishing with `--self-contained false` keeps the deployment lean while still copying the native solver shared objects into the publish folder under `runtimes/linux-x64/native/`.
4. **Optional: Validate the native payload**
   ```bash
   ls bin/Release/net8.0/linux-x64/publish/runtimes/linux-x64/native/
   ```
   Confirm `libortools.so` and related dependencies exist before packaging.

## Windows (win-x64)

1. **Install the .NET 8 SDK**
   ```powershell
   winget install --id Microsoft.DotNet.SDK.8 --source winget
   ```
   On older hosts without `winget`, download the installer from <https://dotnet.microsoft.com/download/dotnet/8.0> and run it manually.
2. **Restore the project for win-x64**
   ```powershell
   dotnet restore .\BNKaraoke.Api\BNKaraoke.Api.csproj --runtime win-x64 --locked-mode
   ```
   This pulls the `Google.OrTools.runtime.win-x64` package that contains `ortools.dll` and its dependent libraries.
3. **Publish or build**
   ```powershell
   dotnet publish .\BNKaraoke.Api\BNKaraoke.Api.csproj -c Release -r win-x64 --self-contained false
   ```
   The publish output will contain the native DLLs under `runtimes\win-x64\native\` inside the publish folder. Copy these files alongside the API binaries on the deployment target.
4. **Optional: Validate native payload**
   ```powershell
   Get-ChildItem .\BNKaraoke.Api\bin\Release\net8.0\win-x64\publish\runtimes\win-x64\native
   ```

## Notes

- Continuous integration agents should cache the NuGet packages directory to avoid re-downloading large solver binaries on each run.
- When creating Docker images, use a multi-stage build and execute the same `dotnet publish` commands above inside the build stage.
- If a host only needs to execute the API (no build tools), install the .NET 8 ASP.NET Core runtime and copy the publish output generated from a build machine following the steps above.
- To prevent accidental commits of publish artifacts or other binary payloads, double-check your working tree before creating commits and delete any build outputs such as `bin/`, `obj/`, and publish folders.
