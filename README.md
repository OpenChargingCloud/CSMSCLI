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
password once - below what went wrong, too, if that start then fails, at a port
something else holds, say. Then open http://127.0.0.1:2351/ and sign in.

**Recommended for the first start: give `root` your own SSH key** with it, so
that you can type at the CSMS over SSH from the start - see
[Typing at it over SSH](#typing-at-it-over-ssh):

```
dotnet run --project CSMSCLI -- --authorize-ssh-key root=C:\Users\you\.ssh\id_ed25519.pub
```

Without it, the first start makes up a key pair for `root` and prints its
private key once, below the password.

Those accounts are the same kind Hermod's HTTPExt API keeps for every other
component here, with this CSMS's roles as groups in them, which is what lets one
sign-in cover several components that share a server.

`dotnet run --project CSMSCLI -- --help` lists the rest: `--port`, `--any`,
`--accounts <dir>`, `--frontend <dir>`, `--config <file>`, `--verbose`,
`--quiet`, `--no-trace`, `--log-file <dir>`, `--no-log-file`, and the
certificate store's `--certificates <dir>`, `--import-certificate
<kind>=<file>`, `--certificate-password <pw>` and `--list-certificates`.
They are every node's switches, read by WWCP_Node, as is what the console
says once the CSMS is up.

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


### Certificates

What this CSMS believes of the servers it asks - the time servers, and the name
servers over TLS or HTTPS - and the roots of ISO 15118's PKI live in one store,
`certificates/` beside the configuration file, so beside the solution unless
`--config` says otherwise. The **Certificate store** page manages it, and so
does the command line:

```
dotnet run --project CSMSCLI -- --import-certificate tlsRoot=our-time-servers-root.pem --list-certificates
```

Importing a root makes it believed - for every use, until the page says what it
is for. `--list-certificates` prints every certificate with its handle, and
`--certificates <dir>` points the CSMS at another store, measured from where it
is started. PEM, DER and PKCS#12 all go in. A password for an import is read
from `CSMS_CERT_PASSWORD` where `--certificate-password` is not given, because
a password given as a switch stands in the process list for every other user of
the machine; and **the private keys in the store are not encrypted** - a
PKCS#12 is opened with its password once, at import, and written back without
one. Only a `systemadmin` changes the store; the viewer and the CPO may look at
it.

The keys the charging station server presents and the chains it lets stations
in by are not in this store: they are that server's, with pages of their own.
Which kinds a CSMS keeps, and what each is for, is in
[CSMS](https://github.com/OpenChargingCloud/CSMS#certificates-and-where-they-live).


### Typing at it

Once it is up, the console is a prompt named after the CSMS's OCPP identity - the
name the stations know it by - rather than a place that only scrolls. `help`
lists what can be typed, `quit` leaves, **Tab** completes and **↑** walks back
through what was typed before.

`syncNTS`, which every kind of node has from WWCP_Node's `NodeCLI`, is **Sync
now** from the **NTS client** page: the same time servers asked, the same
entries in the log, and afterwards the same result on the page as its last
synchronisation. The one line that differs is the one saying who
asked - the page names the account that pressed the button and tags it `web`,
the prompt says it was the command line and tags it `cli`. Like the button, it
asks and reports and leaves the clock alone. The console gets a line for each
server as well, because the log only records what the group concluded:

```
csms001> syncNTS
succeeded after 830 ms: 4 of 4 server(s) answered (2 required), offset +1010.8 ms, spread 6.5 ms
  ptbtime1.ptb.de  +1012.2 ms, round trip 54.2 ms, key exchange new
  ptbtime2.ptb.de  +1013.6 ms, round trip 54.3 ms, key exchange new
  ptbtime3.ptb.de  +1009.5 ms, round trip 45.8 ms, key exchange new
  ptbtime4.ptb.de  +1007.1 ms, round trip 54.1 ms, key exchange new
```

With one of the CSMS's time servers after it, it is that server's **Test** button
instead: one server, on the ports it is configured with, and every step of the
key exchange and the time request with when it happened. Case and the root's dot
do not matter. Only a server of this CSMS is tested; anything else is answered
with the ones there are, and nothing is asked.

The key exchange is TLS, and the test says what its certificate claims and
whether that held up: the session, then every certificate of the chain as this
machine built it - the server's, the intermediates', the root's - each with both
ends of its validity and the days it has left, the root's SHA-256 fingerprint,
and the verdict with its reasons. The root is there as much as the server's
certificate because a root can be pinned, and a pinned root that runs out stops
everything relying on it; which root the chain ends at depends on the machine's
trust store. A certificate that is refused is described just the same, before
the exchange is said to have failed.

```
csms001> syncNTS PTBTIME2.ptb.de.
ptbtime2.ptb.de answered, 360 ms altogether:
    +1 ms  Asking ptbtime2.ptb.de: key exchange on port 4460, time on port 123, 10 second(s) allowed.
   +16 ms  'ptbtime2.ptb.de' resolves to 192.53.103.104, 2001:0638:0610:be01:0000:0000:0000:0104.
   +16 ms  Key exchange over TLS ...
  +317 ms  Connected to 192.53.103.104, of 2 address(es) that were offered.
  +318 ms  Where the time went: name 0 ms, TCP 40 ms, TLS 209 ms, key exchange 51 ms.
  +320 ms  TLS 1.3, TLS_AES_128_GCM_SHA256, ALPN ntske/1.
  +323 ms  Server certificate: CN=ptbtime2.ptb.de, for ptbtime2.ptb.de; RSA 3072-bit, sha256RSA; valid 2026-08-09 03:05:52 to 2026-11-07 03:05:51 UTC, 43 day(s) left.
  +324 ms  Intermediate CA: CN=YR1, O=Let's Encrypt, C=US; RSA 2048-bit, sha256RSA; valid 2025-09-03 00:00:00 to 2028-09-02 23:59:59 UTC, 709 day(s) left.
  +324 ms  Intermediate CA: CN=Root YR, O=ISRG, C=US; RSA 4096-bit, sha256RSA; valid 2026-05-13 00:00:00 to 2032-09-02 23:59:59 UTC, 2170 day(s) left.
  +324 ms  Root CA: CN=ISRG Root X1, O=Internet Security Research Group, C=US; RSA 4096-bit, sha256RSA; valid 2015-06-04 11:04:38 to 2035-06-04 11:04:38 UTC, 3175 day(s) left.
  +324 ms  The root's SHA-256 fingerprint: 96bcec06264976f37460779acf28c5a7cfe8a3c0aae11a8ffcee05c0bddf08c6.
  +324 ms  Validated: the chain ends at a root this machine trusts, nothing in it is revoked (asked online), and 'ptbtime2.ptb.de' is one of the server certificate's names.
  +326 ms  The key exchange succeeded: AES_SIV_CMAC_256, 8 cookie(s).
  +326 ms  It named no NTP server of its own, so the time is asked of this host.
  +326 ms  Authenticated NTP request ...
  +359 ms  Answered by 192.53.103.104:123; 8 cookie(s) left, and a fresh one came back.
  +359 ms  Round trip 30.8 ms.
  +359 ms  This CSMS's clock is +1001.4 ms off what ptbtime2.ptb.de says.
  +359 ms  The clock was not stepped: that is a different thing, with meter readings and certificates hanging off it, and not something a test does by surprise.
```

Tab offers the CSMS's time servers as soon as the command is typed, and each Tab
after that completes as far as the names agree.

The log keeps writing while you type, from whichever thread did the thing it is
reporting, and your half-typed line survives it: the line is taken off the
screen, the entry is written whole, and the line comes back with the cursor
where it was. Nothing is suppressed and nothing is held back to make that work.

Where there is no terminal - from a script, under a service manager, in CI, or
with the output going into a file or through `| tee` - there is no prompt and
nothing to type at, and the CSMS runs until Ctrl+C stops it, or the SIGTERM a
service manager sends; either shuts it down as `quit` does.


### Typing at it over SSH

The same prompt is served over SSH, on port 22351 — twenty thousand above the
web interface's — and on the addresses the web interface listens on: the
loopback, or every address with `--any`. Nothing else is: no shell of the
machine, no files, no tunnels. `--ssh-port` moves it, `--no-ssh` switches it
off.

Whoever signs in is an account of the CSMS, under its name, with a key of its
own. The first start makes `root`; give it your public key with that very
start - the way recommended:

```
dotnet run --project CSMSCLI -- --authorize-ssh-key root=C:\Users\you\.ssh\id_ed25519.pub
```

The private key then stays on your machine, and no console ever shows it.
A first start without `--authorize-ssh-key root=...` makes up a key pair for
`root` instead and prints its private key once, below the password in the
first-start box: save the lines from `-----BEGIN OPENSSH PRIVATE KEY-----` to
the END line as a file only you can read, and sign in with
`ssh -i <file> -p 22351 root@127.0.0.1`, or import the file in PuTTYgen for
PuTTY. Like the password it is kept nowhere - but a console may be kept, by a
service's journal or a redirected output; replace it with your own key and
take it out. `--authorize-ssh-key` also works at any later start.

An OpenSSH `.pub` goes in as it is, and so does what PuTTYgen saves with *Save
public key*. The key is kept with the account, beside its password, and the
`sshKeys` command manages it - at the console, or over SSH for the account
signed in:

```
sshKeys root
sshKeys root add ssh-ed25519 AAAA... you@laptop
sshKeys root remove SHA256:abc
```

`remove` takes the fingerprint `sshKeys` lists, or enough of its beginning, and
locks that key out at once. `apiKeys` does the same for the account's API keys,
and shows a new one once, when it is made. Then:

```
ssh -p 22351 root@127.0.0.1
```

or, in PuTTY, host `127.0.0.1`, port `22351`, *Connection → Data → Auto-login
username* `root`, and the private key under *Connection → SSH → Auth →
Credentials*. The first time, PuTTY asks whether to trust the CSMS's host key:
the banner prints its fingerprint under `SSH`, to compare it with.

Everything works as at the console — Tab, the history, the log above the line
being typed — with three differences. `quit`, `exit` and Ctrl+D leave the
session, and the CSMS keeps running. The account may do what its roles let it
do on the web interface, and the log names it: "'root' at the command line over
SSH asked this CSMS to synchronise its time.", tagged `cli` and `ssh`. And the
session's log starts at the console's level and is its own: `log debug` shows
everything here, `log off` nothing, for this session alone. `who` says who else
is signed in.


### The OCPP 1.6 bench

The former command line - a test PKI built by hand and an OCPP 1.6 central
system beside an OCPP 2.1 CSMS on the ports 8820..8823, one per TLS variant -
is kept as `libs/CSMS/CSMS/PKISetup.cs` and an interactive OCPP console under
`CSMSCLI/CLI`. Both are excluded from the build; the project files say how to
bring them back. The prompt above is not that console: it lives in
`CSMSCLI/CommandLine` and speaks to this CSMS, where the old one wanted an OCPP
1.6 central system beside it.


### Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so. For this please read the Contributor License
Agreement carefully and send us a signed copy or use a similar free and
open license.
