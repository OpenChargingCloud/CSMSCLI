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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Newtonsoft.Json.Linq;
using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod;

using cloud.charging.open.protocols.WWCP.Node.Certificates;
using cloud.charging.open.protocols.WWCP.Node.CommandLine;
using cloud.charging.open.protocols.WWCP.Node.Configuration;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

using cloud.charging.open.CSMS.Tests;
using cloud.charging.open.ChargingStation.Tests;
using cloud.charging.open.LocalController.Tests;

using OCPPConnections = cloud.charging.open.protocols.WWCP.Node.Connections;

using TheCSMS     = cloud.charging.open.CSMS.CSMS;
using TheStation  = cloud.charging.open.ChargingStation.ChargingStation;
using Status      = cloud.charging.open.protocols.WWCP.Node.Connections.ConnectionStatus;

namespace cloud.charging.open.Interop
{

    /// <summary>
    /// The real CSMS dialling the real charging station over TLS, and signing
    /// in with a client certificate - known by its key, or by its CA - or with
    /// a one-time password bound to its TLS session, over HTTP/3.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class InteropStationSignInTests
    {

        private const String  CSMSName         = "csms-01";
        private const String  StationName      = "cs100";
        private const String  TheSharedSecret  = "a-shared-secret-long-enough-for-ocpp";

        private String      directory      = "";
        private TestCA      ca             = default!;
        private TheCSMS     csms           = default!;
        private TheStation  station        = default!;
        private UInt16      port;
        private String      stationKeyPin  = "";

        private readonly ConcurrentQueue<String>  csmsSaid   = new ();
        private readonly ConcurrentQueue<String>  otherSaid  = new ();

        private static UInt16 APortFreeForBoth()
        {

            for (var attempt = 0; attempt < 200; attempt++)
            {

                var port = Random.Shared.Next(20000, 32768);

                try
                {

                    using var udp4 = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback,     port));
                    using var udp6 = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.IPv6Loopback, port));

                    var tcp4 = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                    tcp4.Start();
                    tcp4.Stop();

