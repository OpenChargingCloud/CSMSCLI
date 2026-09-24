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

using System.Globalization;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.CLI;
using org.GraphDefined.Vanaheimr.Illias;

#endregion

namespace cloud.charging.open.CSMS.CommandLine
{

    /// <summary>
    /// Ask this CSMS's time servers what the time is, or one of them
    /// everything.
    /// </summary>
    /// <remarks>
    /// Without a server, the same thing the NTS page does with 'Sync now'. Both
    /// end in <see cref="CSMS.SyncTimeAsync"/>, which asks the same
    /// group of servers, writes every step into the log and keeps the result
    /// as the last synchronisation the page shows - so the log book reads the
    /// same whichever of the two was used, apart from the one line that says
    /// who asked. A command that did any of that itself would be the beginning
    /// of a CSMS disagreeing with itself about what it did.
    ///
    /// With one, the same thing as that server's Test button on the page:
    /// <see cref="CSMS.TestTimeServerAsync"/>, on the ports the
    /// server is configured with, step by step. Only a server of this CSMS
    /// is tested. Anything else is answered with the servers there are, and
    /// nothing is asked - a name that was mistyped would otherwise be a key
    /// exchange with whoever answers to it.
    ///
    /// Neither steps the clock: they say whether the time servers can be
    /// reached and what they think of the local clock.
    /// </remarks>
    /// <param name="CLI">The command line of the CSMS to ask.</param>
    public class SyncNTSCommand(CSMSCommandLine CLI) : ACLICommand<CSMSCommandLine>(CLI),
                                                       ICLICommand
    {

        #region Data

        /// <summary>
        /// The name this is typed as. Taken from the class name, as everywhere
        /// else: "SyncNTSCommand" without its last seven characters. Found in
        /// any case - "syncnts" is the same command.
        /// </summary>
        public static readonly String CommandName = nameof(SyncNTSCommand)[..^7].ToLowerFirstChar();

        #endregion

        #region Suggest(Arguments)

        /// <summary>
        /// Complete the command, and then its one argument from the time
        /// servers this CSMS has.
        /// </summary>
        /// <remarks>
        /// The servers are offered as soon as the command is whole, and not
        /// only once something of a server has been typed. The command line
        /// is split on whitespace and keeps no empty word at its end, so
        /// "syncNTS " arrives here as "syncNTS" alone - and a Tab there that
        /// answered with the command it already was would never show the list
        /// it is there to show.
        /// </remarks>
        public override IEnumerable<SuggestionResponse> Suggest(String[] Arguments)
        {

            #region The command itself - and, once it is whole, the servers

            if (Arguments.Length == 1)
            {

                if (CommandName.Equals(Arguments[0], StringComparison.CurrentCultureIgnoreCase))
                {

                    var all = TimeServers().
                                  Select(server => SuggestionResponse.ParameterPrefix($"{CommandName} {server}")).
                                  ToArray();

                    return all.Length > 0
                               ? all
                               : [ SuggestionResponse.CommandCompleted(CommandName) ];

                }

                if (CommandName.StartsWith(Arguments[0], StringComparison.CurrentCultureIgnoreCase))
                    return [ SuggestionResponse.CommandCompleted(CommandName) ];

                return [];

            }

            #endregion

            #region ... and the server to test

            if (Arguments.Length == 2 &&
                CommandName.Equals(Arguments[0], StringComparison.CurrentCultureIgnoreCase))
            {

                var list = new List<SuggestionResponse>();

                foreach (var server in TimeServers())
                {

                    if (!server.StartsWith(Arguments[1], StringComparison.CurrentCultureIgnoreCase))
                        continue;

                    list.Add(
                        server.Equals(Arguments[1], StringComparison.CurrentCultureIgnoreCase)
                            ? SuggestionResponse.ParameterCompleted($"{CommandName} {server}")
                            : SuggestionResponse.ParameterPrefix   ($"{CommandName} {server}")
                    );

                }

                return list;

            }

            #endregion

            return [];

        }

        #endregion

        #region Execute(Arguments, CancellationToken)

        public override async Task<String[]> Execute(String[]           Arguments,
                                                     CancellationToken  CancellationToken)
        {

            if (Arguments.Length > 2)
                return [ $"Usage: {Help()}" ];

            #region One server, in detail

            if (Arguments.Length == 2)
            {

                var wanted  = Arguments[1].Trim().TrimEnd('.');
                var server  = cli.CSMS.TimeSources.Sources.FirstOrDefault(source => String.Equals(source.Hostname.Trimmed,
                                                                                                  wanted,
                                                                                                  StringComparison.OrdinalIgnoreCase));

                // Said and nothing more. Not written to the log either: nothing
                // was asked of anybody, which is what the log is a book of.
                if (server is null)
                    return [ $"'{Arguments[1]}' is none of this CSMS's time servers, which are {String.Join(", ", TimeServers())}." ];

                // The name as the page sends it for the Test button of that
                // row - as it is read, without the root's dot - so that the
                // line the log gets is the page's line, with the command line
                // where the page names the account and "cli" where it says
                // "web".
                var host = server.Hostname.Trimmed;

                cli.CSMS.Log.Info(
                    $"Somebody at the command line asked this CSMS to test the time server '{host}'.",
                    "nts", "test", "cli"
                );

                return Tested(await cli.CSMS.TestTimeServerAsync(host, CancellationToken));

            }

            #endregion

            // The line the web interface writes when 'Sync now' is pressed, in
            // the same words, at the same level and with the tags it carries
            // there - "cli" where it says "web". There it names the account
            // that pressed the button; here it is whoever is at the console,
            // which the CSMS cannot tell apart. Said before anything is
            // asked, because the entries that follow record which servers were
            // asked and what came of it, and nothing in them says who wanted it.
            cli.CSMS.Log.Info(
                "Somebody at the command line asked this CSMS to synchronise its time.",
                "nts", "test", "cli"
            );

            var result = await cli.CSMS.SyncTimeAsync(CancellationToken);

            return Outcome(result);

        }

        #endregion

        #region Help()

        public override String Help()

            => $"{CommandName} [<time server>] - ask the time servers what the time is, as 'Sync now' on the NTS page does, " +
                "or test one of them in detail, as its Test button does; the clock is not stepped";

        #endregion


        #region (private) TimeServers()

        /// <summary>
        /// The time servers of this CSMS, switched on or not, as somebody
        /// types them: without the root's dot.
        /// </summary>
        /// <remarks>
        /// Switched-off ones as well, because the page tests those too: finding
        /// out whether a server answers is what somebody does before switching
        /// it on.
        /// </remarks>
        private IEnumerable<String> TimeServers()

            => cli.CSMS.TimeSources.Sources.Select(source => source.Hostname.Trimmed);

        #endregion


        #region (private static) Outcome(Result)

        /// <summary>
        /// What the NTS page shows after 'Sync now', as lines for the console:
        /// how it went, and what each server said.
        /// </summary>
        /// <remarks>
        /// The servers get a line each because the log does not have them: it
        /// records that the group was asked and what the group concluded, and
        /// which server was the one that did not answer is only in here and on
        /// the page.
        /// </remarks>
        private static String[] Outcome(JObject Result)
        {

            var lines    = new List<String>();
            var servers  = Result["servers"] as JArray ?? [];
            var took     = Result.Value<Int64?>("runtime_ms") is Int64 runtime
                               ? $" after {runtime} ms"
                               : "";

            if (Result.Value<Boolean>("ok"))
            {

                var group = Result["group"] as JObject;

                lines.Add($"succeeded{took}: {group?.Value<Int32?>("answered")} of {servers.Count} server(s) answered " +
                          $"({group?.Value<Int32?>("required")} required), " +
                          $"offset {Milliseconds(group?.Value<Double?>("offset_ms"), Signed: true)}, " +
                          $"spread {Milliseconds(group?.Value<Double?>("spread_ms"))}");

                if (group?.Value<Boolean?>("deviationExceeded") == true)
                    lines.Add("  The servers disagree by more than the agreed deviation - see the log.");

            }
            else
                lines.Add($"failed{took}: {Result.Value<String>("error") ?? "no reason was given"}");

            if (servers.Count > 0)
            {

                // Without the root's dot, as the banner writes them:
                // "ptbtime1.ptb.de." is correct and nobody reads it that way.
                static String Hostname(JToken Server)
                    => (Server.Value<String>("hostname") ?? "").TrimEnd('.');

                var width = servers.Max(server => Hostname(server).Length);

                foreach (var server in servers.OfType<JObject>())
                    lines.Add($"  {Hostname(server).PadRight(width)}  {ServerSaid(server)}");

            }

            return [.. lines];

        }

        #endregion

        #region (private static) Tested(Result)

        /// <summary>
        /// What the Test button's dialog shows, as lines for the console: whether
        /// the server answered, and then every step with when it happened.
        /// </summary>
        /// <remarks>
        /// All of the steps and not a summary, because they are the answer: a
        /// test is asked for when something did not work, and which step it
        /// got to is the whole of what somebody needs. The log has only the
        /// beginning and the end of it, as it has for the button.
        ///
        /// Warnings and errors say so, where the page says it in colour.
        /// </remarks>
        private static String[] Tested(JObject Result)
        {

            var host   = (Result.Value<String>("host") ?? "").TrimEnd('.');
            var steps  = (Result["steps"] as JArray ?? []).OfType<JObject>().ToArray();
            var at     = steps.Select(step => $"+{step.Value<Int64>("at_ms")} ms").ToArray();
            var width  = at.Length > 0 ? at.Max(when => when.Length) : 0;

            var lines  = new List<String> {
                             $"{host} {(Result.Value<Boolean>("ok") ? "answered" : "did not answer")}, " +
                             $"{Result.Value<Int64>("runtime_ms")} ms altogether:"
                         };

            for (var i = 0; i < steps.Length; i++)
            {

                var level = steps[i].Value<String>("level");

                lines.Add($"  {at[i].PadLeft(width)}  " +
                          (level is "warning" or "error" ? $"{level}: " : "") +
                          steps[i].Value<String>("text"));

            }

            return [.. lines];

        }

        #endregion

        #region (private static) ServerSaid(Server)

        /// <summary>
        /// What one server said: its offset and round trip when its answer
        /// counted, and otherwise why it did not.
        /// </summary>
        /// <remarks>
        /// An answer that did not authenticate is said to be one. The page
        /// writes "no answer" for it, and on a console that is the difference
        /// between a server that is down and one that is not the server it
        /// claims to be.
        /// </remarks>
        private static String ServerSaid(JObject Server)
        {

            if (Server.Value<Boolean>("ok"))
                return $"{Milliseconds(Server.Value<Double?>("offset_ms"), Signed: true)}, " +
                       $"round trip {Milliseconds(Server.Value<Double?>("roundTrip_ms"))}, " +
                       $"key exchange {Server.Value<String>("keyExchange") ?? "unknown"}";

            if (Server.Value<String>("error") is String error && error.Length > 0)
                return error;

            if (Server.Value<Boolean?>("authenticated") == false)
                return "answered, but the answer did not authenticate";

            return "no answer";

        }

        #endregion

        #region (private static) Milliseconds(Value, Signed = false)

        /// <summary>
        /// The one place a number with a fraction is written, and so the one
        /// place the culture has to be kept out: these sentences are English,
        /// and a CSMS in a German locale would otherwise write "+1,2 ms" in
        /// the middle of one.
        /// </summary>
        private static String Milliseconds(Double? Value, Boolean Signed = false)

            => Value is Double value
                   ? value.ToString(Signed ? "+0.0;-0.0;0.0" : "0.0", CultureInfo.InvariantCulture) + " ms"
                   : "unknown";

        #endregion

    }

}
