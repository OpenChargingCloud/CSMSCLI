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
`--quiet`, `--no-trace`, `--log-file <dir>`, `--no-log-file`, the
certificate store's `--certificates <dir>`, `--import-certificate
<kind>=<file>`, `--certificate-password <pw>` and `--list-certificates`, and
HTTPS's `--https`, `--https-cert <file>`, `--https-key <file>`,
`--redirect-port <number>` and `--trusted-proxy <address>`. They are every
node's switches, read by WWCP_Node, as is what the console says once the CSMS
is up.

**`--any` puts the sign-in on the network**, and with it the password, the API
keys and the session cookie - in the clear, unless the CSMS speaks HTTPS itself
or a proxy in front of it does and is named as trusted, both also in the `web`
section of the configuration file. Behind nginx:

```json
"web": { "trustedProxies": [ "127.0.0.1", "::1" ] }
```

With certbot's files and no proxy:

```json
"web": { "tls": { "certificate": { "fullchain": "/etc/letsencrypt/live/csms.example.org/fullchain.pem",
                                   "privateKey": "/etc/letsencrypt/live/csms.example.org/privkey.pem" },
                  "redirectPort": 80 } }
```

Neither said, the CSMS warns in its log, below its banner and above every page
of its web interface to its administrators. What is said on the command line
wins over the file; WWCP_Node's README says the rest. The stations' OCPP port is
another listener, whose TLS the `ocppServer` section's security profiles and
the certificate store decide.

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

`stationSyncNTS` has a charging station that is connected do the same with its
own time servers: SyncNetworkTime and TestTimeServer of the white paper
[Secure Time Synchronization](https://github.com/OpenChargingTechnology/Whitepapers/tree/master/SecureTimeSync),
sent over OCPP. With the station alone it is a round of its legal group, with a
group's name after it a round of that group, and with an `@` the test of one
time server, named by the instance of its `NTPClientController`. The station
answers at once and tells what came of it afterwards, which is waited for, two
minutes at most, and written in the lines of `syncNTS`, so that the two lie side
by side when the time is right here and not there. Each server is named by its
host and, in brackets, by the id that comes after the `@`:

```
csms001> stationSyncNTS cs001
Asked of the charging station 'cs001', NTS group 'legal':
succeeded after 312 ms: 1 of 2 server(s) answered (1 required), offset +1.2 ms, spread 0.0 ms
  ptbtime1.ptb.de (ptb1)  +1.2 ms, round trip 18.7 ms, key exchange new
  ptbtime2.ptb.de (ptb2)  No answer within 3 s.
  legal time: no - Only one of two required servers is configured.

csms001> stationSyncNTS cs001 @ptb2
Asked of the charging station 'cs001':
ptbtime2.ptb.de (ptb2) did not answer, 3096 ms altogether:
     +0 ms  Asking ptbtime2.ptb.de.
    +94 ms  The key exchange succeeded.
  +3096 ms  error: No answer within 3 s.
  failed at: TimeRequest
```

A round asked for this way is a round of the station's like any other, and its
log says who asked; a test changes nothing the station's legal time rests on.
Where the station asks nobody it says why, with the white paper's reason code -
`'cs001' asked nobody: Rejected, UnknownGroup - ...`, or `Busy` while it is still
at another one - and a station that is not connected is not asked. Tab offers the
stations that are connected. It needs what `syncNTS` needs, and the white paper
asks to spread such requests over time where there are many stations, because
the legal time servers are everybody's.

`queryDNS` is the DNS test of the web interface: a name looked up the way this
CSMS looks up anything, cache included, or asked of one of its name servers
alone and without the cache, named after an `@` by its id, its place in the
list or its address. An address is asked for as its reverse name, record types
go by mnemonic, by number or as `TYPE65`, and A and AAAA are asked unless others
are named. The answer is all of it: the question, the RCODE by the IANA
registry's name, the server it came from, the AA and TC bits and the DNSSEC
status, every record in presentation format, and for a name server over TLS or
HTTPS the verdict on its certificate.

`stationQueryDNS` puts the same question to a charging station that is
connected, to be answered with the station's name servers: QueryDNS of the white
paper [Advanced Diagnostics for OCPP](https://github.com/OpenChargingTechnology/Whitepapers/tree/master/AdvancedDiagnostics#querydns),
sent over OCPP. Without an `@` the station asks the way all of its services
ask; with one, the name server of that `serverId` alone. What the station
answers is written in the lines of `queryDNS`, so that the two lie side by side
when a name resolves here and not there:

```
csms001> stationQueryDNS cs001 ntske1.cpo.example A AAAA @dns-1
Asked of the charging station 'cs001':
ntske1.cpo.example. A, AAAA @dns-1 -> NoError from udp://192.0.2.53:53 in 12 ms
  ntske1.cpo.example.  A     300  192.0.2.123
  ntske1.cpo.example.  AAAA  300  2001:db8::123
