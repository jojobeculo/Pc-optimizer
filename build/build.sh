#!/usr/bin/env bash
# FrameCore build script (Linux/macOS host or WSL). Produces:
#   publish/FrameCore/                       self-contained win-x64 application
#   artifacts/FrameCore-Setup-<ver>-x64.exe  NSIS installer
#   artifacts/SHA256SUMS.txt                 checksums
# Requirements: .NET SDK 10, NSIS 3 (makensis). Optional: pandoc (HTML docs).
# Usage: build/build.sh [--skip-tests] [--no-installer]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
[ -x /root/.dotnet/dotnet ] && export PATH="/root/.dotnet:$PATH"

SKIP_TESTS=0; NO_INSTALLER=0
for a in "$@"; do
  case "$a" in
    --skip-tests) SKIP_TESTS=1 ;;
    --no-installer) NO_INSTALLER=1 ;;
    *) echo "unknown option $a" >&2; exit 2 ;;
  esac
done

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)"
PUB="$ROOT/publish/FrameCore"
ART="$ROOT/artifacts"
echo "== FrameCore $VERSION =="

if [ "$SKIP_TESTS" = 0 ]; then
  echo "== Unit tests =="
  dotnet test tests/FrameCore.Tests/FrameCore.Tests.csproj -c Release --nologo
fi

echo "== Publish (Release, win-x64, self-contained) =="
rm -rf "$PUB"
dotnet publish src/FrameCore.UI/FrameCore.UI.csproj -c Release -r win-x64 --self-contained true -o "$PUB" --nologo

echo "== Documentation =="
mkdir -p "$PUB/docs"
cp README.md LICENSE "$PUB/"
cp docs/*.md "$PUB/docs/"
if command -v pandoc >/dev/null 2>&1; then
  for f in "$PUB"/docs/*.md "$PUB/README.md"; do
    pandoc --standalone --metadata title="$(basename "${f%.md}")" -f gfm -t html5 \
      --css=doc.css "$f" -o "${f%.md}.html"
  done
  cp build/doc.css "$PUB/docs/doc.css"; cp build/doc.css "$PUB/doc.css"
fi

# Sanity checks: the package must never contain per-machine data (what lives in %LOCALAPPDATA%\FrameCore on a PC:
# settings, journal and backups, benchmarks, reports, logs, profiles, hardware profile, captures).
if find "$PUB" \( -iname 'settings.json' -o -iname '*.journal.json' -o -iname '*.log' -o -iname 'profile.json' \
     -o -iname 'gpu-profiles.json' -o -iname '*.etl' -o -ipath '*/Backups/*' -o -ipath '*/Benchmarks/*' -o -ipath '*/Reports/*' \
     -o -ipath '*/Logs/*' -o -ipath '*/Profiles/*' -o -ipath '*/QuickBench/*' -o -ipath '*/Verification/*' \) | grep -q .; then
  echo "ERROR: per-machine data found in the publish folder:" >&2
  find "$PUB" \( -iname 'settings.json' -o -iname '*.journal.json' -o -iname '*.log' -o -iname 'profile.json' -o -ipath '*/Backups/*' \) >&2
  exit 1
fi
# No user profile path of the build machine may leak into shipped text files (catalog, languages, docs).
if grep -rIl -E '[A-Za-z]:\\\\Users\\\\[A-Za-z0-9._-]+|/home/[a-z0-9._-]+/' "$PUB/catalog" "$PUB/lang" "$PUB/docs" 2>/dev/null | grep -q .; then
  echo "ERROR: a user profile path was found in shipped text files:" >&2
  grep -rIl -E '[A-Za-z]:\\\\Users\\\\[A-Za-z0-9._-]+|/home/[a-z0-9._-]+/' "$PUB/catalog" "$PUB/lang" "$PUB/docs" >&2
  exit 1
fi
test -f "$PUB/FrameCore.exe"
test -d "$PUB/catalog/tweaks"
test -f "$PUB/lang/fr.json"

if [ "$NO_INSTALLER" = 0 ]; then
  echo "== Installer (NSIS) =="
  mkdir -p "$ART"
  OUT="$ART/FrameCore-Setup-$VERSION-x64.exe"
  makensis -V2 -DVERSION="$VERSION" -DSOURCE_DIR="$PUB" -DOUT_FILE="$OUT" installer/FrameCore.nsi
  (cd "$ART" && sha256sum "$(basename "$OUT")" > SHA256SUMS.txt)
  ls -la "$ART"
fi
echo "== Done =="
