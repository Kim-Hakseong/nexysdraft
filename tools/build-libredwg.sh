#!/usr/bin/env bash
# Build GNU LibreDWG CLI tools (dwg2dxf, dxf2dwg, dwgread) with MSYS2 mingw64.
# Usage (MSYS2 MINGW64 shell): tools/build-libredwg.sh <git-ref> <src-dir> <out-dir>
set -e
REF="${1:-master}"; SRC="${2:-/c/rea-work/draft/work/g0/libredwg}"; OUT="${3:-/c/rea-work/draft/work/g0/out}"
if [ ! -d "$SRC/.git" ]; then git clone --recurse-submodules https://github.com/LibreDWG/libredwg.git "$SRC"; fi
cd "$SRC"; git fetch --tags -q || true; git checkout -q "$REF"; git submodule update --init --recursive -q
echo "COMMIT $(git rev-parse HEAD) $(git describe --tags --always)"
sh ./autogen.sh
./configure --disable-bindings --disable-python --disable-docs --disable-shared --enable-static --disable-werror CFLAGS="-O2"
make -j"$(nproc)" -C src
make -j"$(nproc)" -C programs
mkdir -p "$OUT"
for t in dwg2dxf dxf2dwg dwgread; do cp "programs/.libs/$t.exe" "$OUT/" 2>/dev/null || cp "programs/$t.exe" "$OUT/"; done
for d in $(ldd "$OUT/dxf2dwg.exe" | awk '/mingw64/{print $3}'); do cp "$d" "$OUT/"; done
git rev-parse HEAD > "$OUT/COMMIT.txt"; git describe --tags --always >> "$OUT/COMMIT.txt"
echo "BUILD OK $OUT"