```

Where the station asks nobody it says why, with the white paper's reason code -
`'cs001' asked nobody: Rejected, UnknownServer - ...` - and a station that is
not connected is not asked. Tab offers the stations that are connected, then
the record types. Both commands need what the DNS test of the web interface
needs, and both are written to the log with who asked.

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
and shows a new one once, when it is made: the CSMS keeps no more of a key than
its hash, and names it by its first six characters and the beginning of that
hash (`alices... #41WyMKor`), which `remove` takes too. Then:

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
is signed in. A session ends once what let it in no longer does - the account
switched off or without a role, the key it came with taken out or switched off:
"Signed out: ..." on the terminal, before the next command or within seconds.


### Charging stations over SSH

The charging stations may speak OCPP over the same SSH server, as the subsystem
`ocpp@open.charging.cloud`, where `"ocppServer": { "ssh": { "enabled": true } }`
in `configuration.json` or the switch on the **Charging stations** page says so.
It is off unless somebody switches it on, and only served while the charging
station server runs. The banner then has two more lines under `stations`, with
where they connect; the host key they pin is the one under `SSH`:

```
  stations       ws://0.0.0.0:2352 (unencrypted), 1 login(s)
                 ssh://127.0.0.1:22351
                 subsystem ocpp@open.charging.cloud, 1 with an SSH key
```

The address is the SSH server's own - the loopback, unless `--any` - or the
first name in `reachableAs`, where the stations are told one.

Each station signs in under its identification with a key of its own, given
under **Logins and groups** or with `stationKeys` - at the console, or over SSH
by an account that may change the charging stations on the web interface:

```
stationKeys
stationKeys CS001
stationKeys CS001 add ssh-ed25519 AAAA... cs001
stationKeys CS001 remove SHA256:abc
```

A station not listed yet is added by `add`, in the default group. `remove`
takes the fingerprint `stationKeys` lists, or enough of its beginning, and
ends the session that key opened at once. Where a key cannot let its station
in - the station or its group switched off, a group that takes no SSH keys,
OCPP over SSH switched off, no SSH server - the answer says so beside it. A
station gets OCPP and nothing else: no command line, no command, no SFTP.

The whole way from both ends, step by step, is written down for the local
controller, whose steps a CSMS shares - its SSH port is 22351 where that page
says 22350:
[Charging stations over SSH: a walk-through](https://github.com/OpenChargingCloud/LocalControllerCLI/blob/master/docs/Charging-stations-over-SSH.md).


### Operating the charging stations

`connections` lists the charging stations connected now: from where, how many
WebSocket frames went each way and how long ago the last did - or that one is
connected over SSH. `use` chooses one of them, and from then on the OCPP
commands of the OCPP library go to it: `reset`, `remoteStart`, `remoteStop`,
`changeAvailability`, `getBaseReport`, `getReport`, `triggerMessage`,
`installCertificate`, `setChargingProfile`, `getLog` and the
rest. Tab offers them once a station is chosen, and the prompt says which one is;
`use -` chooses none again.

```
csms001> use cs001
Using the charging station 'cs001': the OCPP commands go to it.
csms001 [cs001]> reset immediate
reset immediate => 34,5361 ms
{
  "status": "Accepted"
}
```

They need what operating the stations needs - the `cpo` role or an
administrator's - and are asked for it when they run, not only at `use`, so
that a station set by hand with `setEnv` is not a way around it. A viewer may
look at `connections`, and is told what it would need. What was sent, and to
which station, is written to the log under who sent it; the command and not its
arguments, which may carry a network profile's password.

The library's own `queryDNS` is left out, for the node's: `stationQueryDNS` is
the same question to a station. All of this - the commands, `use`,
`connections` and the command line itself - is the CSMS library's, in
`libs/CSMS/CSMS/CommandLine`, so a CSMS run by another program has it too.


### The OCPP 1.6 bench

The former command line - a test PKI built by hand and an OCPP 1.6 central
system beside an OCPP 2.1 CSMS on the ports 8820..8823, one per TLS variant -
is kept as `libs/CSMS/CSMS/PKISetup.cs`, excluded from the build; its project
file says how to bring it back. Its interactive console, which wanted the OCPP
1.6 central system beside the CSMS, is gone: what it could do with a CSMS of
OCPP 2.1 - `use` and `connections`, and the OCPP library's commands behind them -
is the command line above.


### Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so. For this please read the Contributor License
Agreement carefully and send us a signed copy or use a similar free and
open license.
