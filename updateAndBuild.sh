#!/bin/bash
#
# Pull everything and build it.
#
# The ISO 15118 schemas are not in any of these repositories and are not
# fetched here either - that is a licence you accept yourself, once:
#
#   bash libs/WWCP_ISO15118/tools/download-schemas.sh
#
# Everything else the build needs it fetches itself: the TypeScript and SASS
# compilers the libraries pin are installed by "npm ci" on the first build, and
# the OCPP stylesheets are compiled by the build rather than by hand.

set -e

cd "$(dirname "$0")"

git pull --ff-only
git submodule update --init --recursive
git submodule foreach git checkout master
git submodule foreach git pull
npm --prefix /home/ahzf/CSMSCLI/libs/CSMS/CSMS/Frontend ci
#dotnet build CSMSCLI.slnx --configuration Release
dotnet build CSMSCLI.slnx
