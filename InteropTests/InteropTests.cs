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

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

using Newtonsoft.Json.Linq;
using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod;

using cloud.charging.open.protocols.WWCP;
using cloud.charging.open.protocols.WWCP.NetworkingNode;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

using cloud.charging.open.CSMS.Tests;
using cloud.charging.open.ChargingStation.Tests;
using cloud.charging.open.LocalController.Tests;

using TheCSMS        = cloud.charging.open.CSMS.CSMS;
using TheStation     = cloud.charging.open.ChargingStation.ChargingStation;
using TheController  = cloud.charging.open.LocalController.LocalController;
using Status         = cloud.charging.open.protocols.WWCP.Node.Connections.ConnectionStatus;

namespace cloud.charging.open.Interop
{

    /// <summary>
    /// The real CSMS dialling the real charging station and the real local
    /// controller: the libraries of all three in one process, each started as
    /// its program starts it, talking over the loopback interface.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class InteropTests
    {

        private const String  CSMSName      = "csms-01";
        private const String  ThePassword   = "what-the-csms-signs-in-with-here";

        private String                          directory = "";
        private TheCSMS?                        csms;
        private readonly ConcurrentQueue<String>  csmsSaid     = new ();
        private readonly ConcurrentQueue<String>  otherSaid    = new ();
        private readonly List<IAsyncDisposable>   toDispose    = [];

        [SetUp]
        public async Task StartTheCSMS()
        {

            directory = TestCSMSs.TemporaryDirectory("interop");
            csmsSaid. Clear();
            otherSaid.Clear();
            toDispose.Clear();

            var configuration = new JObject(
                                    new JProperty("nts",  new JObject(new JProperty("groups", new JObject(new JProperty("legal", new JObject(new JProperty("enabled", false))))))),
                                    new JProperty("ocpp", new JObject(new JProperty("nodeId", Environment.GetEnvironmentVariable("INTEROP_CSMS_ID") ?? CSMSName))),
                                    new JProperty("ocppServer", new JObject(
                                        new JProperty("enabled",           true),
                                        new JProperty("address",           "127.0.0.1"),
                                        new JProperty("port",              TestPorts.Free()),
                                        new JProperty("securityProfiles",  new JArray(1)),
                                        new JProperty("subprotocols",      new JArray("ocpp2.1"))
                                    ))
                                );

            csms = await TestPorts.StartedOnFreshPorts(() => TestCSMSs.New(Path.Combine(directory, "csms"), configuration));
            csms.Log.OnLogged += entry => csmsSaid.Enqueue(entry.Message);

        }

        [TearDown]
        public async Task StopEverything()
        {

            foreach (var one in toDispose)
                try { await one.DisposeAsync(); } catch { }

            if (csms is not null)
                await csms.DisposeAsync();

            TestCSMSs.Remove(directory);

        }

        private String Log
            => $"{Environment.NewLine}--- the CSMS:{Environment.NewLine}{String.Join(Environment.NewLine, csmsSaid.TakeLast(60))}" +
               $"{Environment.NewLine}--- the other end:{Environment.NewLine}{String.Join(Environment.NewLine, otherSaid.TakeLast(60))}";

        private async Task Until(Func<Boolean> Condition, String What, Int32 Seconds = 30)
        {

            var giveUp = DateTimeOffset.UtcNow.AddSeconds(Seconds);

            while (!Condition())
            {
                if (DateTimeOffset.UtcNow > giveUp)
                    Assert.Fail($"{What} did not happen within {Seconds} seconds.{Log}");
                await Task.Delay(50);
            }

        }

        private String TheCSMSDials(String Node, String URL, String Type, String Password = ThePassword)
        {

            Assert.That(csms!.StationLogins.TrySetPassword(Node, null, null, null, out _, out var error), Is.True, error);

            if (Type == "LocalController")
                Assert.That(csms.StationLogins.TrySetLocalController(Node, true, out error), Is.True, error);

            Assert.That(csms.Connections.TryAddAuthentication($"What this CSMS signs in with at '{Node}'", "basic", CSMSName, Password, out var login, out error),
                        Is.True, error);

            Assert.That(csms.Connections.TryAddConnection($"The node '{Node}'", URL, Type, true, login, null,
                                                          out var id, out error,
                                                          RemoteNodeId: Node),
                        Is.True, error);

            return id!;

        }


        #region The charging station

        private async Task<(TheStation Station, String URL, String Inbound)> AStationThatIsDialled(String Password = ThePassword)
        {

            var port           = IPPort.Parse(TestPorts.Free());
            var configuration  = TestStations.Offline;

            configuration.Add("ocppServer", new JObject(
                                                new JProperty("enabled", true),
                                                new JProperty("port",    port.ToUInt16())
                                            ));

            var stationDirectory = Path.Combine(directory, "station");
            var station          = TestStations.New(stationDirectory, configuration);

            station.Log.OnLogged += entry => otherSaid.Enqueue(entry.Message);

            station = await TestStations.Started(station, stationDirectory);
            toDispose.Add(station);

            var url = $"ws://127.0.0.1:{port}/{CSMSName}";

            Assert.That(station.Connections.TryAddAuthentication("What the CSMS signs in with", "basic", CSMSName, Password, out var login, out var error),
                        Is.True, error);

            Assert.That(station.Connections.TryAddConnection("The CSMS, dialling in", url, "CSMS", true, login, null,
                                                             out var id, out error,
                                                             Inbound: true),
                        Is.True, error);

            return (station, url, id!);

        }

        [Test]
        public async Task TheCSMSDialsARealChargingStation_AndBothSpeakOCPP()
        {

            var (station, url, inbound) = await AStationThatIsDialled();

            var id = TheCSMSDials("cs001", url, "ChargingStation");

            await Until(() => csms!.ConnectionStates.TryGetValue(id, out var state) && state.Status == Status.Connected,
                        "The CSMS being connected to the station");

            await Until(() => station.ConnectionStates.TryGetValue(inbound, out var state) && state.Status == Status.Connected,
                        "The station taking the CSMS for connected");

            Assert.That(csms!.IsConnected("cs001"),  Is.True);
            Assert.That(csms.ConnectedStations,      Does.Contain("cs001"));

            // The station says that it started, over the line the CSMS
            // dialled, and the CSMS takes it as the station 'cs001'.
            await Until(() => otherSaid.Any(line => line.Contains("the CSMS accepted this station's OCPP 2.1 node")),
                        "The CSMS accepting the BootNotification of the station");

            // And the CSMS asks the station, by its name: answered by the
            // station itself.
            var installed = await csms.InstalledOn("cs001");

            Assert.That(installed.Asked, Is.True, $"{installed.Answer} {installed.Detail}{Log}");

            TestContext.Out.WriteLine($"GetInstalledCertificateIds: {installed.Answer} {installed.Detail}");
            TestContext.Out.WriteLine(Log);

        }

        [Test]
        public async Task ARealChargingStationTurnsAWrongPasswordAway_AndTheCSMSSaysSo()
        {

            var (station, url, inbound) = await AStationThatIsDialled();

            var id = TheCSMSDials("cs001", url, "ChargingStation", Password: "not-what-the-station-wrote-down-0");

            await Until(() => csms!.ConnectionStates.TryGetValue(id, out var state) && state.Status != Status.Connected && state.Said.Contains("401"),
                        "The CSMS saying that it was turned away");

            Assert.Multiple(() => {
                Assert.That(csms!.IsConnected("cs001"), Is.False);
                Assert.That(station.ConnectionStates.TryGetValue(inbound, out var state) && state.Status == Status.Connected, Is.False);
            });

            TestContext.Out.WriteLine(csms!.ConnectionStates[id].Said);

        }

        [Test]
        public async Task ARealChargingStationThatLetsGoIsDialledAgain()
        {

            var (station, url, inbound) = await AStationThatIsDialled();

            var id = TheCSMSDials("cs001", url, "ChargingStation");

            await Until(() => csms!.IsConnected("cs001"), "The CSMS being connected to the station");

            // The station is told to let nothing in under that name any more.
            var entry = station.Connections.Connections.Single(one => one.Id == inbound);

            Assert.That(station.Connections.TryUpdateConnection(inbound, entry.Description, entry.URL.ToString(), "CSMS", false, entry.AuthenticationId, null, out var error),
                        Is.True, error);

            await Until(() => !csms!.IsConnected("cs001"), "The CSMS noticing that the station let go");

            // And then to let it in again: the CSMS comes back by itself.
            Assert.That(station.Connections.TryUpdateConnection(inbound, entry.Description, entry.URL.ToString(), "CSMS", true, entry.AuthenticationId, null, out error),
                        Is.True, error);

            await Until(() => csms!.IsConnected("cs001"), "The CSMS coming back by itself", Seconds: 60);

            TestContext.Out.WriteLine(csms!.ConnectionStates[id].Said);

        }

        #endregion

        #region The local controller

        private async Task<(TheController Controller, String URL, String Inbound)> AControllerThatIsDialled(String Id)
        {

            var configuration = TestControllers.Offline;

            configuration["ocpp"]       = new JObject(new JProperty("nodeId", Id));
            configuration["ocppServer"] = new JObject(
                                              new JProperty("enabled",           true),
                                              new JProperty("address",           "127.0.0.1"),
                                              new JProperty("port",              TestPorts.Free()),
                                              new JProperty("securityProfiles",  new JArray(1)),
                                              new JProperty("subprotocols",      new JArray("ocpp2.1"))
                                          );
            configuration["csms"]       = new JObject(new JProperty("enabled", true));

            var controllerDirectory = Path.Combine(directory, "controller");

            var controller = await TestPorts.StartedOnFreshPorts(() => TestControllers.New(controllerDirectory, configuration));
            controller.Log.OnLogged += entry => otherSaid.Enqueue(entry.Message);
            toDispose.Add(controller);

            var url = $"ws://127.0.0.1:{controller.OCPPServerSettings.TCPPort}/{CSMSName}";

            Assert.That(controller.Connections.TryAddAuthentication("What the CSMS signs in with", "basic", CSMSName, ThePassword, out var login, out var error),
                        Is.True, error);

            Assert.That(controller.Connections.TryAddConnection("The CSMS, dialling in", url, "CSMS", true, login, null,
                                                                out var id, out error,
                                                                Inbound: true),
                        Is.True, error);

            return (controller, url, id!);

        }

        [Test]
        public async Task TheCSMSDialsARealLocalController_AndReachesAStationBehindIt()
        {

            var (controller, url, inbound) = await AControllerThatIsDialled("lc002");

            var id = TheCSMSDials("lc002", url, "LocalController");

            await Until(() => csms!.ConnectionStates.TryGetValue(id, out var state) && state.Status == Status.Connected,
                        "The CSMS being connected to the controller");

            await Until(() => controller.ConnectionStates.TryGetValue(inbound, out var state) && state.Status == Status.Connected,
                        "The controller taking the CSMS for connected");

            Assert.That(csms!.InOverlayNetwork("lc002"), Is.True);

            // A charging station behind the controller, as a line written by
            // hand: it dials the controller, and says that it started.
            const String stationPassword = "a-password-long-enough-for-ocpp";

            Assert.That(controller.StationLogins.TrySetPassword("cs050", stationPassword, null, null, out _, out var error), Is.True, error);

            using var station  = new ClientWebSocket();
            var       got      = new ConcurrentQueue<JArray>();

            station.Options.AddSubProtocol("ocpp2.1");
            station.Options.SetRequestHeader("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"cs050:{stationPassword}")));

            await station.ConnectAsync(new Uri($"ws://127.0.0.1:{controller.OCPPServerSettings.TCPPort}/cs050"), CancellationToken.None);

            using var ended = new CancellationTokenSource();

            var reading = Task.Run(async () => {

                var buffer = new Byte[1 << 16];

                try
                {
                    while (station.State == WebSocketState.Open)
                    {

                        var text = new StringBuilder();
                        WebSocketReceiveResult result;

                        do
                        {
                            result = await station.ReceiveAsync(buffer, ended.Token);
                            text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        }
                        while (!result.EndOfMessage);

                        if (result.MessageType == WebSocketMessageType.Close)
                            return;

                        var frame = JArray.Parse(text.ToString());
                        got.Enqueue(frame);

                        // What it is asked, it answers: no certificates.
                        if (frame[0]!.Value<Int32>() == 2)
                            await station.SendAsync(Encoding.UTF8.GetBytes(new JArray(3, frame[1], new JObject(new JProperty("status", "NotFound"))).ToString(Newtonsoft.Json.Formatting.None)),
                                                    WebSocketMessageType.Text, true, ended.Token);

                    }
                }
                catch { }

            });

            await station.SendAsync(Encoding.UTF8.GetBytes(new JArray(2, "boot-1", "BootNotification", new JObject(
                                                               new JProperty("reason",           "PowerUp"),
                                                               new JProperty("chargingStation",  new JObject(
                                                                   new JProperty("model",       "A model"),
                                                                   new JProperty("vendorName",  "A vendor")
                                                               ))
                                                           )).ToString(Newtonsoft.Json.Formatting.None)),
                                    WebSocketMessageType.Text, true, CancellationToken.None);

            // Answered - by the CSMS, through the controller, over the line
            // the CSMS dialled.
            await Until(() => got.Any(frame => frame[0]!.Value<Int32>() == 3 && frame[1]!.Value<String>() == "boot-1"),
                        "The BootNotification of the station behind the controller being answered");

            TestContext.Out.WriteLine($"BootNotification answered: {got.First(frame => frame[0]!.Value<Int32>() == 3)}");

            // The CSMS knows a way to the station now: through the line to
            // the controller.
            Assert.That(csms.Node.Routing.LookupNetworkingNode(NetworkingNode_Id.Parse("cs050"), out var reachability) && reachability is not null,
                        Is.True, $"The CSMS knows no way to the station behind the controller.{Log}");

            // And asks it, by its name: the request goes down the line the
            // CSMS dialled, through the controller, to the station.
            var asking = csms.Node.OCPP.OUT.GetInstalledCertificateIds(
                             new cloud.charging.open.protocols.OCPPv2_1.CSMS.GetInstalledCertificateIdsRequest(
                                 SourceRouting.To(NetworkingNode_Id.Parse("cs050")),
                                 NetworkPath: NetworkPath.From(csms.Node.Id)
                             )
                         );

            await Until(() => got.Any(frame => frame[0]!.Value<Int32>() == 2 && frame[2]!.Value<String>() == "GetInstalledCertificateIds"),
                        "The station behind the controller being asked by the CSMS");

            var answer = await asking;

            TestContext.Out.WriteLine($"GetInstalledCertificateIds answered: {answer.Status} {answer.Result}");
            TestContext.Out.WriteLine(Log);

            Assert.That(answer.Result.ResultCode.ToString(), Is.EqualTo("OK"), $"{answer.Result}{Log}");
            Assert.That(answer.Status.ToString(), Is.EqualTo("NotFound"), $"{answer.Result}{Log}");

            // And the controller itself, by its name: answered by the real
            // local controller.
            var ofTheController = await csms.Node.OCPP.OUT.GetInstalledCertificateIds(
                                            new cloud.charging.open.protocols.OCPPv2_1.CSMS.GetInstalledCertificateIdsRequest(
                                                SourceRouting.To(NetworkingNode_Id.Parse("lc002")),
                                                NetworkPath: NetworkPath.From(csms.Node.Id)
                                            )
                                        );

            TestContext.Out.WriteLine($"The controller itself answered: {ofTheController.Status} {ofTheController.Result}");

            Assert.That(ofTheController.Result.ResultCode.ToString(), Is.EqualTo("OK"), $"{ofTheController.Result}{Log}");

            ended.Cancel();
            try { station.Abort(); } catch { }
            try { await reading;   } catch { }

        }

        #endregion

    }

}
