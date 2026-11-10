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

using cloud.charging.open.protocols.WWCP.NetworkingNode;
using cloud.charging.open.protocols.WWCP.Node.Web;
using cloud.charging.open.protocols.OCPPv2_1;
using cloud.charging.open.protocols.OCPPv2_1.CS;
using cloud.charging.open.protocols.OCPPv2_1.CSMS;

using NodeQueryDNSCommand  = cloud.charging.open.protocols.WWCP.Node.CommandLine.QueryDNSCommand;
using OCPPResultCode       = cloud.charging.open.protocols.OCPP.ResultCode;

#endregion

namespace cloud.charging.open.CSMS.CommandLine
{

    /// <summary>
    /// Have a charging station look a name up with its own name servers:
    /// QueryDNS of the white paper "Advanced Diagnostics for OCPP", sent over
    /// OCPP to a station that is connected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// stationQueryDNS &lt;station&gt; &lt;name&gt; [&lt;record type&gt; ...]
    /// [@&lt;serverId&gt;] - without a server the station asks the way all of
    /// its services ask, cache included; with one, that name server alone and
    /// without the cache, named by the instance of its DNSServerController.
    /// An IP address is asked for as its reverse name.
    /// </para>
    /// <para>
    /// queryDNS beside it is this CSMS's own name resolution. What the station
    /// answered is written in the same lines - the question, the RCODE, the
    /// server it came from, the flags, each record, the verdict on each
    /// certificate - so that the two can be laid side by side when a name
    /// resolves here and not there.
    /// </para>
    /// <para>
    /// The permission is that of queryDNS and of the DNS test of the web
    /// interface: running the DNS test. The record types and the server are
    /// the station's to judge, and it says so with a reason code where it asks
    /// nobody; this console does not guess its list for it.
    /// </para>
    /// </remarks>
    /// <param name="CLI">The command line.</param>
    public class StationQueryDNSCommand(CSMSCommandLine CLI) : ACLICommand<CSMSCommandLine>(CLI),
                                                             ICLICommand
    {

        #region Data

        /// <summary>
        /// The name this is typed as.
        /// </summary>
        public const String CommandName = "stationQueryDNS";

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

            var before = String.Join(" ", Arguments[..^1]);
            var typed  = Arguments[^1];

            // The stations that can be asked now; the name is somebody's to
            // type; the record types queryDNS offers - but not the station's
            // name servers, which only the station knows.
            return Arguments.Length switch {
                       2                             => Offered(cli.CSMS.StationLogins.Logins.
                                                                    Select (login   => login.Id).
                                                                    Where  (cli.CSMS.IsConnected).
                                                                    OrderBy(station => station, StringComparer.Ordinal),
                                                                typed,
                                                                before),
                       > 3 when !typed.StartsWith('@')  => Offered(NodeQueryDNSCommand.OfferedRecordTypes, typed, before),
                       _                             => []
                   };

        }

        #endregion

        #region (private static) Offered(Words, Typed, Before)

