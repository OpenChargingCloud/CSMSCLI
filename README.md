# CSMS - Charging Station Management System

This software implements an EV Charging Station Management System: the thing the
charging stations and the local controllers of an estate dial into, with a web
interface in front of it. What it is and what it can be told lives in
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

The OCPP projects keep their stylesheets as SCSS and their compiled CSS out of
git, so a fresh clone needs those generated once. It needs `sass` and `jq` on
the PATH:

```
for f in libs/WWCP_OCPP/*/compileSASS.sh; do bash "$f"; done
```

Without that step the build stops at `error CS1566: ... events.css` in whichever
of those projects it reaches first - a missing build product, not a missing file
in git.

At the first start there are no accounts, so the CSMS makes one up for the user
`root`, keeps its hash with the other accounts below `accounts/` and prints the
password once. Then open http://127.0.0.1:2351/ and sign in.

Those accounts are the same kind Hermod's HTTPExt API keeps for every other
component here, with this CSMS's roles as groups in them, which is what lets one
sign-in cover several components that share a server.

`dotnet run --project CSMSCLI -- --help` lists the rest: `--port`, `--any`,
`--accounts <dir>`, `--frontend <dir>`, `--config <file>`, `--verbose`,
`--quiet`, `--no-trace`.


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
