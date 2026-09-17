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
using System.Collections.Generic;

#endregion

namespace org.GraphDefined.OCPP.CSMS.TestApp.CommandLine
{

    /// <summary>
    /// Use the networking node with the specified name
    /// </summary>
    /// <param name="CLI">The command line interface</param>
    public class ServersCommand(CSMSTestCLI CLI) : ACLICommand<CSMSTestCLI>(CLI),
                                                   ICLICommand
    {

        #region Data

        public static readonly String CommandName = nameof(ServersCommand)[..^7].ToLowerFirstChar();

        #endregion

        private IEnumerable<IWWCPWebSocketServer> AllServers
            => cli.TestCentralSystemNode.WWCPWebSocketServers.Concat(cli.TestCSMSNode.WWCPWebSocketServers).
                   OrderBy(webSocketServer => webSocketServer.Description.FirstText());

        private IWWCPWebSocketServer GetServer(UInt16 ServerId)
            => AllServers.ElementAt(ServerId-1);


        #region Suggest(Arguments)

        public override IEnumerable<SuggestionResponse> Suggest(String[] Arguments)
        {

            if (Arguments.Length >= 1)
            {

                if (CommandName.Equals(Arguments[0], StringComparison.CurrentCultureIgnoreCase))
                {

                    if (Arguments.Length == 1)
                    {

                        var list = new List<SuggestionResponse>() {
                            SuggestionResponse.CommandCompleted($"{CommandName} show")
                        };

                        var maxServerId = cli.TestCentralSystemNode.WWCPWebSocketServers.Concat(cli.TestCSMSNode.WWCPWebSocketServers).Count();

                        for (var i=1; i <= maxServerId; i++)
                            list.Add(SuggestionResponse.ParameterCompleted($"{Arguments[0]} {i}"));

                        return list;

                    }

                    if (Arguments.Length >= 2)
                    {

                        if ("show".Equals    (Arguments[1], StringComparison.CurrentCultureIgnoreCase))
                            return [ SuggestionResponse.ParameterCompleted($"{Arguments[0]} show") ];

                        if ("show".StartsWith(Arguments[1], StringComparison.CurrentCultureIgnoreCase))
                            return [ SuggestionResponse.ParameterPrefix   ($"{Arguments[0]} show") ];


                        if (UInt16.TryParse(Arguments[1], out var serverId))
                        {

                            var maxServerId = AllServers.Count();

                            // ToDo: Handle more than 9 servers!
                            if (serverId < 1 || serverId > maxServerId)
                                return [
                                    SuggestionResponse.CommandCompleted($"{Arguments[0]}")
                                ];


                            if (Arguments.Length >= 3)
                            {

                                if (Arguments[2].Equals("disableWebSocketPings",      StringComparison.CurrentCultureIgnoreCase))
                                {

                                    if (Arguments.Length >= 4)
                                    {

                                        if ("true". Equals    (Arguments[3], StringComparison.CurrentCultureIgnoreCase))
                                            return [SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} disableWebSocketPings true")];

                                        if ("true". StartsWith(Arguments[3], StringComparison.CurrentCultureIgnoreCase))
                                            return [SuggestionResponse.ParameterPrefix   ($"{Arguments[0]} {serverId} disableWebSocketPings true")];


                                        if ("false".Equals    (Arguments[3], StringComparison.CurrentCultureIgnoreCase))
                                            return [SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} disableWebSocketPings false")];

                                        if ("false".StartsWith(Arguments[3], StringComparison.CurrentCultureIgnoreCase))
                                            return [SuggestionResponse.ParameterPrefix   ($"{Arguments[0]} {serverId} disableWebSocketPings false")];

                                    }

                                    return [
                                        SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} disableWebSocketPings true"),
                                        SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} disableWebSocketPings false")
                                    ];

                                }

                                if (Arguments[2].Equals("maxBinaryMessageSize",       StringComparison.CurrentCultureIgnoreCase))
                                {

                                    if (Arguments.Length >= 4)
                                    {

                                        if (UInt64.TryParse(Arguments[3], out var maxBinaryMessageSize))
                                            return [SuggestionResponse.ParameterPrefix($"{Arguments[0]} {serverId} maxBinaryMessageSize {maxBinaryMessageSize}")];

                                        return [SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} maxBinaryMessageSize")];

                                    }

                                    return [
                                        SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} maxBinaryMessageSize")
                                    ];

                                }

                                if (Arguments[2].Equals("maxTextMessageSize",         StringComparison.CurrentCultureIgnoreCase))
                                {

                                    if (Arguments.Length >= 4)
                                    {

                                        if (UInt64.TryParse(Arguments[3], out var maxTextMessageSize))
                                            return [SuggestionResponse.ParameterPrefix($"{Arguments[0]} {serverId} maxTextMessageSize {maxTextMessageSize}")];

                                        return [SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} maxTextMessageSize")];

                                    }

                                    return [
                                        SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} maxTextMessageSize")
                                    ];

                                }

                                if (Arguments[2].Equals("requireAuthentication",      StringComparison.CurrentCultureIgnoreCase))
                                {

                                    if (Arguments.Length >= 4)
                                    {

                                        if ("true". Equals    (Arguments[3], StringComparison.CurrentCultureIgnoreCase))
                                            return [SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} requireAuthentication true")];

                                        if ("true". StartsWith(Arguments[3], StringComparison.CurrentCultureIgnoreCase))
                                            return [SuggestionResponse.ParameterPrefix   ($"{Arguments[0]} {serverId} requireAuthentication true")];


                                        if ("false".Equals    (Arguments[3], StringComparison.CurrentCultureIgnoreCase))
                                            return [SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} requireAuthentication false")];

                                        if ("false".StartsWith(Arguments[3], StringComparison.CurrentCultureIgnoreCase))
                                            return [SuggestionResponse.ParameterPrefix   ($"{Arguments[0]} {serverId} requireAuthentication false")];

                                    }

                                    return [
                                        SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} requireAuthentication true"),
                                        SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} requireAuthentication false")
                                    ];

                                }

                                if (Arguments[2].Equals("slowNetworkSimulationDelay", StringComparison.CurrentCultureIgnoreCase))
                                {

                                    if (Arguments.Length >= 4)
                                    {

                                        if (UInt64.TryParse(Arguments[3], out var slowNetworkSimulationDelay))
                                            return [SuggestionResponse.ParameterPrefix($"{Arguments[0]} {serverId} slowNetworkSimulationDelay {slowNetworkSimulationDelay}")];

                                        return [SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} slowNetworkSimulationDelay")];

                                    }

                                    return [
                                        SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} slowNetworkSimulationDelay")
                                    ];

                                }