        /// <summary>
        /// The words that begin with what was typed, each after what came
        /// before it: completed where it is the whole word, a prefix otherwise.
        /// </summary>
        private static IEnumerable<SuggestionResponse> Offered(IEnumerable<String>  Words,
                                                               String               Typed,
                                                               String               Before)
        {

            foreach (var word in Words)
            {

                if (!word.StartsWith(Typed, StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return word.Equals(Typed, StringComparison.OrdinalIgnoreCase)
                                 ? SuggestionResponse.ParameterCompleted($"{Before} {word}")
                                 : SuggestionResponse.ParameterPrefix   ($"{Before} {word}");

            }

        }

        #endregion

        #region Execute(Arguments, CancellationToken)

        public override async Task<String[]> Execute(String[]           Arguments,
                                                     CancellationToken  CancellationToken)
        {

            if (Arguments.Length < 3)
                return [ $"Usage: {Help()}" ];

            if (!cli.MayDo(Permission.Run(NodeResources.DNS), CommandName, out var refused))
                return [ refused ];

            var station  = Arguments[1].Trim();
            var name     = Arguments[2].Trim();
            var servers  = Arguments.Skip(3).Where(argument =>  argument.StartsWith('@')).ToArray();
            var types    = Arguments.Skip(3).Where(argument => !argument.StartsWith('@')).ToArray();

            if (servers.Length > 1)
                return [ $"Name one name server at most, not {String.Join(" and ", servers)}." ];

            var serverId = servers.Length == 1 ? servers[0][1..] : null;

            if (serverId is not null && serverId.Length == 0)
                return [ $"Usage: {Help()}" ];

            if (!NetworkingNode_Id.TryParse(station, out var id) || !cli.CSMS.IsConnected(station))
                return [ $"The charging station '{station}' is not connected." ];

            cli.Node.Log.Info(
                $"{cli.Caller.Asker} asked the charging station '{station}' over OCPP to resolve '{name}'" +
                (serverId is not null ? $" with its name server '{serverId}'." : "."),
                cli.Caller.Tags("dns", "test", "ocpp", "station")
            );

            QueryDNSResponse response;

            try
            {
                response = await cli.CSMS.Node.QueryDNS(
                                     SourceRouting.To(id),
                                     name,
                                     types.Length > 0 ? types : null,
                                     serverId,
                                     CancellationToken: CancellationToken
                                 );
            }
            catch (Exception e)
            {
                return [ $"'{station}' could not be asked: {e.Message}" ];
            }

            return Answered(station, response);

        }

        #endregion

        #region Help()

        public override String Help()

            => $"{CommandName} <station> <name> [<record type> ...] [@<serverId>] - have a connected charging station look a " +
                "name or an address up with its own name servers, all of them the way it looks up anything, or one of them " +
                "alone without the cache; QueryDNS over OCPP, answered in the lines of queryDNS";

        #endregion


        #region (static) Answered(Station, Response)

        /// <summary>
        /// What a station answered, in the lines of queryDNS: where it asked,
        /// the question and what came of it; where it asked nobody, why; and
        /// where it did not answer at all, that.
        /// </summary>
        /// <param name="Station">The charging station that was asked.</param>
        /// <param name="Response">Its response.</param>
        public static String[] Answered(String            Station,
                                        QueryDNSResponse  Response)
        {

            if (Response.Result.ResultCode != OCPPResultCode.OK)
                return [ $"'{Station}' did not answer: {Response.Result.ResultCode}" +
                         (Response.Result.Description is { Length: > 0 } description ? $" - {description}" : "") ];

            if (Response.Status != GenericStatus.Accepted)
            {

                var why = Response.Error ?? Response.StatusInfo?.AdditionalInfo;

                return [ $"'{Station}' asked nobody: {Response.Status}" +
                         (Response.StatusInfo?.ReasonCode is { Length: > 0 } reasonCode ? $", {reasonCode}" : "") +
                         (why is { Length: > 0 } ? $" - {why}" : "") ];

            }

            return [ $"Asked of the charging station '{Station}':",
                     .. NodeQueryDNSCommand.Answered(AsNodeAnswer(Response)) ];

        }

        #endregion

        #region (static) AsNodeAnswer(Response)

        /// <summary>
        /// A station's response as the JSON the node's own resolution gives,
        /// which is what queryDNS writes its lines from.
        /// </summary>
        /// <param name="Response">An accepted QueryDNS response.</param>
        public static JObject AsNodeAnswer(QueryDNSResponse Response)
        {

            var json = new JObject(
                           new JProperty("name",         Response.QueriedName ?? ""),
                           new JProperty("recordTypes",  new JArray(Response.RecordTypes))
                       );

            if (Response.ReverseLookupOf is not null)
                json.Add("turnedAround",  $"'{Response.ReverseLookupOf}' is an address, so it was turned around and '{Response.QueriedName}' was asked for instead.");

            if (Response.ServerId       is not null)
                json.Add("serverId",      Response.ServerId);

            if (Response.AnsweredBy     is not null)
                json.Add("answeredBy",    Response.AnsweredBy);

            if (Response.ResponseCode   is not null)
                json.Add("responseCode",  Response.ResponseCode);

            if (Response.Authoritative  is not null)
                json.Add("authoritative", Response.Authoritative.Value);

            if (Response.Truncated      is not null)
                json.Add("truncated",     Response.Truncated.Value);

            if (Response.TimedOut       is not null)
                json.Add("timedOut",      Response.TimedOut.Value);

            if (Response.DNSSEC         is not null)
                json.Add("dnssec",        Response.DNSSEC);

            if (Response.QueryRuntime   is not null)
                json.Add("runtime_ms",    (Int64) Math.Round(Response.QueryRuntime.Value.TotalMilliseconds));

            if (Response.Error          is not null)
                json.Add("error",         Response.Error);

            json.Add("answers", new JArray(Response.Answers.Select(answer => new JObject(
                                    new JProperty("name",        answer.Name),
                                    new JProperty("type",        answer.Type),
                                    new JProperty("timeToLive",  (Int64) answer.TimeToLive.TotalSeconds),
                                    new JProperty("value",       answer.Value)
                                ))));

            if (Response.OmittedAnswers is not null)
                json.Add("more",          (Int64) Response.OmittedAnswers.Value);

            json.Add("certificates", new JArray(Response.Certificates.Select(certificate => {

                                         var one = new JObject(
                                                       new JProperty("server",    certificate.Server),
                                                       new JProperty("accepted",  certificate.Accepted),
                                                       new JProperty("outcome",   certificate.Status)
                                                   );

                                         if (certificate.CertificateFingerprint is not null)
                                             one.Add("certificate", certificate.CertificateFingerprint);

                                         if (certificate.RootFingerprint        is not null)
                                             one.Add("root",        certificate.RootFingerprint);

                                         return one;

                                     })));

            return json;

        }

        #endregion

    }

}
