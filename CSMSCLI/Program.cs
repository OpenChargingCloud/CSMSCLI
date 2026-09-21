/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of CSMS <https://github.com/OpenChargingCloud/CSMS>
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

using cloud.charging.open.CSMS.Configuration;
using cloud.charging.open.CSMS.Logging;
using cloud.charging.open.CSMS.Web;

using CSMSNode = cloud.charging.open.CSMS.CSMS;

#endregion

namespace cloud.charging.open.CSMS.CLI
{

    /// <summary>
    /// One CSMS, with its web interface, until Ctrl+C.
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

        #region (private static) PrintUsage()

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: CSMSCLI [--port <number>] [--any] [--frontend <dist directory>]");
            Console.WriteLine("               [--config <file>] [--accounts <directory>]");
            Console.WriteLine("               [--verbose | --quiet] [--no-trace]");
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
            Console.WriteLine($"  --config <file>   where the name servers, the time server, the OCPP identification,");
            Console.WriteLine($"                    the charging station server and the OCPI identity of this CSMS");
            Console.WriteLine($"                    live (default:");
            Console.WriteLine($"                    {CSMSConfigFile.DefaultFileName} below the repository root). Without the");
            Console.WriteLine("                    file the CSMS runs on the system defaults; the");
            Console.WriteLine("                    Configuration pages of the web interface write it, and every");
            Console.WriteLine("                    change there takes effect at once.");
            Console.WriteLine();
            Console.WriteLine("Log:");
            Console.WriteLine("  -v, --verbose     write every entry to the console, down to the debug ones");
            Console.WriteLine("  -q, --quiet       write only warnings and worse");
            Console.WriteLine("      --no-trace    do not pick up what the libraries below write with DebugX");
            Console.WriteLine();
            Console.WriteLine("Whatever the console shows, the web interface shows the whole log under 'Logs'.");
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
            var      verbose         = false;
            var      quiet           = false;
            var      noTrace         = false;

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

                           ConfigFile:               new CSMSConfigFile(
                                                         configFilePath ?? Path.Combine(RepositoryRoot(), CSMSConfigFile.DefaultFileName)
                                                     ),

                           Frontend:                 frontend,

                           ConsoleLogLevel:          verbose ? LogLevel.Debug
                                                         : quiet ? LogLevel.Warning
                                                         : LogLevel.Info,

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

                await csms.Start();

                #region What somebody who just started this needs to know

                Console.WriteLine();
                Console.WriteLine($"  web interface  {csms.WebInterfaceURL}");
                Console.WriteLine($"  JSON API       {csms.WebInterfaceURL}api/v1/status");
                Console.WriteLine($"  event stream   {csms.WebInterfaceURL}api/v1/events");
                Console.WriteLine($"  HTTPExt API    {csms.WebInterfaceURL}{CSMSNode.ExtAPIPath.ToString().Trim('/')}/");
                Console.WriteLine($"  frontend from  {csms.Frontend.Description}");
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
                Console.WriteLine($"  time server    {csms.NTSClient.Hostname}{(csms.NTSEnabled ? "" : " (switched off)")}");

                if (csms.GeneratedPassword is not null)
                {
                    Console.WriteLine();
                    Console.WriteLine("  ┌─ First start: there were no accounts, so one was made up for you ─────────");
                    Console.WriteLine($"  │  user      {CSMSNode.DefaultAdminUser}");
                    Console.WriteLine($"  │  password  {csms.GeneratedPassword}");
                    Console.WriteLine("  │  It is shown here once and kept only as a hash. Write it down.");
                    Console.WriteLine("  └───────────────────────────────────────────────────────────────────────────");
                }

                Console.WriteLine();
                Console.WriteLine("Press Ctrl+C to stop.");
                Console.WriteLine();

                #endregion

                #region Wait for Ctrl+C

                var stopped = new TaskCompletionSource();

                Console.CancelKeyPress += (_, e) => {
                    e.Cancel = true;
                    stopped.TrySetResult();
                };

                await stopped.Task;

                #endregion

            }

            #endregion

            return 0;

        }

    }

}
