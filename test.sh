#!/usr/bin/env bash
set -e
dotnet test SoWImprover.Tests/SoWImprover.Tests.csproj --verbosity normal "$@"
