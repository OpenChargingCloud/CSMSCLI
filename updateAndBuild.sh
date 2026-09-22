#!/bin/bash
#
# Pull everything and build it.
#
# Two things are not fetched here and are done once, by hand, in a fresh
# clone. The OCPP projects keep their stylesheets as SCSS and their compiled
# CSS out of git, so those have to be generated before anything referencing
# them compiles - it needs sass and jq on the PATH:
#
#   for f in libs/WWCP_OCPP/*/compileSASS.sh; do bash "$f"; done
#
# And the ISO 15118 schemas are in none of these repositories, because that is
# a licence you accept yourself:
#
#   bash libs/WWCP_ISO15118/tools/download-schemas.sh

set -e

cd "$(dirname "$0")"

git submodule foreach git pull
git pull
dotnet build CSMSCLI.slnx
