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

using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.CommandLine;
using cloud.charging.open.protocols.WWCP.Node.Configuration;

using cloud.charging.open.CSMS.CommandLine;

using CSMSNode = cloud.charging.open.CSMS.CSMS;

#endregion

namespace cloud.charging.open.CSMS.CLI
{

    /// <summary>
    /// One CSMS, with its web interface and a prompt, until 'quit', Ctrl+C or
    /// SIGTERM.
    /// </summary>
    /// <remarks>
    /// What every kind of node's program does is the node's: the switches and
    /// the words -h explains them with, why it could not be set up or could not
    /// start, what goes into the certificate store, the banner and the prompt.
    /// What is left here is the CSMS's: what its configuration holds, what of
    /// its certificate store it does not use yet, its charging station server's
    /// port, and what its banner says of OCPP and OCPI.
    /// </remarks>
    public class Program
    {

        #region (private static) Usage

        /// <summary>
        /// What -h shows: every node's switches, in a CSMS's words.
        /// </summary>
        private static readonly NodeUsage Usage = new (

            Program:            "CSMSCLI",
            Kind:               CSMSNode.CSMSKind,
            DefaultPort:        CSMSNode.DefaultHTTPPort,
            FrontendSources:    "libs/CSMS/CSMS/Frontend",

            ConfigurationSays:  "where the name servers, the time servers, the OCPP identification, the charging station " +
                               $"server and the OCPI identity of this CSMS live (default: {WWCPConfigFile.DefaultFileName} " +
                                "below the repository root). Without the file the CSMS runs on the system defaults; the " +
                                "Configuration pages of the web interface write it, and every change there takes effect at once.",

            CertificateKinds:   CSMSNode.CertificateKinds,

            CertificatesSays:   "v2gRoot, moRoot, oemRoot and tlsIdentity are kept, and used by nothing in this CSMS yet; the " +
                                "charging station server's own certificate and the chains it accepts are not in this store, but " +
                                "on pages of their own."

        );

        #endregion

        #region (private static) WhatToDoAbout(Problem)

        /// <summary>
        /// What somebody can do about the charging station server's port, which
        /// is set in the configuration file rather than with --port; null for
        /// the web interface's, which is every node's.
        /// </summary>
        private static String? WhatToDoAbout(PortUnavailableException Problem)

            => Problem.Whose == CSMSNode.StationServerPort

                   ? "Another copy of this CSMS already running is the usual answer. Stop it, or give the " +
                     "charging station server another port: \"port\" in the \"ocppServer\" section of the configuration file."

                   : null;

        #endregion


        public static async Task<Int32> Main(String[] Arguments)
        {

            Console.OutputEncoding = System.Text.Encoding.UTF8;

            #region Arguments

            // Every node's switches; a CSMS has none of its own.
            var arguments = NodeArguments.Parse(Arguments);

            if (arguments.Refused(Usage) is Int32 refused)
                return refused;

            if (arguments.RefuseTheRest(Usage) is Int32 unknown)
                return unknown;

            var root = NodeProgram.RepositoryRoot("CSMSCLI.slnx");

            #endregion

            #region The CSMS

            CSMSNode csms;

            try
            {
                csms = new CSMSNode(
                           HTTPHostname:      arguments.HTTPHostname,
                           HTTPPort:          arguments.Port,
                           AccountsPath:      arguments.AccountsPathBelow(root),
                           ConfigFile:        new WWCPConfigFile(arguments.ConfigFilePathBelow(root)),
                           Frontend:          arguments.Frontend,
                           CertificatesPath:  arguments.CertificatesPath,
                           ConsoleLogLevel:   arguments.ConsoleLogLevel,
                           LogPath:           arguments.LogPathBelow(root),
                           BridgeDebugLog:    !arguments.NoTrace
                       );
            }
            catch (Exception e)
            {
                return NodeProgram.CouldNotBeSetUp(CSMSNode.CSMSKind, e, arguments.Verbose);
            }

            await using (csms)
            {

                if (csms.ImportCertificates(arguments, out _) is Int32 notImported)
                    return notImported;

                if (arguments.ListCertificates)
                    csms.ListCertificates();

                if (await csms.Started(arguments.Verbose, WhatToDoAbout) is Int32 notStarted)
                    return notStarted;

                #region What somebody who just started this needs to know

                foreach (var line in csms.Banner(
                                         OfTheKind: [
                                             ("OCPP node",      $"{csms.Node.Id} ({csms.Node.VendorName} {csms.Node.Model})"),
                                             ("stations",       csms.OCPPServerEnabled
                                                                    ? $"{csms.OCPPServerURL}{(csms.OCPPServerTLS ? "" : " (unencrypted)")}, " +
                                                                      $"{csms.StationLogins.EnabledCount} login(s)"
                                                                    : "switched off - no charging station can connect"),
                                             ("OCPI operator",  $"{csms.PartyIdText} '{csms.BusinessDetails.Name}', " +
                                                                $"speaking {String.Join(", ", csms.OCPIVersions.Select(version => version.Label))}"),
                                             ("partners",       $"{csms.OCPIVersionsURL} - {csms.RemotePartyCount} partner(s), " +
                                                                $"{csms.LocationCount} location(s)")
                                         ]))
                    Console.WriteLine(line);

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
