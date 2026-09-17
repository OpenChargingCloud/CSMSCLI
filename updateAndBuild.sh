#!/bin/bash

git submodule foreach git pull
git pull
#dotnet build CSMSCLI.slnx --configuration Release
dotnet build CSMSCLI.slnx