                    return (UInt16) port;

                }
                catch (System.Net.Sockets.SocketException)
                { }

            }

            throw new InvalidOperationException("No port free for TCP and UDP was found.");

        }

        private String TheStationsIdentityFile()
        {

            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            var request   = new CertificateRequest("CN=charging station", key, HashAlgorithmName.SHA256);
            var names     = new SubjectAlternativeNameBuilder();

            names.AddIpAddress(System.Net.IPAddress.Loopback);
            names.AddDnsName("localhost");

            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([ new Oid("1.3.6.1.5.5.7.3.1") ], false));

            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

            stationKeyPin = KeyPin.FromCertificate(certificate).ToHex();

            var file = Path.Combine(directory, "station.p12");

            File.WriteAllBytes(file, certificate.Export(X509ContentType.Pkcs12));

            return file;

        }

        [SetUp]
        public async Task StartBoth()
        {

            directory = TestCSMSs.TemporaryDirectory("interop-station-signin");
            Directory.CreateDirectory(directory);
            csmsSaid. Clear();
            otherSaid.Clear();

            ca = TestCA.Create($"Operator CA {Guid.NewGuid()}", WithIntermediate: Environment.GetEnvironmentVariable("INTEROP_FLAT") != "1");

            #region The CSMS

            var configuration = new JObject(
                                    new JProperty("nts",  new JObject(new JProperty("groups", new JObject(new JProperty("legal", new JObject(new JProperty("enabled", false))))))),
                                    new JProperty("ocpp", new JObject(new JProperty("nodeId", CSMSName))),
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

            #endregion

            #region The charging station, with TLS and HTTP/3 on its OCPP server

            var arguments = NodeArguments.Parse([ "--https", "--import-certificate", $"tlsServerIdentity={TheStationsIdentityFile()}" ]);

            Assert.That(arguments.Problem, Is.Null);

            var attempt = 0;

            station = await TestPorts.StartedOnFreshPorts(() => {

                          port = APortFreeForBoth();

                          var stationConfiguration = TestStations.Offline;

                          stationConfiguration.Add("ocppServer", new JObject(
                                                                     new JProperty("enabled", true),
                                                                     new JProperty("port",    port),
                                                                     new JProperty("port16",  TestPorts.Free()),
                                                                     new JProperty("http3",   true)
                                                                 ));

                          var made = TestStations.New(Path.Combine(directory, "station" + attempt++),
                                                      stationConfiguration,
                                                      WithDisplay:  false,
                                                      Web:          arguments.Web);

                          made.Log.OnLogged += entry => otherSaid.Enqueue(entry.Message);

                          var refusal = new StringWriter();
                          Assert.That(made.ImportCertificates(arguments, out _, new StringWriter(), refusal), Is.Null, refusal.ToString());

                          return made;

                      });

            #endregion

        }

        [TearDown]
        public async Task StopBoth()
        {

            try { await station.DisposeAsync(); } catch { }
            try { await csms.   DisposeAsync(); } catch { }

            TestCSMSs.Remove(directory);

            ca.Dispose();

        }

        private String Log
            => $"{Environment.NewLine}--- the CSMS:{Environment.NewLine}{String.Join(Environment.NewLine, csmsSaid.TakeLast(60))}" +
               $"{Environment.NewLine}--- the station:{Environment.NewLine}{String.Join(Environment.NewLine, otherSaid.TakeLast(60))}";

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

        private Byte[] AnIdentity(String Subject)
        {

            using var key     = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request       = new CertificateRequest($"CN={Subject}", key, HashAlgorithmName.SHA256);

            using var issued  = ca.Sign(request.CreateSigningRequestPem(),
                                        DateTimeOffset.UtcNow.AddDays(-1),
                                        DateTimeOffset.UtcNow.AddYears(1),
                                        ClientAuthentication: true);

            using var withKey = issued.CopyWithPrivateKey(key);

            var collection    = new X509Certificate2Collection { withKey };

            // With what is above it, or alone: then whoever lets it in has
            // to know the intermediate already.
            if (ca.Intermediate is not null && Environment.GetEnvironmentVariable("INTEROP_LEAFONLY") != "1")
                collection.Add(ca.Intermediate);

            return collection.Export(X509ContentType.Pkcs12)!;

        }

        private static String KeyPinOf(Byte[] Identity)
        {
            var certificates = X509CertificateLoader.LoadPkcs12Collection(Identity, null);
            try
            {
                return KeyPin.FromCertificate(certificates.First(one => one.HasPrivateKey)).ToHex();
            }
            finally
            {
                foreach (var one in certificates)
                    one.Dispose();
            }
        }

        private String URL
            => $"wss://127.0.0.1:{port}/{CSMSName}";

        /// <summary>
        /// What the station writes down of the CSMS that dials it.
        /// </summary>
        private String TheStationExpects(String? KeyPins = null, Boolean ByCA = false, Boolean BoundOneTimePass = false)
        {

            String? login = null;

            if (BoundOneTimePass)
                Assert.That(station.Connections.TryAddAuthentication("What the CSMS signs in with", "totp", CSMSName, TheSharedSecret,
                                                                     out login, out var loginError,
                                                                     TLSChannelBinding: true),
                            Is.True, loginError);

            if (ByCA)
                Assert.That(station.Certificates.Import(Encoding.ASCII.GetBytes(ca.Certificate.ExportCertificatePem()),
                                                        CertificateKind.TLSRoot, null, "The operator's authority",
                                                        Environment.GetEnvironmentVariable("INTEROP_GROUP") == "1" ? [ ServerPins.MarkOf(station.ConnectionsCertificateUsage, "operator") ] : null,
                                                        out _, out var rootError),
                            Is.True, rootError);

            // The intermediate of the operator's authority, told to the
            // station: the CSMS shows its own certificate and nothing above it.
            if (ByCA && ca.Intermediate is not null && Environment.GetEnvironmentVariable("INTEROP_KEEPINTERMEDIATE") == "1")
                Assert.That(station.Certificates.Import(Encoding.ASCII.GetBytes(ca.Intermediate.ExportCertificatePem()),
                                                        CertificateKind.TLSIntermediate, null, "The operator's intermediate", null,
                                                        out _, out var intermediateError),
                            Is.True, intermediateError);

            Assert.That(station.Connections.TryAddConnection("The CSMS, dialling in", URL, "CSMS", true, login, null,
                                                             out var id, out var error,
                                                             ServerKeyPins:  KeyPins,
                                                             Pins:           ByCA ? new OCPPConnections.ServerPinsChange(Environment.GetEnvironmentVariable("INTEROP_GROUP") == "1"
                                                                                                                             ? new ServerPins([], [], RootGroup: "operator")
                                                                                                                             : new ServerPins([], [ ServerPins.KeyPinOf(ca.Certificate) ])) : null,
                                                             Inbound:        true),
                        Is.True, error);

            return id!;

        }

        /// <summary>
        /// What the real CSMS writes down of the station it dials: believed by
        /// the key of its certificate.
        /// </summary>
        private String TheCSMSDials(Byte[]? Identity = null, Boolean BoundOneTimePass = false, String? HTTPVersion = null)
        {

            Assert.That(csms.StationLogins.TrySetPassword(StationName, null, null, null, out _, out var error), Is.True, error);

            String? login = null;

            if (BoundOneTimePass)
                Assert.That(csms.Connections.TryAddAuthentication("What this CSMS signs in with at the station", "totp", CSMSName, TheSharedSecret,
                                                                  out login, out error,
                                                                  TLSChannelBinding: true),
                            Is.True, error);

            List<OCPPConnections.ConnectionIdentity>? identities = null;

            if (Identity is not null)
            {

                Assert.That(csms.Certificates.Import(Identity, CertificateKind.TLSIdentity, null, "Towards the station", null,
                                                     out var entry, out var identityError),
                            Is.True, identityError);

                identities = [ new OCPPConnections.ConnectionIdentity(entry!.Id) ];

            }

            Assert.That(csms.Connections.TryAddConnection("The station", URL, "ChargingStation", true, login, null,
                                                          out var id, out error,
                                                          ServerKeyPins:  stationKeyPin,
                                                          HTTPVersion:    HTTPVersion,
                                                          Identities:     identities,
                                                          RemoteNodeId:   StationName),
                        Is.True, error);

            return id!;

        }

        private async Task BothAreConnected_AndTheStationAnswers(String Dialled, String Inbound)
        {

            await Until(() => csms.ConnectionStates.TryGetValue(Dialled, out var state) && state.Status == Status.Connected,
                        "The CSMS being connected to the station");

            await Until(() => station.ConnectionStates.TryGetValue(Inbound, out var state) && state.Status == Status.Connected,
                        "The station taking the CSMS for connected");

            Assert.That(csms.IsConnected(StationName), Is.True);

            var installed = await csms.InstalledOn(StationName);

            TestContext.Out.WriteLine($"GetInstalledCertificateIds: {installed.Answer} {installed.Detail}");
            TestContext.Out.WriteLine(Log);

            Assert.That(installed.Asked, Is.True, $"{installed.Answer} {installed.Detail}{Log}");

        }

        [Test]
        public async Task WithAClientCertificateKnownByItsKey()
        {

            var identity = AnIdentity("whatever-it-is-called");
            var inbound  = TheStationExpects(KeyPins: KeyPinOf(identity));
            var dialled  = TheCSMSDials(identity);

            await BothAreConnected_AndTheStationAnswers(dialled, inbound);

        }

        [Test]
        public async Task WithAClientCertificateOfTheOperatorsCA_IssuedToItsName()
        {

            var identity = AnIdentity(CSMSName);
            var inbound  = TheStationExpects(ByCA: true);
            var dialled  = TheCSMSDials(identity);

            await BothAreConnected_AndTheStationAnswers(dialled, inbound);

        }

        [Test]
        public async Task WithAClientCertificateIssuedToAnotherName_ItIsTurnedAway()
        {

            var identity = AnIdentity("csms-02");
            var inbound  = TheStationExpects(ByCA: true);
            var dialled  = TheCSMSDials(identity);

            await Until(() => csms.ConnectionStates.TryGetValue(dialled, out var state) && state.Status != Status.Connected && state.Attempt is >= 2,
                        "The CSMS saying what went wrong");

            TestContext.Out.WriteLine(Log);

            Assert.That(station.ConnectionStates.TryGetValue(inbound, out var taken) && taken.Status == Status.Connected, Is.False, Log);

        }

        [Test]
        public async Task WithAOneTimePasswordBoundToItsTLSSession_OverHTTP3()
        {

            await Until(() => station.OCPPServerJSON().SelectTokens("$..http3Port").Any(token => token.Type == JTokenType.Integer && token.Value<Int32>() == port),
                        "The station taking WebSockets over HTTP/3", 15);

            var inbound = TheStationExpects(BoundOneTimePass: true);
            var dialled = TheCSMSDials(BoundOneTimePass: true, HTTPVersion: "3");

            await BothAreConnected_AndTheStationAnswers(dialled, inbound);

            Assert.That(String.Join("\n", otherSaid), Does.Contain("bound to its TLS session"), Log);

        }

    }

}
