#!/usr/bin/env sh
set -eu

if ! command -v dotnet >/dev/null 2>&1; then
    echo ".NET 8 ASP.NET Core Runtime is required. Install it before starting the demos." >&2
    exit 1
fi

package_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec dotnet "$package_dir/launcher/SecsFrame.DemoLauncher.dll" "$@"
