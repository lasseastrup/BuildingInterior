#!/usr/bin/env bash
# Make the C# half of this repository testable in a fresh container.
#
# `unity/` holds the Unity packages, and their test suite runs under `dotnet test`
# with no Unity editor involved. A container without a .NET SDK can run the
# prototype's tests and not the C# side, which is a confusing way to discover a
# missing toolchain half an hour into a session.
#
# Microsoft's own installer host is not reachable through the sandbox proxy, so
# this uses the distribution package rather than dot.net/v1/dotnet-install.sh.
set -uo pipefail

if command -v dotnet >/dev/null 2>&1; then
  echo "dotnet $(dotnet --version) already present"
  exit 0
fi

if [ ! -d unity ]; then
  echo "no unity/ directory; nothing to install a .NET SDK for"
  exit 0
fi

echo "installing .NET SDK for the unity/ packages..."
if (apt-get update -qq && apt-get install -y -qq dotnet-sdk-8.0) >/tmp/dotnet-install.log 2>&1; then
  echo "dotnet $(dotnet --version) installed"
else
  echo "could not install the .NET SDK; the C# tests will not run in this session."
  echo "  see /tmp/dotnet-install.log"
fi

# Never fail the session over this: the prototype is unaffected.
exit 0
