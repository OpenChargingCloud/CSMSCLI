/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of CSMSCLI <https://github.com/OpenChargingCloud/CSMSCLI>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.CSMS.CommandLine;
using cloud.charging.open.CSMS.Web;

using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.Certificates;
using cloud.charging.open.protocols.WWCP.Node.Configuration;
using cloud.charging.open.protocols.WWCP.Node.Logging;

using CSMSNode = cloud.charging.open.CSMS.CSMS;

#endregion

namespace cloud.charging.open.CSMS.CLI
{

    /// <summary>
    /// One CSMS, with its web interface and a prompt, until 'quit', Ctrl+C or
    /// SIGTERM.
    /// </summary>
    public class Program
    {

        #region (private static) TryTakeValue(Arguments, ref Index, out Value)

        private static Boolean TryTakeValue(String[]     Arguments,
                                            ref Int32    Index,
                                            out String?  Value)
        {

            if (Index + 1 < Arguments.Length && !Arguments[Index + 1].StartsWith("--"))
            {
                Value = Arguments[++Index];
                return true;
            }

            Value = null;
            return false;

        }

        #endregion

        #region (private static) RepositoryRoot()

        /// <summary>
        /// The directory holding CSMSCLI.slnx, looked up from the binary and
        /// from the current directory; the current directory when neither
        /// leads to it.
        /// </summary>
        /// <remarks>
        /// The web login, the configuration and the account database default
        /// to a place below it, so that they do not end up in bin/ - where the
        /// next "dotnet clean" would take this CSMS's password with it.
        /// </remarks>
        private static String RepositoryRoot()
        {

            foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {

                var directory = new DirectoryInfo(start);

                while (directory is not null)
                {

                    if (File.Exists(Path.Combine(directory.FullName, "CSMSCLI.slnx")))
                        return directory.FullName;

                    directory = directory.Parent;

                }

            }

            return Environment.CurrentDirectory;

        }

        #endregion

        #region (private static) ListCertificates(CSMS)

        /// <summary>
        /// What is in this CSMS's certificate store, as a table.
        /// </summary>
        /// <remarks>
        /// Printed and not returned: this is what <c>--list-certificates</c>
        /// exists for - what the store holds, whether each one is switched on,
        /// until when, and what a root or a server certificate is kept for,
        /// for somebody at a console rather than on the Certificate store page.
        /// </remarks>
        private static void ListCertificates(CSMSNode csms)
        {

            Console.WriteLine();
            Console.WriteLine($"  Certificates in {csms.Certificates.Directory}");
            Console.WriteLine();

            var entries = csms.Certificates.Entries;

            if (entries.Count == 0)
            {
                Console.WriteLine("  (empty - put one there with --import-certificate <kind>=<file>)");
                Console.WriteLine();
                return;
            }

            foreach (var kind in csms.Certificates.Kinds)
            {

                var ofKind = entries.Where(entry => entry.Kind == kind).ToArray();

                if (ofKind.Length == 0)
                    continue;

                Console.WriteLine($"  {kind.Describe()}");

                foreach (var entry in ofKind)
                {

                    var state = !entry.IsActive       ? "off"
                                : entry.IsExpired     ? "EXPIRED"
                                : entry.IsNotYetValid ? "not yet valid"
                                : "on";

                    Console.WriteLine($"    {entry.Id}  {state,-13}  until {entry.NotAfter.UtcDateTime:yyyy-MM-dd}  " +
                                      $"{entry.Label}{(csms.Certificates.HasUsages(kind) ? $"  ({CertificateUsages.Describe(entry.Usages)})" : "")}");

                }

                Console.WriteLine();

            }

        }

        #endregion

        #region (private static) WhatToDoAbout(Problem)

        /// <summary>
        /// What somebody can do about a port this CSMS could not have - which
        /// depends on which of its two it was, because they are set in two
        /// different places.
        /// </summary>
        private static String WhatToDoAbout(PortUnavailableException Problem)

