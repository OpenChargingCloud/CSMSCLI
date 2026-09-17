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

using org.GraphDefined.Vanaheimr.CLI;
using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.WWCP.NetworkingNode;

#endregion

namespace org.GraphDefined.OCPP.CSMS.TestApp.CommandLine
{

    /// <summary>
    /// Use the networking node with the specified name
    /// </summary>
    /// <param name="CLI">The command line interface</param>
    public class UseCommand(CSMSTestCLI CLI) : ACLICommand<CSMSTestCLI>(CLI),
                                               ICLICommand
    {

        #region Data

        public static readonly String CommandName = nameof(UseCommand)[..^7].ToLowerFirstChar();

        #endregion

        #region Suggest(Arguments)

        public override IEnumerable<SuggestionResponse> Suggest(String[] Arguments)
        {

            if (Arguments.Length == 1)
            {

                if (CommandName.Equals    (Arguments[0], StringComparison.CurrentCultureIgnoreCase))
                {

                    var list = new List<SuggestionResponse>();

                    foreach (var connectedNetworkingNodeId in cli.ConnectedNetworkingNodeIds)
                    {
                        list.Add(SuggestionResponse.ParameterCompleted($"{Arguments[0]} {connectedNetworkingNodeId}"));
                    }

                    return list;

                }

                if (CommandName.StartsWith(Arguments[0], StringComparison.CurrentCultureIgnoreCase))
                    return [ SuggestionResponse.CommandPrefix   (CommandName) ];

            }

            if (Arguments.Length >= 2 &&
                CommandName.Equals(Arguments[0], StringComparison.CurrentCultureIgnoreCase))
            {

                var list = new List<SuggestionResponse>();

                foreach (var connectedNetworkingNodeId in cli.ConnectedNetworkingNodeIds)
                {

                    var chargingStation = connectedNetworkingNodeId.ToString();

                    if (chargingStation.Equals    (Arguments[1], StringComparison.CurrentCultureIgnoreCase))
                        list.Add(SuggestionResponse.ParameterCompleted($"{Arguments[0]} {chargingStation}"));

                    else if (chargingStation.StartsWith(Arguments[1], StringComparison.CurrentCultureIgnoreCase))
                        list.Add(SuggestionResponse.ParameterPrefix   ($"{Arguments[0]} {chargingStation}"));

                }

                return list;

            }

            return [];

        }

        #endregion

        #region Execute(Arguments, CancellationToken)

        public override Task<String[]> Execute(String[]           Arguments,
                                               CancellationToken  CancellationToken)
        {

            if (Arguments.Length == 2)
            {

                if (NetworkingNode_Id.TryParse(Arguments[1], out var newRemoteSystemId))
                {

                    var ocppVersion = "";

                    #region Get the OCPP version for the remote system

                    foreach (var webSocketServer in cli.TestCentralSystemNode.WWCPWebSocketServers)
                        if (webSocketServer.ConnectedNetworkingNodeIds.Contains(newRemoteSystemId))
                            ocppVersion = DefaultStrings.OCPPv1_6;

                    foreach (var webSocketServer in cli.TestCSMSNode.WWCPWebSocketServers)
                        if (webSocketServer.ConnectedNetworkingNodeIds.Contains(newRemoteSystemId))
                            ocppVersion = DefaultStrings.OCPPv2_1;

                    foreach (var webSocketClient in cli.TestCentralSystemNode.WWCPWebSocketClients)
                    {
                        //if (webSocketClient. .Contains(newRemoteSystemId))
                        //    ocppVersion = DefaultStrings.OCPPv1_6;
                    }

                    foreach (var webSocketClient in cli.TestCSMSNode.WWCPWebSocketClients)
                    {
                        //if (webSocketClient. .Contains(newRemoteSystemId))
                        //    ocppVersion = DefaultStrings.OCPPv2_1;
                    }

                    #endregion

                    if (ocppVersion != "")
                    {

                        if (!cli.Environment.TryAdd(EnvironmentKey.RemoteSystemOCPPVersion, new ConcurrentList<String>(ocppVersion)))
                            cli.Environment[EnvironmentKey.RemoteSystemOCPPVersion].TrySet(ocppVersion);

                        if (!cli.Environment.TryAdd(EnvironmentKey.RemoteSystemId,          new ConcurrentList<String>(newRemoteSystemId.ToString())))
                            cli.Environment[EnvironmentKey.RemoteSystemId         ].TrySet(newRemoteSystemId.ToString());

                        return Task.FromResult<String[]>([$"Using networking node '{newRemoteSystemId}' ({ocppVersion})!"]);

                    }

                    return Task.FromResult<String[]>([$"Unknown networking node '{newRemoteSystemId}'!"]);

                }

                return Task.FromResult<String[]>([$"Invalid networking node '{Arguments[1]}'!"]);

            }

            return Task.FromResult<String[]>([$"Usage: {CommandName} <networking node>"]);

        }

        #endregion

        #region Help()

        public override String Help()
            => $"{CommandName} <networking node> - Use the networking node with the specified name";

        #endregion

    }

}
