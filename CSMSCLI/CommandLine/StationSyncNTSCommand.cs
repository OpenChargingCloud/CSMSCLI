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

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.CLI;

using cloud.charging.open.protocols.OCPP;
using cloud.charging.open.protocols.WWCP.NetworkingNode;
using cloud.charging.open.protocols.WWCP.Node.Web;
using cloud.charging.open.protocols.OCPPv2_1;
using cloud.charging.open.protocols.OCPPv2_1.CS;
using cloud.charging.open.protocols.OCPPv2_1.CSMS;
using cloud.charging.open.protocols.OCPPv2_1.NetworkingNode;

using NodeSyncNTSCommand  = cloud.charging.open.protocols.WWCP.Node.CommandLine.SyncNTSCommand;
using OCPPResultCode      = cloud.charging.open.protocols.OCPP.ResultCode;

#endregion

namespace cloud.charging.open.CSMS.CommandLine
{

    /// <summary>
    /// Have a charging station ask its time servers now, or test one of them
    /// step by step: SyncNetworkTime and TestTimeServer of the white paper
    /// "Secure Time Synchronization", sent over OCPP to a station that is
    /// connected, and what it tells afterwards in NotifyNetworkTimeResult.
    /// </summary>
    /// <remarks>
    /// <para>
    /// stationSyncNTS &lt;station&gt; [&lt;group&gt;] - a round of the group,
    /// the legal one unless another is named, with every consequence a round
    /// has on the station. stationSyncNTS &lt;station&gt; @&lt;client&gt; - a
    /// test of one time server, named by the instance of its
    /// NTPClientController, which is all OCPP knows it by and which says the
    /// group as well. syncNTS beside it is this CSMS's own: there a word is a
    /// group where the CSMS has one by that name, and a time server otherwise;
    /// here the station's groups are the station's to know, so a server is
    /// told apart by its "@", as in stationQueryDNS.
    /// </para>
    /// <para>
    /// The station answers at once, Accepted or Rejected with a reason code,
    /// and tells what came of it in a request of its own, with the requestId
    /// this command chose. That is waited for, two minutes at most, and
    /// written in the lines of syncNTS, so that the two can be laid side by
    /// side when the time is right here and not there.
    /// </para>
    /// <para>
    /// The permission is that of syncNTS, 'Sync now' and the Test button:
    /// running the time servers. The station asks its own servers; spread
    /// such requests over time where there are many stations (section 3.2.7
    /// of the white paper), because the legal time servers are everybody's.
    /// </para>
    /// </remarks>
    /// <param name="CLI">The command line.</param>
    public class StationSyncNTSCommand(CSMSCommandLine CLI) : ACLICommand<CSMSCommandLine>(CLI),
                                                            ICLICommand
    {

        #region Data

        /// <summary>
        /// The name this is typed as.
        /// </summary>
        public const String CommandName = "stationSyncNTS";

        /// <summary>
        /// How long what the station tells is waited for, once it accepted.
        /// </summary>
        public static readonly TimeSpan ResultTimeout = TimeSpan.FromMinutes(2);

        #endregion

        #region Suggest(Arguments)

        public override IEnumerable<SuggestionResponse> Suggest(String[] Arguments)
        {

            if (Arguments.Length == 1)
            {

                if (CommandName.Equals(Arguments[0], StringComparison.OrdinalIgnoreCase))
                    return [ SuggestionResponse.CommandHelp(Help()) ];

                if (CommandName.StartsWith(Arguments[0], StringComparison.OrdinalIgnoreCase))
                    return [ SuggestionResponse.CommandCompleted(CommandName) ];

                return [];

            }

            if (!CommandName.Equals(Arguments[0], StringComparison.OrdinalIgnoreCase))
                return [];

            // The stations that can be asked now - and nothing after them: the
            // station's groups and time servers are the station's to know.
            if (Arguments.Length == 2)
            {

                var before = String.Join(" ", Arguments[..^1]);
                var typed  = Arguments[^1];

                return cli.CSMS.StationLogins.Logins.
                           Select (login   => login.Id).
                           Where  (cli.CSMS.IsConnected).
                           OrderBy(station => station, StringComparer.Ordinal).
                           Where  (station => station.StartsWith(typed, StringComparison.OrdinalIgnoreCase)).
                           Select (station => station.Equals(typed, StringComparison.OrdinalIgnoreCase)
                                                  ? SuggestionResponse.ParameterCompleted($"{before} {station}")
                                                  : SuggestionResponse.ParameterPrefix   ($"{before} {station}")).
                           ToArray();

            }

            return [];

        }

        #endregion

        #region Execute(Arguments, CancellationToken)

        public override async Task<String[]> Execute(String[]           Arguments,
                                                     CancellationToken  CancellationToken)
        {

            if (Arguments.Length is < 2 or > 3)
                return [ $"Usage: {Help()}" ];

            if (!cli.MayDo(Permission.Run(NodeResources.NTS), CommandName, out var refused))
                return [ refused ];

            var station  = Arguments[1].Trim();
            var word     = Arguments.Length == 3 ? Arguments[2].Trim() : null;
            var client   = word is not null && word.StartsWith('@') ? word[1..] : null;
            var group    = word is not null && client is null       ? word      : null;

            if (client is not null && client.Length == 0)
                return [ $"Usage: {Help()}" ];

            if (!NetworkingNode_Id.TryParse(station, out var id) || !cli.CSMS.IsConnected(station))
                return [ $"The charging station '{station}' is not connected." ];

            // What the station tells afterwards carries this, and the station
            // checks it is not one it is still busy with.
            var requestId = Random.Shared.Next(1, Int32.MaxValue);

            cli.Node.Log.Notice(
                client is not null ? $"{cli.Caller.Asker} asked the charging station '{station}' over OCPP to test its time server '{client}'."
              : group  is not null ? $"{cli.Caller.Asker} asked the charging station '{station}' over OCPP to check its time against its NTS group '{group}'."
                                   : $"{cli.Caller.Asker} asked the charging station '{station}' over OCPP to synchronise its time.",
                cli.Caller.Tags("nts", "test", "ocpp", "station")
            );

            // Listened for before anything is sent: a station may tell what
            // came of a refused key exchange before its response to the
            // request has even arrived here.
            var told = new TaskCompletionSource<NotifyNetworkTimeResultRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

            OnNotifyNetworkTimeResultRequestReceivedDelegate listener = (timestamp, sender, connection, request, token) => {

                if (request.NetworkTimeRequestId == requestId &&
                    request.NetworkPath.Source   == id)
                {
                    told.TrySetResult(request);
                }

                return Task.CompletedTask;

            };

            cli.CSMS.Node.OCPP.IN.OnNotifyNetworkTimeResultRequestReceived += listener;

            try
            {

                #region Ask

                OCPPResultCode  resultCode;
                String?         description;
                GenericStatus   status;
                StatusInfo?     statusInfo;

                try
                {

                    if (client is not null)
                    {

                        var tested   = await cli.CSMS.Node.TestTimeServer(
                                                 SourceRouting.To(id),
                                                 requestId,
                                                 client,
                                                 CancellationToken: CancellationToken
                                             );

                        resultCode   = tested.Result.ResultCode;
                        description  = tested.Result.Description;
                        status       = tested.Status;
                        statusInfo   = tested.StatusInfo;

                    }
                    else
                    {

                        var synced   = await cli.CSMS.Node.SyncNetworkTime(
                                                 SourceRouting.To(id),
                                                 requestId,
                                                 group,
                                                 CancellationToken: CancellationToken
                                             );

                        resultCode   = synced.Result.ResultCode;
                        description  = synced.Result.Description;
                        status       = synced.Status;
                        statusInfo   = synced.StatusInfo;

                    }

                }
                catch (Exception e)
                {
                    return [ $"'{station}' could not be asked: {e.Message}" ];
                }

                if (resultCode != OCPPResultCode.OK)
                    return [ $"'{station}' did not answer: {resultCode}" +
                             (description is { Length: > 0 } ? $" - {description}" : "") ];

                if (status != GenericStatus.Accepted)
                    return [ $"'{station}' asked nobody: {status}" +
                             (statusInfo?.ReasonCode     is { Length: > 0 } reasonCode ? $", {reasonCode}" : "") +
                             (statusInfo?.AdditionalInfo is { Length: > 0 } why        ? $" - {why}"       : "") ];

                #endregion

                #region Wait for what it tells

                using var waiting = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

                var first = await Task.WhenAny(
                                      told.Task,
                                      Task.Delay(ResultTimeout, waiting.Token)
                                  );

                waiting.Cancel();

                if (first != told.Task)
                    return [ CancellationToken.IsCancellationRequested
                                 ? $"'{station}' accepted; stopped waiting for what it tells."
                                 : $"'{station}' accepted, and told nothing within {ResultTimeout.TotalSeconds:0} s." ];

                return Told(station, await told.Task);

                #endregion

            }
            finally
            {
                cli.CSMS.Node.OCPP.IN.OnNotifyNetworkTimeResultRequestReceived -= listener;
            }

        }

        #endregion

        #region Help()

        public override String Help()

            => $"{CommandName} <station> [<group> | @<client>] - have a connected charging station ask a group of its time " +
                "servers now, the legal one unless another is named, or test one of them step by step, by the id of its " +
                "NTPClientController; SyncNetworkTime and TestTimeServer over OCPP, told in the lines of syncNTS";

        #endregion


        #region (static) Told(Station, Request)

        /// <summary>
        /// What a station told of a round or a test, in the lines of syncNTS.
        /// </summary>
        /// <param name="Station">The charging station that was asked.</param>
        /// <param name="Request">What it told.</param>
        public static String[] Told(String                          Station,
                                    NotifyNetworkTimeResultRequest  Request)
        {

            if (Request.Round is NetworkTimeRound round)
            {

                List<String> lines = [
                                         $"Asked of the charging station '{Station}', NTS group '{round.Group}':",
                                         .. NodeSyncNTSCommand.Outcome(AsNodeRound(round))
                                     ];

                if (round.LegalTime is Boolean legal)
                    lines.Add($"  legal time: {(legal ? "yes" : "no")}" +
                              (round.LegalTimeReason is { Length: > 0 } reason ? $" - {reason}" : ""));

                return [.. lines];

            }

            if (Request.Test is TimeServerTest test)
            {

                List<String> lines = [
                                         $"Asked of the charging station '{Station}':",
                                         .. NodeSyncNTSCommand.Tested(AsNodeTest(test))
                                     ];

                if (!test.OK && test.FailedAt is { Length: > 0 } failedAt)
                    lines.Add($"  failed at: {failedAt}");

                return [.. lines];

            }

            return [ $"'{Station}' told neither a round nor a test." ];

        }

        #endregion

        #region (static) AsNodeRound(Round)

        /// <summary>
        /// A round a station told of as the JSON of the node's own round, which
        /// is what syncNTS writes its lines from.
        /// </summary>
        /// <param name="Round">The outcome of a round.</param>
        public static JObject AsNodeRound(NetworkTimeRound Round)
        {

            var json = new JObject(
                           new JProperty("ok",          Round.OK),
                           new JProperty("runtime_ms",  (Int64) Math.Round(Round.Duration * 1000)),
                           new JProperty("group",       new JObject(
                                                            new JProperty("answered",           Round.Answered),
                                                            new JProperty("required",           Round.Required),
                                                            new JProperty("offset_ms",          Milliseconds(Round.Offset)),
                                                            new JProperty("spread_ms",          Milliseconds(Round.Spread)),
                                                            new JProperty("deviationExceeded",  Round.DeviationExceeded)
                                                        )),
                           new JProperty("servers",     new JArray(Round.Clients.Select(client => new JObject(
                                                            new JProperty("hostname",      Named(client.Host, client.Client)),
                                                            new JProperty("ok",            client.OK),
                                                            new JProperty("offset_ms",     Milliseconds(client.Offset)),
                                                            new JProperty("roundTrip_ms",  Milliseconds(client.RoundTrip)),
                                                            new JProperty("keyExchange",   client.KeyExchange?.ToLowerInvariant()),
                                                            new JProperty("error",         client.Error)
                                                        ))))
                       );

            if (Round.Error is not null)
                json.Add("error", Round.Error);

            return json;

        }

        #endregion

        #region (static) AsNodeTest(Test)

        /// <summary>
        /// A test a station told of as the JSON of the node's own test, which
        /// is what syncNTS writes its lines from.
        /// </summary>
        /// <param name="Test">The outcome of a test.</param>
        public static JObject AsNodeTest(TimeServerTest Test)

            => new (
                   new JProperty("host",        Named(Test.Host, Test.Client)),
                   new JProperty("ok",          Test.OK),
                   new JProperty("runtime_ms",  (Int64) Math.Round(Test.Duration * 1000)),
                   new JProperty("steps",       new JArray(Test.Steps.Select(step => new JObject(
                                                    new JProperty("at_ms",  (Int64) Math.Round(step.Elapsed * 1000)),
                                                    new JProperty("stage",  step.Stage),
                                                    new JProperty("level",  step.Level.ToLowerInvariant()),
                                                    new JProperty("text",   step.Text)
                                                ))))
               );

        #endregion


        #region (private static) Named(Host, Client)

        /// <summary>
        /// A time server as the station names it: its host, and the id OCPP
        /// knows it by, which is what @&lt;client&gt; is typed with.
        /// </summary>
        private static String Named(String   Host,
                                    String?  Client)

            => Client is { Length: > 0 }
                   ? $"{Host.TrimEnd('.')} ({Client})"
                   : Host.TrimEnd('.');

        #endregion

        #region (private static) Milliseconds(Seconds)

        /// <summary>
        /// Seconds as OCPP tells them, as the milliseconds of the node's JSON.
        /// </summary>
        private static Double? Milliseconds(Decimal? Seconds)

            => Seconds is Decimal seconds
                   ? (Double) (seconds * 1000)
                   : null;

        #endregion

    }

}