            => Problem.Whose == CSMSNode.StationServerPort

                   ? "Another copy of this CSMS already running is the usual answer. Stop it, or give the " +
                     "charging station server another port: \"port\" in the \"ocppServer\" section of the configuration file."

                   : "Another copy of this CSMS already running is the usual answer. Stop it, or give this one " +
                     "another port with --port <number>.";

        #endregion

        #region (private static) PrintUsage()

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: CSMSCLI [--port <number>] [--any] [--frontend <dist directory>]");
            Console.WriteLine("               [--config <file>] [--accounts <directory>]");
            Console.WriteLine("               [--verbose | --quiet] [--no-trace]");
            Console.WriteLine("               [--log-file <dir>] [--no-log-file]");
            Console.WriteLine("               [--certificates <dir>] [--import-certificate <kind>=<file>]");
            Console.WriteLine("               [--certificate-password <pw>] [--list-certificates]");
            Console.WriteLine();
            Console.WriteLine("Web interface:");
            Console.WriteLine($"  --port <number>   TCP port to listen on (default: {CSMSNode.DefaultHTTPPort})");
            Console.WriteLine("  --any             listen on all addresses instead of 127.0.0.1");
            Console.WriteLine("  --frontend <dir>  serve the web interface from a directory on disk instead of the");
            Console.WriteLine("                    bundle embedded in the assembly - use it together with");
            Console.WriteLine("                    'npm run watch' in libs/CSMS/CSMS/Frontend");
            Console.WriteLine();
            Console.WriteLine("Accounts:");
            Console.WriteLine($"  --accounts <dir>    where the accounts live (default: {CSMSNode.DefaultAccountsPath}/ below the");
            Console.WriteLine("                      repository root): the users, their roles, the organizations and");
            Console.WriteLine("                      the API keys. Without them a password is made up at the first");
            Console.WriteLine($"                      start for the user '{CSMSNode.DefaultAdminUser}' and shown once.");
            Console.WriteLine();
            Console.WriteLine("Configuration:");
            Console.WriteLine($"  --config <file>   where the name servers, the time servers, the OCPP identification,");
            Console.WriteLine($"                    the charging station server and the OCPI identity of this CSMS");
            Console.WriteLine($"                    live (default:");
            Console.WriteLine($"                    {WWCPConfigFile.DefaultFileName} below the repository root). Without the");
            Console.WriteLine("                    file the CSMS runs on the system defaults; the");
            Console.WriteLine("                    Configuration pages of the web interface write it, and every");
            Console.WriteLine("                    change there takes effect at once.");
            Console.WriteLine();
            Console.WriteLine("The certificate store. What this CSMS believes of the servers it asks, and the roots");
            Console.WriteLine("of ISO 15118's PKI, one file per certificate, switched on and off one at a time:");
            Console.WriteLine($"  --certificates <dir>      where the store is (default: {CertificatesConfiguration.DefaultDirectory}/ beside the");
            Console.WriteLine("                    configuration file). Certificates already in that directory are");
            Console.WriteLine("                    read again at every start, so copying one in is a way to install");
            Console.WriteLine("                    it. The Certificate store page manages the same store");
            Console.WriteLine("  --import-certificate <kind>=<file>");
            Console.WriteLine("                    copy a certificate into the store, as PEM, DER or PKCS#12. A root");
            Console.WriteLine("                    is a certificate on its own; a tlsIdentity has to bring its private");
            Console.WriteLine("                    key, so a PEM for one carries the key beside it. May be given");
            Console.WriteLine("                    several times. <kind> is one of:");
            Console.WriteLine("                      v2gRoot    what a station's certificate must chain to");
            Console.WriteLine("                      moRoot     what a contract certificate must chain to");
            Console.WriteLine("                      oemRoot    what an OEM provisioning certificate must chain to");
            Console.WriteLine("                      tlsRoot    what a time server or a name server over TLS may chain to");
            Console.WriteLine("                      tlsServer  a server's own certificate, to hold it to by fingerprint");
            Console.WriteLine("                      tlsIdentity  kept, and used by nothing here yet");
            Console.WriteLine("                    The three ISO 15118 roots are kept for what is to come: nothing in");
            Console.WriteLine("                    this CSMS checks a chain against them yet. A tlsRoot or a tlsServer");
            Console.WriteLine("                    goes in for every use; the Certificate store page says what it is");
            Console.WriteLine("                    for - the time servers, the name servers. A root is believed as");
            Console.WriteLine("                    soon as it is in. Not in this store: the charging station server's");
            Console.WriteLine("                    own certificate and the chains it accepts, which have pages of");
            Console.WriteLine("                    their own");
            Console.WriteLine("  --certificate-password <pw>");
            Console.WriteLine("                    what opens a protected PKCS#12 being imported. Used once and not");
            Console.WriteLine("                    kept: the store holds what it has without a password. A password");
            Console.WriteLine("                    given here stands in the process list for every other user of the");
            Console.WriteLine("                    machine, so prefer the environment: CSMS_CERT_PASSWORD");
            Console.WriteLine("  --list-certificates       print the store, with the handle of each certificate");
            Console.WriteLine();
            Console.WriteLine("Log:");
            Console.WriteLine("  -v, --verbose     write every entry to the console, down to the debug ones");
            Console.WriteLine("  -q, --quiet       write only warnings and worse");
            Console.WriteLine("      --no-trace    do not pick up what the libraries below write with DebugX");
            Console.WriteLine("      --log-file <dir>");
            Console.WriteLine($"                    where the log files go (default: {CSMSNode.DefaultLogPath}/ below the repository");
            Console.WriteLine("                    root). One file per day, every entry down to the debug ones, and");
            Console.WriteLine("                    nothing is ever deleted.");
            Console.WriteLine("      --no-log-file do not write one. Then what the console did not show, and what");
            Console.WriteLine("                    falls out of the web interface's last 2000 entries, is gone.");
            Console.WriteLine();
            Console.WriteLine("Whatever the console shows, the web interface shows the whole log under 'Logs'.");
            Console.WriteLine();
            Console.WriteLine("Once it is up, the console is a prompt: 'help' lists what can be typed there,");
            Console.WriteLine("Tab completes it, and 'quit' or Ctrl+C stops the CSMS. Started where there is");
            Console.WriteLine("no terminal - from a script, under a service manager, in CI, or with the output");
            Console.WriteLine("going into a file - there is no prompt and it simply runs.");
        }