                                if (Arguments[2].Equals("webSocketPingEvery",         StringComparison.CurrentCultureIgnoreCase))
                                {

                                    if (Arguments.Length >= 4)
                                    {

                                        if (UInt64.TryParse(Arguments[3], out var webSocketPingEvery))
                                            return [SuggestionResponse.ParameterPrefix($"{Arguments[0]} {serverId} webSocketPingEvery {webSocketPingEvery}")];

                                        return [SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} webSocketPingEvery")];

                                    }

                                    return [
                                        SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} webSocketPingEvery")
                                    ];

                                }


                                var list3 = new List<SuggestionResponse>();

                                if ("disableWebSocketPings".     StartsWith(Arguments[2], StringComparison.OrdinalIgnoreCase))
                                    list3.Add(SuggestionResponse.ParameterPrefix($"{Arguments[0]} {serverId} disableWebSocketPings"));

                                if ("maxBinaryMessageSize".      StartsWith(Arguments[2], StringComparison.OrdinalIgnoreCase))
                                    list3.Add(SuggestionResponse.ParameterPrefix($"{Arguments[0]} {serverId} maxBinaryMessageSize"));

                                if ("maxTextMessageSize".        StartsWith(Arguments[2], StringComparison.OrdinalIgnoreCase))
                                    list3.Add(SuggestionResponse.ParameterPrefix($"{Arguments[0]} {serverId} maxTextMessageSize"));

                                if ("requireAuthentication".     StartsWith(Arguments[2], StringComparison.OrdinalIgnoreCase))
                                    list3.Add(SuggestionResponse.ParameterPrefix($"{Arguments[0]} {serverId} requireAuthentication"));

                                if ("slowNetworkSimulationDelay".StartsWith(Arguments[2], StringComparison.OrdinalIgnoreCase))
                                    list3.Add(SuggestionResponse.ParameterPrefix($"{Arguments[0]} {serverId} slowNetworkSimulationDelay"));

                                if ("webSocketPingEvery".        StartsWith(Arguments[2], StringComparison.OrdinalIgnoreCase))
                                    list3.Add(SuggestionResponse.ParameterPrefix($"{Arguments[0]} {serverId} webSocketPingEvery"));


                                return list3.Count > 0
                                           ? list3
                                           : [ SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId}") ];

                            }

                            return [
                                SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} disableWebSocketPings"),
                                SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} maxBinaryMessageSize"),
                                SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} maxTextMessageSize"),
                                SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} requireAuthentication"),
                                SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} slowNetworkSimulationDelay"),
                                SuggestionResponse.ParameterCompleted($"{Arguments[0]} {serverId} webSocketPingEvery")
                            ];

                        }

                        return [
                            SuggestionResponse.CommandCompleted($"{Arguments[0]}")
                        ];

                    }

                }

                if (CommandName.StartsWith(Arguments[0], StringComparison.CurrentCultureIgnoreCase))
                    return [SuggestionResponse.CommandCompleted(CommandName)];

            }

            return [];

        }

        #endregion

        #region Execute(Arguments, CancellationToken)

        public override Task<String[]> Execute(String[]           Arguments,
                                               CancellationToken  CancellationToken)
        {

            if (Arguments.Length >= 2)
            {

                if (Arguments[1].Equals("show", StringComparison.OrdinalIgnoreCase))
                {

                    var list = new List<String>();

                    AllServers.ForEachCounted((webSocketServer, i) => {

                        list.Add($"{i}: {webSocketServer.Description.FirstText()}");
                        list.Add($"   IP Socket:                     {webSocketServer.IPSocket}");
                        list.Add($"   RequireAuthentication:         {webSocketServer.RequireAuthentication}");
                        list.Add($"   DisableWebSocketPings:         {webSocketServer.DisableWebSocketPings}");
                        list.Add($"   WebSocketPingEvery:            {webSocketServer.WebSocketPingEvery.TotalSeconds:F2}");
                        list.Add($"   MaxTextMessageSize:            {webSocketServer.MaxTextMessageSizeOut?.ToString() ?? "-"}");
                        list.Add($"   MaxBinaryMessageSize:          {webSocketServer.MaxBinaryMessageSizeOut?.ToString() ?? "-"}");
                        list.Add($"   SecWebSocketProtocols:         {webSocketServer.SecWebSocketProtocols.AggregateWith(", ")}");
                        list.Add($"   SlowNetworkSimulationDelay:    {webSocketServer.SlowNetworkSimulationDelay?.TotalSeconds.ToString("F2") ?? "-"}");
                        //list.Add($"   TrustedClientCertificates:     {webSocketServer.TrustedClientCertificates}");
                        //list.Add($"   TrustedCertificatAuthorities:  {webSocketServer.TrustedCertificatAuthorities}");
                        list.Add("");

                    });

                    return Task.FromResult<String[]>([.. list]);

                }

                if (UInt16.TryParse(Arguments[1], out var serverId))
                {

                    if (Arguments.Length >= 3)
                    {

                        if (Arguments[2].Equals("disableWebSocketPings",      StringComparison.OrdinalIgnoreCase))
                        {

                            if (Arguments.Length >= 4)
                            {
                                if (Arguments[3].Equals("true",  StringComparison.OrdinalIgnoreCase))
                                {
                                    GetServer(serverId).DisableWebSocketPings = true;
                                    return Task.FromResult<String[]>([$"success"]);
                                }

                                if (Arguments[3].Equals("false", StringComparison.OrdinalIgnoreCase))
                                {
                                    GetServer(serverId).DisableWebSocketPings = false;
                                    return Task.FromResult<String[]>([$"success"]);
                                }
                            }

                            return Task.FromResult<String[]>([$"Usage: {CommandName} {serverId} disableWebSocketPings <true|false>"]);

                        }

                        if (Arguments[2].Equals("maxBinaryMessageSize",       StringComparison.OrdinalIgnoreCase))
                        {

                            if (Arguments.Length >= 4)
                            {
                                if (UInt64.TryParse(Arguments[3], out var maxBinaryMessageSize))
                                {
                                    GetServer(serverId).MaxBinaryMessageSizeOut = maxBinaryMessageSize;
                                    return Task.FromResult<String[]>([$"success"]);
                                }
                            }

                            return Task.FromResult<String[]>([$"Usage: {CommandName} {serverId} maxBinaryMessageSize <number of bytes>"]);

                        }

                        if (Arguments[2].Equals("maxTextMessageSize",         StringComparison.OrdinalIgnoreCase))
                        {

                            if (Arguments.Length >= 4)
                            {
                                if (UInt64.TryParse(Arguments[3], out var maxTextMessageSize))
                                {
                                    GetServer(serverId).MaxTextMessageSizeOut = maxTextMessageSize;
                                    return Task.FromResult<String[]>([$"success"]);
                                }
                            }

                            return Task.FromResult<String[]>([$"Usage: {CommandName} {serverId} maxTextMessageSize <number of bytes>"]);

                        }

                        if (Arguments[2].Equals("requireAuthentication",      StringComparison.OrdinalIgnoreCase))
                        {

                            if (Arguments.Length >= 4)
                            {
                                if (Arguments[3].Equals("true",  StringComparison.OrdinalIgnoreCase))
                                {
                                    GetServer(serverId).RequireAuthentication = true;
                                    return Task.FromResult<String[]>([$"success"]);
                                }

                                if (Arguments[3].Equals("false", StringComparison.OrdinalIgnoreCase))
                                {
                                    GetServer(serverId).RequireAuthentication = false;
                                    return Task.FromResult<String[]>([$"success"]);
                                }
                            }

                            return Task.FromResult<String[]>([$"Usage: {CommandName} {serverId} requireAuthentication <true|false>"]);

                        }

                        if (Arguments[2].Equals("slowNetworkSimulationDelay", StringComparison.OrdinalIgnoreCase))
                        {

                            if (Arguments.Length >= 4)
                            {
                                if (UInt64.TryParse(Arguments[3], out var slowNetworkSimulationDelay))
                                {
                                    GetServer(serverId).SlowNetworkSimulationDelay = TimeSpan.FromMilliseconds(slowNetworkSimulationDelay);
                                    return Task.FromResult<String[]>([$"success"]);
                                }
                            }

                            return Task.FromResult<String[]>([$"Usage: {CommandName} {serverId} slowNetworkSimulationDelay <milliseconds>"]);

                        }

                        if (Arguments[2].Equals("webSocketPingEvery",         StringComparison.OrdinalIgnoreCase))
                        {

                            if (Arguments.Length >= 4)
                            {
                                if (UInt64.TryParse(Arguments[3], out var webSocketPingEvery))
                                {
                                    GetServer(serverId).WebSocketPingEvery = TimeSpan.FromSeconds(webSocketPingEvery);
                                    return Task.FromResult<String[]>([$"success"]);
                                }
                            }

                            return Task.FromResult<String[]>([$"Usage: {CommandName} {serverId} webSocketPingEvery <seconds>"]);

                        }

                    }

                    else
                        return Task.FromResult<String[]>([$"Usage: {CommandName} {serverId} <disableWebSocketPings|maxBinaryMessageSize|maxTextMessageSize|requireAuthentication|slowNetworkSimulationDelay|webSocketPingEvery> ..."]);

                }

            }

            var list2        = new List<String>();
            var maxServerId  = cli.TestCentralSystemNode.WWCPWebSocketServers.Concat(cli.TestCSMSNode.WWCPWebSocketServers).Count();

            for (var i = 1; i <= maxServerId; i++)
                list2.Add($"{i}");

            var serverIds = list2.Count > 0
                                ? "|" + list2.AggregateWith("|")
                                : "";

            return Task.FromResult<String[]>([$"Usage: {CommandName} <show{serverIds}>"]);

        }

        #endregion

        #region Help()

        public override String Help()
            => $"{CommandName} <show|serverId> ... - Show or change HTTP WebSocket server settings";

        #endregion

    }

}
