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

using System.Reflection;

// Styx's command line under a name of its own: the namespace this program's
// entry point lives in ends in "CLI" as well, and would be found first.
using StyxCLI = org.GraphDefined.Vanaheimr.CLI.CLI;

#endregion

namespace cloud.charging.open.CSMS.CommandLine
{

    /// <summary>
    /// The command line of a running CSMS.
    /// </summary>
    /// <remarks>
    /// Everything a command needs is reachable from here, which is why every
    /// command takes one of these: the CSMS itself, and through it its
    /// configuration, its log and everything the JSON API can do. A command is
    /// a second way of asking for the same thing as the web interface - never
    /// an implementation of its own.
    ///
    /// Commands are not listed anywhere. The constructor asks Styx to walk this
    /// assembly for anything that implements ICLICommand and can be built from
    /// a CSMSCommandLine, so a new command is a new file and nothing else.
    ///
    /// Not the OCPP operator console in CLI/, which is kept beside this and out
    /// of the build: that one wants an OCPP 1.6 central system node this CSMS
    /// does not have.
    /// </remarks>
    public class CSMSCommandLine : StyxCLI
    {

        #region Properties

        /// <summary>
        /// The CSMS these commands are about.
        /// </summary>
        public CSMS CSMS { get; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create the command line of the given CSMS.
        /// </summary>
        /// <param name="CSMS">The running CSMS.</param>
        /// <param name="AssembliesWithCLICommands">Further assemblies to search for commands. This one is searched either way.</param>
        public CSMSCommandLine(CSMS               CSMS,
                               params Assembly[]  AssembliesWithCLICommands)

            : base(AssembliesWithCLICommands)

        {

            this.CSMS = CSMS;

            RegisterCLIType(typeof(CSMSCommandLine));

        }

        #endregion


        #region (protected override) GetPrompt()

        /// <summary>
        /// The CSMS's OCPP identity, because it is often tried out on one bench
        /// with a charging station and a vehicle, and three consoles should not
        /// have to be told apart by what scrolls past on them. It is the name
        /// the stations know it by, and the "ocpp" section of the file sets it.
        /// </summary>
        protected override String GetPrompt()

            => $"{CSMS.Node.Id}> ";

        #endregion

    }

}
