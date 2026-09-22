#!/bin/sh
#
# Add every submodule of .gitmodules again, for a tree whose .git/modules was
# thrown away.

set -e

cd "$(dirname "$0")"

git config -f .gitmodules --get-regexp '^submodule\..*\.path$' |
    while read path_key path
    do
        url_key=$(echo $path_key | sed 's/\.path/.url/')
        url=$(git config -f .gitmodules --get "$url_key")
        git submodule add $url $path
    done