        #endregion


        public static async Task<Int32> Main(String[] Arguments)
        {

            Console.OutputEncoding = System.Text.Encoding.UTF8;

            #region Arguments

            IPPort?  port            = null;
            var      anyAddress      = false;
            String?  frontendDir     = null;
            String?  configFilePath  = null;
            String?  accountsPath    = null;
            String?  logPath         = null;
            var      noLogFile       = false;
            var      verbose         = false;
            var      quiet           = false;
            var      noTrace         = false;
            String?  certificatesDir   = null;
            String?  certPassword      = null;
            var      listCertificates  = false;
            var      imports           = new List<(CertificateKind Kind, String File)>();

            for (var i = 0; i < Arguments.Length; i++)
            {
                switch (Arguments[i])
                {

                    case "--port":
                        if (i + 1 < Arguments.Length && UInt16.TryParse(Arguments[i + 1], out var parsedPort))
                        {
                            port = IPPort.Parse(parsedPort);
                            i++;
                        }
                        else
                        {
                            Console.Error.WriteLine("Missing or invalid port number after --port!");
                            return 2;
                        }
                        break;

                    case "--any":
                        anyAddress = true;
                        break;

                    case "--frontend":
                        if (!TryTakeValue(Arguments, ref i, out frontendDir))
                        {
                            Console.Error.WriteLine("Missing directory after --frontend!");
                            return 2;
                        }
                        break;

                    case "--accounts":
                        if (!TryTakeValue(Arguments, ref i, out accountsPath))
                        {
                            Console.Error.WriteLine("Missing directory after --accounts!");
                            return 2;
                        }
                        break;

                    case "--config":
                        if (!TryTakeValue(Arguments, ref i, out configFilePath))
                        {
                            Console.Error.WriteLine("Missing file after --config!");
                            return 2;
                        }
                        break;

                    case "--log-file":
                        if (!TryTakeValue(Arguments, ref i, out logPath))
                        {
                            Console.Error.WriteLine("Missing directory after --log-file!");
                            return 2;
                        }
                        break;

                    case "--no-log-file":
                        noLogFile = true;
                        break;

                    case "-v":
                    case "--verbose":
                        verbose = true;
                        break;

                    case "-q":
                    case "--quiet":
                        quiet = true;
                        break;

                    case "--no-trace":
                        noTrace = true;
                        break;

                    case "--certificates":
                        if (!TryTakeValue(Arguments, ref i, out certificatesDir))
                        {
                            Console.Error.WriteLine("Missing directory after --certificates!");
                            return 2;
                        }
                        break;

                    case "--certificate-password":
                        if (!TryTakeValue(Arguments, ref i, out certPassword))
                        {
                            Console.Error.WriteLine("Missing password after --certificate-password!");
                            return 2;
                        }
                        break;

                    case "--list-certificates":
                        listCertificates = true;
                        break;

                    case "--import-certificate":
                    {

                        if (!TryTakeValue(Arguments, ref i, out var import) || import is null)
                        {
                            Console.Error.WriteLine("Missing <kind>=<file> after --import-certificate!");
                            return 2;
                        }

                        // Split at the FIRST '=' only: everything after it is
                        // the path, and a Windows path is full of things that
                        // are not separators.
                        var split = import.IndexOf('=');

                        if (split < 1 || split == import.Length - 1)
                        {
                            Console.Error.WriteLine($"--import-certificate wants <kind>=<file>, and '{import}' is not that.");
                            return 2;
                        }

                        if (!CertificateKindExtensions.TryParseKind(import[..split], out var importKind) ||
                            !CSMSNode.CertificateKinds.Contains(importKind))
                        {
                            Console.Error.WriteLine($"'{import[..split]}' is not a kind of certificate this CSMS keeps. " +
                                                    $"Use one of {String.Join(", ", CSMSNode.CertificateKinds.Select(one => one.AsText()))}.");
                            return 2;
                        }

                        imports.Add((importKind, import[(split + 1)..]));

                        break;

                    }

                    case "-h":
                    case "--help":
                        PrintUsage();
                        return 0;

                    default:
                        Console.Error.WriteLine($"Unknown argument '{Arguments[i]}'!");
                        PrintUsage();
                        return 2;

                }
            }

            if (verbose && quiet)
            {
                Console.Error.WriteLine("--verbose and --quiet ask for opposite things!");
                return 2;
            }

            #endregion

            #region Where the web interface comes from

            // A directory given on the command line wins, so that
            // "npm run watch" beside a running CSMS shows up in the browser on
            // a reload, without rebuilding the C# side.
            IStaticContentSource? frontend = null;

            if (frontendDir is not null)
            {

                if (!Directory.Exists(frontendDir))
                {
                    Console.Error.WriteLine($"The frontend directory '{frontendDir}' does not exist!");
                    return 2;
                }

                frontend = new FileSystemContentSource(frontendDir);

            }

            #endregion

            #region The CSMS

            CSMSNode csms;

            try
            {
                csms = new CSMSNode(

                           HTTPHostname:             anyAddress
                                                         ? IPvXAddress.Any
                                                         : IPv4Address.Localhost,

                           HTTPPort:                 port,

                           AccountsPath:             accountsPath ?? Path.Combine(RepositoryRoot(), CSMSNode.DefaultAccountsPath),

                           ConfigFile:               new WWCPConfigFile(
                                                         configFilePath ?? Path.Combine(RepositoryRoot(), WWCPConfigFile.DefaultFileName)
                                                     ),

                           Frontend:                 frontend,

                           ConsoleLogLevel:          verbose ? LogLevel.Debug
                                                         : quiet ? LogLevel.Warning
                                                         : LogLevel.Info,

                           // On unless it is switched off. A console nobody
                           // was watching kept nothing, and the log a browser
                           // shows goes with the process - so the one place an
                           // afternoon's question can still be answered from is
                           // a file.
                           LogPath:                  noLogFile
                                                         ? null
                                                         : logPath ?? Path.Combine(RepositoryRoot(), CSMSNode.DefaultLogPath),

                           // Measured from where the CSMS is started, as every
                           // other path on this command line is. Handed on
                           // relative, it would be measured from the
                           // configuration file.
                           CertificatesPath:         certificatesDir is not null
                                                         ? Path.GetFullPath(certificatesDir)
                                                         : null,

                           BridgeDebugLog:           !noTrace

                       );
            }
            catch (Exception e)
            {

                Console.Error.WriteLine($"The CSMS could not be set up: {e.Message}");

                // A CSMS that does not come up at all is the one moment the
                // stack trace is worth more than a tidy console.
                if (verbose)
                    Console.Error.WriteLine(e);

                return 1;

            }

            await using (csms)
            {

                #region What the switches said about certificates

                // Before the start, so that a root imported here is believed by
                // the first key exchange with a time server, and not only by the
                // one after it.
                foreach (var (kind, file) in imports)
                {

                    if (!File.Exists(file))
                    {
                        Console.Error.WriteLine($"--import-certificate: there is no file '{file}'.");
                        return 2;
                    }

                    Byte[] content;

                    try
                    {
                        content = await File.ReadAllBytesAsync(file);
                    }
                    catch (Exception problem)
                    {
                        Console.Error.WriteLine($"--import-certificate: '{file}' could not be read: {problem.Message}");
                        return 2;
                    }

                    if (!csms.Certificates.Import(content,
                                                  kind,
                                                  certPassword ?? Environment.GetEnvironmentVariable("CSMS_CERT_PASSWORD"),
                                                  Label: null,
                                                  out var imported,
                                                  out var problem2))
                    {
                        Console.Error.WriteLine($"--import-certificate: {file} could not be imported as " +
                                                $"{kind.AsText()}: {problem2}");
                        return 2;
                    }

                    Console.WriteLine($"  imported       {imported.Label} as {kind.AsText()}, handle {imported.Id}");

                }

                if (listCertificates)
                    ListCertificates(csms);

                #endregion

                try
                {
                    await csms.Start();
                }
                catch (PortUnavailableException problem)
                {

                    // What somebody starting a second copy of this CSMS used to
                    // get was a stack trace under the operating system's own
                    // words for a port in use - in German on a German Windows,
                    // with the port named nowhere.
                    Console.Error.WriteLine($"The CSMS could not start: {problem.Message}.");
                    Console.Error.WriteLine(WhatToDoAbout(problem));

                    if (verbose)
                        Console.Error.WriteLine(problem);

                    return 1;

                }

                #region What somebody who just started this needs to know

                Console.WriteLine();
                Console.WriteLine($"  web interface  {csms.WebInterfaceURL}");
                Console.WriteLine($"  JSON API       {csms.WebInterfaceURL}api/v1/status");
                Console.WriteLine($"  event stream   {csms.WebInterfaceURL}api/v1/events");
                Console.WriteLine($"  HTTPExt API    {csms.WebInterfaceURL}{CSMSNode.ExtAPIPath.ToString().Trim('/')}/");
                Console.WriteLine($"  frontend from  {csms.Frontend.Description}");

                foreach (var line in csms.BuiltFrom.BannerLines())
                    Console.WriteLine(line);

                Console.WriteLine($"  configuration  {csms.ConfigFile.Path}");
                Console.WriteLine($"  accounts       {csms.ExtAPI.Users.Count()} user(s) in {csms.AccountsPath}");
                Console.WriteLine($"  sign in at     {csms.WebInterfaceURL}{CSMSNode.ExtAPIPath.ToString().Trim('/')}/login");
                Console.WriteLine($"  OCPP node      {csms.Node.Id} ({csms.Node.VendorName} {csms.Node.Model})");
                Console.WriteLine($"  stations       {(csms.OCPPServerEnabled
                                                              ? $"{csms.OCPPServerURL}{(csms.OCPPServerTLS ? "" : " (unencrypted)")}, " +
                                                                $"{csms.StationLogins.EnabledCount} login(s)"
                                                              : "switched off - no charging station can connect")}");
                Console.WriteLine($"  OCPI operator  {csms.PartyIdText} '{csms.BusinessDetails.Name}', speaking {String.Join(", ", csms.OCPIVersions.Select(version => version.Label))}");
                Console.WriteLine($"  partners       {csms.OCPIVersionsURL} - {csms.RemotePartyCount} partner(s), {csms.LocationCount} location(s)");
                Console.WriteLine($"  name servers   {(csms.DNSEnabled ? String.Join(", ", csms.DNSClient.DNSServers) : "switched off")}");
                #region The time servers

                var bands = csms.TimeSources.Bands();
                var asked = bands.SelectMany(band => band).ToArray();

                // The group's one server where it has one, which is not always
                // the host of the single client the detailed test starts from:
                // a list naming one server leaves that client where it was.
                // Trimmed, like every name here that is read rather than
                // written back into a file.
                if (asked.Length <= 1)
                    Console.WriteLine($"  time server    {(asked.Length == 1 ? asked[0].Hostname : csms.NTSClient.Hostname).Trimmed}{(csms.NTSEnabled ? "" : " (switched off)")}");

                else
                {

                    // One line per band, because a band is the unit that is
                    // asked at once - putting two bands on one line would read
                    // as six equal servers when it is two and then four.
                    for (var i = 0; i < bands.Count; i++)
                        Console.WriteLine((i == 0 ? "  time servers   " : "                 ") +
                                          String.Join(", ", bands[i].Select(source => source.Hostname.Trimmed)) +
                                          (bands.Count > 1 ? $"   (priority {bands[i][0].Priority})" : ""));

                    Console.WriteLine($"                 at least {csms.TimeSources.MinServers} of them must answer" +
                                      (csms.NTSEnabled ? "" : " - and NTS is switched off"));

                }

                #endregion

                if (csms.GeneratedPassword is not null)
                {
                    Console.WriteLine();
                    Console.WriteLine("  ┌─ First start: there were no accounts, so one was made up for you ─────────");
                    Console.WriteLine($"  │  user      {CSMSNode.DefaultAdminUser}");
                    Console.WriteLine($"  │  password  {csms.GeneratedPassword}");
                    // Named rather than called "a hash", and read from the
                    // implementation rather than typed here, so the box cannot
                    // end up describing a scheme this CSMS no longer uses.
                    // "i=600000" is also how passwords.db writes it down, which
                    // is where somebody checking this will look.
                    Console.WriteLine($"  │  It is shown here once and kept only as a {SecurePassword.PBKDF2SHA256} hash");
                    Console.WriteLine($"  │  over {SecurePassword.DefaultIterations} iterations. Write it down.");
                    Console.WriteLine("  └───────────────────────────────────────────────────────────────────────────");
                }

                Console.WriteLine();

                #endregion

                #region The command line, until 'quit', Ctrl+C or SIGTERM

                // The node's: a prompt where somebody can type, and waiting
                // where nobody can, with the log sharing the screen.
                await new CSMSCommandLine(csms).RunUntilStopped();

                #endregion

            }

            #endregion

            return 0;

        }

    }

}
