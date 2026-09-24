# CSMS - Charging Station Management System

[![CI](https://github.com/OpenChargingCloud/CSMSCLI/actions/workflows/ci.yml/badge.svg)](https://github.com/OpenChargingCloud/CSMSCLI/actions/workflows/ci.yml)
[![Nightly](https://github.com/OpenChargingCloud/CSMSCLI/actions/workflows/nightly.yml/badge.svg)](https://github.com/OpenChargingCloud/CSMSCLI/actions/workflows/nightly.yml)

This software implements an EV Charging Station Management System: the thing the
charging stations and the local controllers of an estate dial into, with a web
interface in front of it. Beside that it is a charge point operator in OCPI -
peered with e-mobility service providers, publishing the locations its stations
stand at. What it is and what it can be told lives in
[libs/CSMS](libs/CSMS); this repository is the command line that starts it and
the submodules it is built from.


### Getting it

The libraries it is built from are submodules, so they have to come along:

```
git clone --recurse-submodules <this repository>
```

If you already cloned it without them:

```
git submodule update --init --recursive
```

They are fetched from GitHub over https, so nothing but git is needed - no
account, no key.

**On Windows**, turn long paths on first:

```
git config --global core.longpaths true
```

The deepest file in the submodules is well over 140 characters below the clone
root, so under the classic 260-character limit the root has little room to live
in. `D:\src\CSMS` is fine; a checkout somewhere below
`C:\Users\<you>\AppData\Local\Temp\...` is not, and the clone fails halfway
through a submodule with `Filename too long` rather than at the start.
Per clone instead of globally: `git clone -c core.longpaths=true ...`.


### Building and running it

```
dotnet build CSMSCLI.slnx
dotnet run --project CSMSCLI
```

The build needs the .NET 10 SDK and Node.js: the web interface is built by npm
and embedded into the assembly, so the CSMS is one thing to deploy.

Nothing has to be installed globally beside those two. The TypeScript and SASS
compilers the libraries pin are installed by `npm ci` on the first build, and
the OCPP stylesheets are compiled by the build. The one step a fresh clone
still needs by hand is the ISO 15118 schemas, which are ISO's and a licence you
accept yourself:

```
bash libs/WWCP_ISO15118/tools/download-schemas.sh
```

At the first start there are no accounts, so the CSMS makes one up for the user
`root`, keeps its hash with the other accounts below `accounts/` and prints the
password once. Then open http://127.0.0.1:2351/ and sign in.

Those accounts are the same kind Hermod's HTTPExt API keeps for every other
component here, with this CSMS's roles as groups in them, which is what lets one
sign-in cover several components that share a server.

`dotnet run --project CSMSCLI -- --help` lists the rest: `--port`, `--any`,
`--accounts <dir>`, `--frontend <dir>`, `--config <file>`, `--verbose`,
`--quiet`, `--no-trace`, `--log-file <dir>`, `--no-log-file`.

Everything that happens is written three times over, because the three answer
different questions. The **console** shows what is going on to whoever is
watching, at the level `--verbose` and `--quiet` choose. The **Logs** page
keeps the last two thousand entries for whoever asks, and loses them when the
process ends. And `logs/` beside the solution keeps one file per day, every
entry down to the debug ones, for the afternoon somebody asks what happened
last night - `--log-file <dir>` puts it elsewhere, `--no-log-file` leaves it
out, and nothing in it is ever deleted. A disk that cannot take the file is
said once on stderr rather than with every entry; once it can, the file begins
again with a line saying how many entries are missing from it and since when.


### The OCPP 1.6 bench

The former command line - a test PKI built by hand and an OCPP 1.6 central
system beside an OCPP 2.1 CSMS on the ports 8820..8823, one per TLS variant -
is kept as `libs/CSMS/CSMS/PKISetup.cs` and an interactive OCPP console under
`CSMSCLI/CLI`. Both are excluded from the build; the project files say how to
bring them back.


### Your participation

This software is Open Source under the **Apache 2.0 license** and in some parts
**Affero GPL 3.0 license**. We appreciate your participation in this
ongoing project, and your help to improve it and the e-mobility ICT in
general. If you find bugs, want to request a feature or send us a pull
request, feel free to use the normal GitHub features to do so. For this
please read the Contributor License Agreement carefully and send us a signed
copy or use a similar free and open license.
