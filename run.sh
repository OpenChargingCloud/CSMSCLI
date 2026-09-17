#!/bin/bash

cd libs
cd Styx;    versionHash_Styx=$(git rev-list --max-count=1 HEAD);    cd ..
cd Hermod;  versionHash_Hermod=$(git rev-list --max-count=1 HEAD);  cd ..
cd ..

cd CSMSCLI
dotnet run --no-build --no-restore $versionHash_Styx $versionHash_Hermod
