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

using System.Text;

using org.GraphDefined.Vanaheimr.CLI;
using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.WWCP.WebSockets;
using cloud.charging.open.protocols.WWCP.NetworkingNode;

#endregion

namespace org.GraphDefined.OCPP.CSMS.TestApp.CommandLine
{

    /// <summary>
    /// Use the networking node with the specified name
    /// </summary>
    /// <param name="CLI">The command line interface</param>
    public class ConnectionsCommand(CSMSTestCLI CLI) : ACLICommand<CSMSTestCLI>(CLI),
                                                       ICLICommand
    {

        #region Data

        public static readonly String CommandName = nameof(ConnectionsCommand)[..^7].ToLowerFirstChar();

        #endregion

        #region Suggest(Arguments)

        public override IEnumerable<SuggestionResponse> Suggest(String[] Arguments)
        {

            if (Arguments.Length == 1)
            {

                if (CommandName.Equals    (Arguments[0], StringComparison.CurrentCultureIgnoreCase))
                    return [ SuggestionResponse.CommandCompleted(CommandName) ];

                if (CommandName.StartsWith(Arguments[0], StringComparison.CurrentCultureIgnoreCase))
                    return [ SuggestionResponse.CommandCompleted(CommandName) ];

            }

            if (Arguments.Length >= 2 &&
                CommandName.Equals(Arguments[0], StringComparison.CurrentCultureIgnoreCase))
            {

                var subcommands  = new List<String>() { "show" };
                var list         = new List<SuggestionResponse>();

                foreach (var subcommand in subcommands)
                {

                    if (subcommand.Equals    (Arguments[1], StringComparison.CurrentCultureIgnoreCase))
                        list.Add(SuggestionResponse.ParameterCompleted($"{Arguments[0]} {subcommand}"));

                    if (subcommand.StartsWith(Arguments[1], StringComparison.CurrentCultureIgnoreCase))
                        list.Add(SuggestionResponse.ParameterPrefix   ($"{Arguments[0]} {subcommand}"));

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

                var list = new List<String>();

                cli.TestCentralSystemNode.WWCPWebSocketServers.ForEach(webSocketServer => {
                    list.AddRange(webSocketServer.ConnectedNetworkingNodeIds.Select(networkingNodeId => networkingNodeId.ToString()));
                });

                cli.TestCSMSNode.WWCPWebSocketServers.ForEach(webSocketServer => {
                    if (webSocketServer.ConnectedNetworkingNodes.Any())
                    {

                        list.Add($"{webSocketServer.Description.FirstText()} ({webSocketServer.IPSocket}):");

                        foreach (var networkingNodeConnection in webSocketServer.ConnectedNetworkingNodes)
                        {

                            var now                = Timestamp.Now;
                            var sb                 = new StringBuilder();
                            var destinationNodeId  = networkingNodeConnection.DestinationNodeId.ToString();

                            foreach (var webSocketServerConnection in networkingNodeConnection.WebSocketServerConnections)
                            {

                                sb.Append($"  {destinationNodeId}");
                                sb.Append($" ({webSocketServerConnection.RemoteSocket}, ");
                                sb.Append($"{webSocketServerConnection.TryGetCustomDataAs<NetworkingMode>(WebSocketKeys.X_WWCP_NetworkingMode) ?? NetworkingMode.Unknown}, ");

                                sb.Append($"msg in/out: {webSocketServerConnection.FramesReceivedCounter}/{webSocketServerConnection.FramesSentCounter}, ");

                                sb.Append($"last in/out: {(now - webSocketServerConnection.LastReceivedTimestamp)?.TotalSeconds.ToString("F2") ?? "-"}/");
                                sb.Append($"{  (now - webSocketServerConnection.LastSentTimestamp)?.    TotalSeconds.ToString("F2") ?? "-"} sec");

                                sb.AppendLine(")");

                                // 2nd++ line will not include the id again
                                destinationNodeId = "".Repeat(destinationNodeId.Length);

                            }

                            list.Add(sb.ToString());

                        }

                    }
                });

                return Task.FromResult<String[]>([.. list]);

            }

            return Task.FromResult<String[]>([$"Usage: {CommandName} <show>"]);

        }

        #endregion

        #region Help()

        public override String Help()
            => $"{CommandName} <networking node> - Use the networking node with the specified name";

        #endregion

    }

}
