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

using OCPPv1_6 = cloud.charging.open.protocols.OCPPv1_6;
using OCPPv2_1 = cloud.charging.open.protocols.OCPPv2_1;

#endregion

namespace org.GraphDefined.OCPP.CSMS.TestApp.CommandLine
{

    /// <summary>
    /// Use the networking node with the specified name
    /// </summary>
    /// <param name="CLI">The command line interface</param>
    public class SetDefaultRegistrationStatusCommand(CSMSTestCLI CLI) : ACLICommand<CSMSTestCLI>(CLI),
                                                                        ICLICommand
    {

        #region Data

        public static readonly String CommandName = nameof(SetDefaultRegistrationStatusCommand)[..^7].ToLowerFirstChar();


        private readonly IEnumerable<String> allRegistrationStatus =

            OCPPv1_6.RegistrationStatus.All.Select(registrationStatus => registrationStatus.ToString()).Concat(
            OCPPv2_1.RegistrationStatus.All.Select(registrationStatus => registrationStatus.ToString())).

            Where(registrationStatus => !registrationStatus.Equals("unknown", StringComparison.OrdinalIgnoreCase) &&
                                           !registrationStatus.Equals("error", StringComparison.OrdinalIgnoreCase) &&
                                           !registrationStatus.Equals("signatureError", StringComparison.OrdinalIgnoreCase)).
            Distinct().
            Order();

        #endregion

        #region Suggest(Arguments)

        public override IEnumerable<SuggestionResponse> Suggest(String[] Arguments)
        {

            if (Arguments.Length == 1)
            {

                if (CommandName.Equals(Arguments[0], StringComparison.CurrentCultureIgnoreCase))
                {

                    var list = new List<SuggestionResponse>();

                    foreach (var registrationStatus in allRegistrationStatus)
                    {
                        list.Add(SuggestionResponse.ParameterCompleted($"{Arguments[0]} {registrationStatus}"));
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

                foreach (var registrationStatus in allRegistrationStatus)
                {

                    if (registrationStatus.Equals    (Arguments[1], StringComparison.CurrentCultureIgnoreCase))
                        list.Add(SuggestionResponse.ParameterCompleted($"{Arguments[0]} {registrationStatus}"));

                    else if (registrationStatus.StartsWith(Arguments[1], StringComparison.CurrentCultureIgnoreCase))
                        list.Add(SuggestionResponse.ParameterPrefix   ($"{Arguments[0]} {registrationStatus}"));

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

            if (Arguments.Length >= 2)
            {

                var list = new List<String>();

                if (OCPPv1_6.RegistrationStatus.IsDefined(Arguments[1], out var registrationStatus16))
                {
                    cli.TestCentralSystemNode.OCPP.DefaultRegistrationStatus = registrationStatus16;
                    list.Add($"OCPP v1.6 default registration status set to: '{registrationStatus16}'!");
                }
                else
                    list.Add($"Unknown OCPP v1.6 registration status '{Arguments[1]}'!");


                if (OCPPv2_1.RegistrationStatus.IsDefined(Arguments[1], out var registrationStatus21))
                {
                    cli.TestCSMSNode.         OCPP.DefaultRegistrationStatus = registrationStatus21;
                    list.Add($"OCPP v2.1 default registration status set to: '{registrationStatus21}'!");
                }

                else
                    list.Add($"Unknown OCPP v2.1 registration status '{Arguments[1]}'!");

                return Task.FromResult(list.ToArray());

            }

            return Task.FromResult<String[]>([$"Usage: {CommandName} <registration status>"]);

        }

        #endregion

        #region Help()

        public override String Help()
            => $"{CommandName} <registration status> - Set the default registration status";

        #endregion

    }

}
