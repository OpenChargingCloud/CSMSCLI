/*
 * Copyright (c) 2014-2025 GraphDefined GmbH <achim.friedland@graphdefined.com>
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
using cloud.charging.open.protocols.WWCP.NetworkingNode;
using org.GraphDefined.Vanaheimr.CLI;

using OCPPv1_6 = cloud.charging.open.protocols.OCPPv1_6;
using OCPPv2_1 = cloud.charging.open.protocols.OCPPv2_1;

#endregion

namespace org.GraphDefined.OCPP.CSMS.TestApp
{

    public static class DefaultStrings
    {

        public const String OCPPv1_6    = "OCPPv1.6";
        public const String OCPPv2_0_1  = "OCPPv2.0.1";
        public const String OCPPv2_1    = "OCPPv2.1";

    }


    public class CSMSTestCLI : CLI,
                               OCPPv1_6.CentralSystem.CommandLine.ICentralSystemCLI,
                               OCPPv2_1.CSMS.CommandLine.ICSMSCLI
    {

        #region (static class) DefaultStrings

        /// <summary>
        /// Default strings.
        /// </summary>
        public new static class DefaultStrings
        {

            //public const String ChargingStations = "chargingStations";

        }

        #endregion


        #region Properties

        public List<String> ChargingStations { get; } = [];

        public IEnumerable<NetworkingNode_Id> ConnectedNetworkingNodeIds
        {
            get
            {

                var list = new List<NetworkingNode_Id>();

                foreach (var webSocketServer in TestCentralSystemNode.WWCPWebSocketServers)
                    list.AddRange(webSocketServer.ConnectedNetworkingNodeIds);

                foreach (var webSocketServer in TestCSMSNode.WWCPWebSocketServers)
                    list.AddRange(webSocketServer.ConnectedNetworkingNodeIds);

                return list.Distinct();

            }
        }


        public OCPPv1_6.TestCentralSystemNode  TestCentralSystemNode    { get; }
        public OCPPv2_1.CSMS.TestCSMSNode      TestCSMSNode             { get; }

        #region OCPP Adapters

        OCPPv1_6.NetworkingNode.OCPPAdapter OCPPv1_6.CentralSystem.CommandLine.ICentralSystemCLI.OCPP
            => TestCentralSystemNode.OCPP;

        OCPPv2_1.NetworkingNode.OCPPAdapter OCPPv2_1.CSMS.CommandLine.ICSMSCLI.OCPP
            => TestCSMSNode.OCPP;

        #endregion

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create a new CSMS command line interface.
        /// </summary>
        /// <param name="AssembliesWithCLICommands">The assemblies to search for CSMS commands.</param>
        public CSMSTestCLI(OCPPv1_6.TestCentralSystemNode  TestCentralSystemNode,
                           OCPPv2_1.CSMS.TestCSMSNode      TestCSMSNode,
                           params Assembly[]               AssembliesWithCLICommands)

            : base(AssembliesWithCLICommands)

        {

            this.TestCentralSystemNode  = TestCentralSystemNode;
            this.TestCSMSNode           = TestCSMSNode;

            RegisterCLIType(typeof(CSMSTestCLI));
            RegisterCLIType(typeof(OCPPv1_6.CentralSystem.CommandLine.ICentralSystemCLI));
            RegisterCLIType(typeof(OCPPv2_1.CSMS.CommandLine.ICSMSCLI));

        }

        #endregion

    }

}
