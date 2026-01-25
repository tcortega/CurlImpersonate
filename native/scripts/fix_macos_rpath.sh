#!/bin/bash
# Fix macOS library dependencies to use @loader_path
# Usage: fix_macos_rpath.sh <library_path>

LIBRARY="$1"

if [ -z "$LIBRARY" ] || [ ! -f "$LIBRARY" ]; then
    echo "Error: Library not found: $LIBRARY"
    exit 1
fi

# Find any dependency that contains libcurl-impersonate and fix it
OLD_PATH=$(otool -L "$LIBRARY" | grep libcurl-impersonate | head -1 | awk '{print $1}')

if [ -n "$OLD_PATH" ]; then
    echo "Fixing dependency: $OLD_PATH -> @loader_path/libcurl-impersonate.4.dylib"
    install_name_tool -change "$OLD_PATH" "@loader_path/libcurl-impersonate.4.dylib" "$LIBRARY"
else
    echo "No libcurl-impersonate dependency found to fix"
fi
