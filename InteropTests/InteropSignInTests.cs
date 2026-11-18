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

using cloud.charging.open.protocols.WWCP;
using cloud.charging.open.protocols.WWCP.NetworkingNode;
using cloud.charging.open.protocols.WWCP.Node.Certificates;
using cloud.charging.open.protocols.WWCP.Node.Configuration;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

using cloud.charging.open.CSMS.Tests;
using cloud.charging.open.LocalController.Tests;
using cloud.charging.open.LocalController.OCPP;
using cloud.charging.open.protocols.OCPP.Node.Serving;

using OCPPConnections = cloud.charging.open.protocols.WWCP.Node.Connections;

using TheCSMS        = cloud.charging.open.CSMS.CSMS;
using TheController  = cloud.charging.open.LocalController.LocalController;
using Status         = cloud.charging.open.protocols.WWCP.Node.Connections.ConnectionStatus;

namespace cloud.charging.open.Interop
{

    /// <summary>
    /// The real CSMS dialling the real local controller over TLS, and signing
    /// in with a client certificate - known by its key, or by its CA - or with
    /// a one-time password bound to its TLS session, over HTTP/3.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class InteropSignInTests
    {

        private const String  CSMSName         = "csms-01";
        private const String  ControllerName   = "lc002";
        private const String  TheSharedSecret  = "a-shared-secret-long-enough-for-ocpp";
        private const String  RootGroup        = "operator";

        private String          directory            = "";
        private String          controllerDirectory  = "";
        private TestCA          ca                   = default!;
        private TestCA          clientCA             = default!;
        private TheCSMS         csms                 = default!;
        private TheController   controller           = default!;
        private UInt16          port;

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

        [SetUp]
        public async Task StartBoth()
        {

            directory = TestCSMSs.TemporaryDirectory("interop-signin");
            csmsSaid. Clear();
            otherSaid.Clear();

            ca = TestCA.Create($"Site CA {Guid.NewGuid()}", WithIntermediate: true);

            // The authority of the CSMS's client certificates: the site's, or
            // one the controller has nothing of but its root.
            clientCA = Environment.GetEnvironmentVariable("INTEROP_OTHERCA") == "1"
                           ? TestCA.Create($"Operator CA {Guid.NewGuid()}", WithIntermediate: true)
                           : ca;

            var noLegalTime = new JObject(new JProperty("groups", new JObject(new JProperty("legal", new JObject(new JProperty("enabled", false))))));

            #region The CSMS

            var configuration = new JObject(
                                    new JProperty("nts",  noLegalTime),
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

            Assert.That(csms.Certificates.Import(Encoding.ASCII.GetBytes(ca.Certificate.ExportCertificatePem()),
                                                 CertificateKind.TLSRoot, null, "The site's authority", null,
                                                 out _, out var rootError),
                        Is.True, rootError);

            #endregion

            #region The local controller, on a port with TLS

            controller = await TestPorts.StartedOnFreshPorts(() => {

                             if (controllerDirectory != "")
                                 TestControllers.Remove(controllerDirectory);

                             port                 = APortFreeForBoth();
                             controllerDirectory  = TestControllers.TemporaryDirectory("interop-signin-lc");
                             Directory.CreateDirectory(controllerDirectory);

                             using (var store = new ServerCertificateStore(Path.Combine(controllerDirectory, ServerCertificateStore.DefaultDirectoryName)))
                             {

                                 Assert.That(store.TryCreateKey("127.0.0.1", [ "127.0.0.1" ], null, out _, out var csr, out var keyError),
                                             Is.True, keyError);

                                 using var certificate = ca.Sign(csr!, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

                                 Assert.That(store.TryAddCertificate(ca.ChainPEM(certificate), [ "127.0.0.1" ], out _, out _, out var addError),
                                             Is.True, addError);

                             }

                             var made = TestControllers.New(
                                            controllerDirectory,
                                            new JObject(
                                                new JProperty("nts",        noLegalTime),
                                                new JProperty("ocpp",       new JObject(new JProperty("nodeId", ControllerName))),
                                                new JProperty("ocppServer", new JObject(
                                                    new JProperty("enabled",           true),
                                                    new JProperty("address",           "127.0.0.1"),
                                                    new JProperty("port",              port),
                                                    new JProperty("securityProfiles",  new JArray(2, 3)),
                                                    new JProperty("http3",             true),
                                                    new JProperty("reachableAs",       new JArray("127.0.0.1")),
                                                    new JProperty("subprotocols",      new JArray("ocpp2.1"))
                                                )),
                                                new JProperty("csms",       new JObject(new JProperty("enabled", true)))
                                            )
                                        );

                             made.Log.OnLogged += entry => otherSaid.Enqueue(entry.Message);

                             return made;

                         });

            #endregion

        }

        [TearDown]
        public async Task StopBoth()
        {

            try { await controller.DisposeAsync(); } catch { }
            try { await csms.      DisposeAsync(); } catch { }

            TestCSMSs.      Remove(directory);
            TestControllers.Remove(controllerDirectory);
            controllerDirectory = "";

            ca.Dispose();

        }

        private String Log
            => $"{Environment.NewLine}--- the CSMS:{Environment.NewLine}{String.Join(Environment.NewLine, csmsSaid.TakeLast(60))}" +
               $"{Environment.NewLine}--- the controller:{Environment.NewLine}{String.Join(Environment.NewLine, otherSaid.TakeLast(60))}";

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

            using var issued  = clientCA.Sign(request.CreateSigningRequestPem(),
                                        DateTimeOffset.UtcNow.AddDays(-1),
                                        DateTimeOffset.UtcNow.AddYears(1),
                                        ClientAuthentication: true);

            using var withKey = issued.CopyWithPrivateKey(key);

            var collection    = new X509Certificate2Collection { withKey };

            if (clientCA.Intermediate is not null && Environment.GetEnvironmentVariable("INTEROP_LEAFONLY") != "1")
                collection.Add(clientCA.Intermediate);

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
        /// What the controller writes down of the CSMS that dials it.
        /// </summary>
        private String TheControllerExpects(String? KeyPins = null, Boolean ByCA = false, Boolean BoundOneTimePass = false)
        {

            String? login = null;

            if (BoundOneTimePass)
                Assert.That(controller.Connections.TryAddAuthentication("What the CSMS signs in with", "totp", CSMSName, TheSharedSecret,
                                                                        out login, out var loginError,
                                                                        TLSChannelBinding: true),
                            Is.True, loginError);

            if (ByCA)
                Assert.That(controller.Certificates.Import(Encoding.ASCII.GetBytes(clientCA.Certificate.ExportCertificatePem()),
                                                           CertificateKind.TLSRoot, null, "The operator's authority",
                                                           [ ServerPins.MarkOf(controller.ConnectionsCertificateUsage, RootGroup) ],
                                                           out _, out var rootError),
                            Is.True, rootError);

            Assert.That(controller.Connections.TryAddConnection("The CSMS, dialling in", URL, "CSMS", true, login, null,
                                                                out var id, out var error,
                                                                ServerKeyPins:  KeyPins,
                                                                Pins:           ByCA ? new OCPPConnections.ServerPinsChange(new ServerPins([], [], RootGroup: RootGroup)) : null,
                                                                Inbound:        true),
                        Is.True, error);

            return id!;

        }

        /// <summary>
        /// What the real CSMS writes down of the controller it dials.
        /// </summary>
        private String TheCSMSDials(Byte[]? Identity = null, Boolean BoundOneTimePass = false, String? HTTPVersion = null)
        {

            Assert.That(csms.StationLogins.TrySetPassword(ControllerName, null, null, null, out _, out var error), Is.True, error);
            Assert.That(csms.StationLogins.TrySetLocalController(ControllerName, true, out error),                 Is.True, error);

            String? login = null;

            if (BoundOneTimePass)
                Assert.That(csms.Connections.TryAddAuthentication("What this CSMS signs in with at the controller", "totp", CSMSName, TheSharedSecret,
                                                                  out login, out error,
                                                                  TLSChannelBinding: true),
                            Is.True, error);

            List<OCPPConnections.ConnectionIdentity>? identities = null;

            if (Identity is not null)
            {

                Assert.That(csms.Certificates.Import(Identity, CertificateKind.TLSIdentity, null, "Towards the controller", null,
                                                     out var entry, out var identityError),
                            Is.True, identityError);

                identities = [ new OCPPConnections.ConnectionIdentity(entry!.Id) ];

            }

            Assert.That(csms.Connections.TryAddConnection("The controller", URL, "LocalController", true, login, null,
                                                          out var id, out error,
                                                          HTTPVersion:   HTTPVersion,
                                                          Identities:    identities,
                                                          RemoteNodeId:  ControllerName),
                        Is.True, error);

            return id!;

        }

        private async Task BothAreConnected_AndTheControllerAnswers(String Dialled, String Inbound)
        {

            await Until(() => csms.ConnectionStates.TryGetValue(Dialled, out var state) && state.Status == Status.Connected,
                        "The CSMS being connected to the controller");

            await Until(() => controller.ConnectionStates.TryGetValue(Inbound, out var state) && state.Status == Status.Connected,
                        "The controller taking the CSMS for connected");

            Assert.That(csms.InOverlayNetwork(ControllerName), Is.True);

            var answer = await csms.Node.OCPP.OUT.GetInstalledCertificateIds(
                                   new cloud.charging.open.protocols.OCPPv2_1.CSMS.GetInstalledCertificateIdsRequest(
                                       SourceRouting.To(NetworkingNode_Id.Parse(ControllerName)),
                                       NetworkPath: NetworkPath.From(csms.Node.Id)
                                   )
                               );

            TestContext.Out.WriteLine($"The controller itself answered: {answer.Status} {answer.Result}");
            TestContext.Out.WriteLine(Log);

            Assert.That(answer.Result.ResultCode.ToString(), Is.EqualTo("OK"), $"{answer.Result}{Log}");

        }

        [Test]
        public async Task WithAClientCertificateKnownByItsKey()
        {

            var identity = AnIdentity("whatever-it-is-called");
            var inbound  = TheControllerExpects(KeyPins: KeyPinOf(identity));
            var dialled  = TheCSMSDials(identity);

            await BothAreConnected_AndTheControllerAnswers(dialled, inbound);

            Assert.That(String.Join("\n", otherSaid), Does.Contain("security profile 3"), Log);

        }

        [Test]
        public async Task WithAClientCertificateOfTheOperatorsCA_IssuedToItsName()
        {

            var identity = AnIdentity(CSMSName);
            var inbound  = TheControllerExpects(ByCA: true);
            var dialled  = TheCSMSDials(identity);

            await BothAreConnected_AndTheControllerAnswers(dialled, inbound);

            Assert.That(String.Join("\n", otherSaid), Does.Contain("security profile 3"), Log);

        }

        [Test]
        public async Task WithAClientCertificateIssuedToAnotherName_ItIsTurnedAway()
        {

            var identity = AnIdentity("csms-02");
            var inbound  = TheControllerExpects(ByCA: true);
            var dialled  = TheCSMSDials(identity);

            await Until(() => csms.ConnectionStates.TryGetValue(dialled, out var state) && state.Status != Status.Connected && state.Attempt is >= 2,
                        "The CSMS saying what went wrong");

            TestContext.Out.WriteLine(Log);

            Assert.That(controller.ConnectionStates.TryGetValue(inbound, out var taken) && taken.Status == Status.Connected, Is.False, Log);

        }

        [Test]
        public async Task WithAOneTimePasswordBoundToItsTLSSession_OverHTTP3()
        {

            var server = controller.StationServer;

            SpinWait.SpinUntil(() => server?.HTTP3Port is not null || server?.WhyNoHTTP3 is not null, TimeSpan.FromSeconds(10));

            Assert.That(server?.HTTP3Port?.ToString(), Is.EqualTo(port.ToString()),
                        "The controller takes no WebSockets over HTTP/3: " + server?.WhyNoHTTP3);

            var inbound = TheControllerExpects(BoundOneTimePass: true);
            var dialled = TheCSMSDials(BoundOneTimePass: true, HTTPVersion: "3");

            await BothAreConnected_AndTheControllerAnswers(dialled, inbound);

            Assert.That(String.Join("\n", otherSaid), Does.Contain("a one-time password bound to its TLS session"), Log);

        }

        [Test]
        public async Task WithAOneTimePasswordBoundToItsTLSSession_OverHTTP1_ItIsTurnedAway()
        {

            var inbound = TheControllerExpects(BoundOneTimePass: true);
            var dialled = TheCSMSDials(BoundOneTimePass: true, HTTPVersion: "1.1");

            // The CSMS does not dial at all: it knows what it could not bind.
            await Until(() => csmsSaid.Any(line => line.Contains("Not dialled") && line.Contains("HTTP/3")),
                        "The CSMS saying why it does not dial");

            await Task.Delay(2000);

            TestContext.Out.WriteLine(Log);

            Assert.That(controller.ConnectionStates.TryGetValue(inbound, out var taken) && taken.Status == Status.Connected, Is.False, Log);

        }

    }

}
