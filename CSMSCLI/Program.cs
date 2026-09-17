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

using System.Diagnostics;
using System.Security.Cryptography;

using Newtonsoft.Json;

using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.Crypto.Operators;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.DNS;
using org.GraphDefined.Vanaheimr.Hermod.WebSocket;
using org.GraphDefined.Vanaheimr.Norn.NTS;

using cloud.charging.open.protocols.WWCP.NetworkingNode;

using OCPPv1_6 = cloud.charging.open.protocols.OCPPv1_6;
using OCPPv2_1 = cloud.charging.open.protocols.OCPPv2_1;

#endregion

namespace org.GraphDefined.OCPP.CSMS.TestApp
{

    /// <summary>
    /// An OCPP CSMS Test Application.
    /// </summary>
    public class Program
    {

        #region (class) CommandException

        public class CommandException(String Message) : Exception(Message)
        {

            #region (static) NotWithinOCPPv1_6

            public static CommandException NotWithinOCPPv1_6
                => new ("This command ist not available within OCPP v1.6!");

            #endregion

        }

        #endregion

        #region Data

        private const           String         debugLogFile      = "debug.log";
        private const           String         ocppVersion1_6    = "v1.6";
        private const           String         ocppVersion2_1    = "v2.1";

        private static readonly SemaphoreSlim  cliLock           = new (1, 1);
        private static readonly SemaphoreSlim  logfileLock1_6    = new (1, 1);
        private static readonly SemaphoreSlim  logfileLock2_6    = new (1, 1);

        private readonly static String         logfileNameV1_6   = Path.Combine(AppContext.BaseDirectory, "OCPPv1.6_Messages.log");
        private readonly static String         logfileNameV2_1   = Path.Combine(AppContext.BaseDirectory, "OCPPv2.1_Messages.log");

        #endregion


        private static async Task DebugLog(String             Message,
                                           CancellationToken  CancellationToken)
        {

            try
            {
                await cliLock.WaitAsync(CancellationToken);
                DebugX.Log(Message);
            }
            catch (Exception e)
            {
                //DebugX.LogException(e, $"{nameof(testCSMSv2_1)}.{nameof(testCSMSv2_1.OnNewTCPConnection)}");
                DebugX.LogException(e, $"{nameof(DebugLog)}");
            }
            finally
            {
                cliLock.Release();
            }

        }


        private static async Task Log(String             LogFileName,
                                      String             Message,
                                      SemaphoreSlim      LogFileLock,
                                      CancellationToken  CancellationToken)
        {

            var retry = 0;

            do
            {
                try
                {

                    retry++;

                    await LogFileLock.WaitAsync(CancellationToken);

                    await File.AppendAllTextAsync(
                             LogFileName,
                             Message + Environment.NewLine,
                             CancellationToken
                         );

                }
                catch (Exception e)
                {
                    DebugX.LogException(e, $"{nameof(WriteToLogfileV2_1)}");
                }
                finally
                {
                    LogFileLock.Release();
                }


            }
            while (retry > 3);

        }

        private static Task WriteToLogfileV1_6(String             Message,
                                               CancellationToken  CancellationToken)

            => Log(logfileNameV1_6,
                   Message,
                   logfileLock1_6,
                   CancellationToken);


        private static Task WriteToLogfileV2_1(String             Message,
                                               CancellationToken  CancellationToken)

            => Log(logfileNameV2_1,
                   Message,
                   logfileLock2_6,
                   CancellationToken);


        static System.Security.Cryptography.ECDsa ConvertFromPkcs8(byte[] pkcs8)
        {
            using (var ms     = new MemoryStream(pkcs8))
            using (var reader = new BinaryReader(ms))
            {
                var ecdsa = System.Security.Cryptography.ECDsa.Create();
                ecdsa.ImportPkcs8PrivateKey(reader.ReadBytes((int) ms.Length), out _);
                return ecdsa;
            }
        }

        #region ToDotNet(this Certificate, PrivateKey = null)

        static ECDsa ToDotNetECDsa(ECPrivateKeyParameters privateKeyParameters)
        {

            var domainParameters  = privateKeyParameters.Parameters;
            var curveParams       = domainParameters.Curve;
            var q                 = domainParameters.G.Multiply(privateKeyParameters.D).Normalize();

            var ecdsa             = ECDsa.Create(new ECParameters() {
                                        Curve = ECCurve.CreateFromOid(new Oid(curveParams.ToString())),
                                        D     = privateKeyParameters.D.ToByteArrayUnsigned(),
                                        Q     = new ECPoint {
                                                    X = q.AffineXCoord.GetEncoded(),
                                                    Y = q.AffineYCoord.GetEncoded()
                                                }
                                    });

            return ecdsa;

        }


        /// <summary>
        /// Convert the Bouncy Castle certificate to a .NET certificate.
        /// </summary>
        /// <param name="Certificate">A Bouncy Castle certificate.</param>
        /// <param name="PrivateKey">An optional private key to be included.</param>
        static System.Security.Cryptography.X509Certificates.X509Certificate2? ToDotNet(X509Certificate                Certificate,
                                                                                        AsymmetricKeyParameter?        PrivateKey       = null,
                                                                                        IEnumerable<X509Certificate>?  CACertificates   = null)
        {

            if (PrivateKey is null)
                return new (Certificate.GetEncoded());

            if (PrivateKey is RsaPrivateCrtKeyParameters rsaPrivateKey)
            {

                var store             = new Pkcs12StoreBuilder().Build();
                var certificateEntry  = new X509CertificateEntry(Certificate);

                store.SetCertificateEntry(Certificate.SubjectDN.ToString(),
                                          certificateEntry);

                store.SetKeyEntry        (Certificate.SubjectDN.ToString(),
                                          new AsymmetricKeyEntry(rsaPrivateKey),
                                          [ certificateEntry ]);

                foreach (var caCertificate in (CACertificates ?? []))
                {
                    store.SetCertificateEntry(caCertificate.SubjectDN.ToString(),
                                              new X509CertificateEntry(caCertificate));
                }

                using (var pfxStream = new MemoryStream())
                {

                    var password = RandomExtensions.RandomString(10);

                    store.Save(pfxStream,
                               password.ToCharArray(),
                               new SecureRandom());

                    return new System.Security.Cryptography.X509Certificates.X509Certificate2(
                               pfxStream.ToArray(),
                               password,
                               System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.Exportable
                           );

                }

            }

            if (PrivateKey is ECPrivateKeyParameters eccPrivateKey)
            {

                //var dotNetCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(Certificate.GetEncoded());
                //var ecdsa             = ToDotNetECDsa(eccPrivateKey);

                //return dotNetCertificate.CopyWithPrivateKey(ecdsa);

            }

            return null;

        }

        #endregion


        /// <summary>
        /// Start the OCPP CSMS Test Application.
        /// </summary>
        /// <param name="Arguments">Command line arguments</param>
        public static async Task Main(String[] Arguments)
        {

            #region Data

            var dnsClient = new DNSClient(SearchForIPv6DNSServers: false);

            #endregion

            #region Debug to Console/file

            var debugFile    = new TextWriterTraceListener(debugLogFile);

            var debugTargets = new[] {
                debugFile,
                new TextWriterTraceListener(Console.Out)
            };

            Trace.Listeners.AddRange(debugTargets);

            #endregion


            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "HTTPSSEs"));

            #region Setup PKI

            #region Data

            AsymmetricCipherKeyPair? rootCA_ECC_KeyPair           = null;
            X509Certificate?         rootCA_ECC_Certificate       = null;
            AsymmetricCipherKeyPair? rootCA_RSA_KeyPair           = null;
            X509Certificate?         rootCA_RSA_Certificate       = null;

            AsymmetricCipherKeyPair? serverCA_ECC_KeyPair         = null;
            X509Certificate?         serverCA_ECC_Certificate     = null;
            AsymmetricCipherKeyPair? serverCA_RSA_KeyPair         = null;
            X509Certificate?         serverCA_RSA_Certificate     = null;

            AsymmetricCipherKeyPair? clientCA_ECC_KeyPair         = null;
            X509Certificate?         clientCA_ECC_Certificate     = null;
            AsymmetricCipherKeyPair? clientCA_RSA_KeyPair         = null;
            X509Certificate?         clientCA_RSA_Certificate     = null;

            AsymmetricCipherKeyPair? firmwareCA_ECC_KeyPair       = null;
            X509Certificate?         firmwareCA_ECC_Certificate   = null;
            AsymmetricCipherKeyPair? firmwareCA_RSA_KeyPair       = null;
            X509Certificate?         firmwareCA_RSA_Certificate   = null;


            AsymmetricCipherKeyPair? server1_ECC_KeyPair          = null;
            X509Certificate?         server1_ECC_Certificate      = null;
            AsymmetricCipherKeyPair? server1_RSA_KeyPair          = null;
            X509Certificate?         server1_RSA_Certificate      = null;


            AsymmetricCipherKeyPair? client1_ECC_KeyPair          = null;
            X509Certificate?         client1_ECC_Certificate      = null;
            AsymmetricCipherKeyPair? client1_RSA_KeyPair          = null;
            X509Certificate?         client1_RSA_Certificate      = null;

            #endregion

            #region Crypto defaults

            var secureRandom                    = new SecureRandom();
            var eccSignatureAlgorithm           = "SHA256withECDSA";
            var rsaSignatureAlgorithm           = "SHA256WithRSA";

            var eccCurve                        = ECNamedCurveTable.GetByName("secp256r1");
            var eccDomainParameters             = new ECDomainParameters(eccCurve.Curve, eccCurve.G, eccCurve.N, eccCurve.H, eccCurve.GetSeed());
            var eccKeyGenParams                 = new ECKeyGenerationParameters(eccDomainParameters, secureRandom);

            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "pki"));
            var rootCA_ECC_privateKeyFile       = Path.Combine(AppContext.BaseDirectory, "pki", "rootCA_ECC.key");
            var rootCA_ECC_certificateFile      = Path.Combine(AppContext.BaseDirectory, "pki", "rootCA_ECC.cert");
            var rootCA_RSA_privateKeyFile       = Path.Combine(AppContext.BaseDirectory, "pki", "rootCA_RSA.key");
            var rootCA_RSA_certificateFile      = Path.Combine(AppContext.BaseDirectory, "pki", "rootCA_RSA.cert");

            var serverCA_ECC_privateKeyFile     = Path.Combine(AppContext.BaseDirectory, "pki", "serverCA_ECC.key");
            var serverCA_ECC_certificateFile    = Path.Combine(AppContext.BaseDirectory, "pki", "serverCA_ECC.cert");
            var serverCA_RSA_privateKeyFile     = Path.Combine(AppContext.BaseDirectory, "pki", "serverCA_RSA.key");
            var serverCA_RSA_certificateFile    = Path.Combine(AppContext.BaseDirectory, "pki", "serverCA_RSA.cert");

            var clientCA_ECC_privateKeyFile     = Path.Combine(AppContext.BaseDirectory, "pki", "clientCA_ECC.key");
            var clientCA_ECC_certificateFile    = Path.Combine(AppContext.BaseDirectory, "pki", "clientCA_ECC.cert");
            var clientCA_RSA_privateKeyFile     = Path.Combine(AppContext.BaseDirectory, "pki", "clientCA_RSA.key");
            var clientCA_RSA_certificateFile    = Path.Combine(AppContext.BaseDirectory, "pki", "clientCA_RSA.cert");

            var firmwareCA_ECC_privateKeyFile   = Path.Combine(AppContext.BaseDirectory, "pki", "firmwareCA_ECC.key");
            var firmwareCA_ECC_certificateFile  = Path.Combine(AppContext.BaseDirectory, "pki", "firmwareCA_ECC.cert");
            var firmwareCA_RSA_privateKeyFile   = Path.Combine(AppContext.BaseDirectory, "pki", "firmwareCA_RSA.key");
            var firmwareCA_RSA_certificateFile  = Path.Combine(AppContext.BaseDirectory, "pki", "firmwareCA_RSA.cert");

            // ----------------------------------------------------------------------------------------------------

            var server1_ECC_privateKeyFile      = Path.Combine(AppContext.BaseDirectory, "pki", "server1_ECC.key");
            var server1_ECC_certificateFile     = Path.Combine(AppContext.BaseDirectory, "pki", "server1_ECC.cert");
            var server1_ECC_pfx                 = Path.Combine(AppContext.BaseDirectory, "pki", "server1_ECC.pfx");

            var server1_RSA_privateKeyFile      = Path.Combine(AppContext.BaseDirectory, "pki", "server1_RSA.key");
            var server1_RSA_certificateFile     = Path.Combine(AppContext.BaseDirectory, "pki", "server1_RSA.cert");
            var server1_RSA_pfx                 = Path.Combine(AppContext.BaseDirectory, "pki", "server1_RSA.pfx");


            var client1_ECC_privateKeyFile      = Path.Combine(AppContext.BaseDirectory, "pki", "client1_ECC.key");
            var client1_ECC_certificateFile     = Path.Combine(AppContext.BaseDirectory, "pki", "client1_ECC.cert");

            var client1_RSA_privateKeyFile      = Path.Combine(AppContext.BaseDirectory, "pki", "client1_RSA.key");
            var client1_RSA_certificateFile     = Path.Combine(AppContext.BaseDirectory, "pki", "client1_RSA.cert");

            #endregion

            #region Try to reload crypto data from disc

            try
            {

                // Root CA
                using (var reader = File.OpenText(rootCA_ECC_privateKeyFile))
                {
                    rootCA_ECC_KeyPair          = (AsymmetricCipherKeyPair)   new PemReader(reader).ReadObject();
                }

                using (var reader = File.OpenText(rootCA_ECC_certificateFile))
                {
                    rootCA_ECC_Certificate      = (X509Certificate)           new PemReader(reader).ReadObject();
                }

                using (var reader = File.OpenText(rootCA_RSA_privateKeyFile))
                {
                    rootCA_RSA_KeyPair          = (AsymmetricCipherKeyPair)   new PemReader(reader).ReadObject();
                }

                using (var reader = File.OpenText(rootCA_RSA_certificateFile))
                {
                    rootCA_RSA_Certificate      = (X509Certificate)           new PemReader(reader).ReadObject();
                }


                // Server CA
                using (var reader = File.OpenText(serverCA_ECC_privateKeyFile))
                {
                    serverCA_ECC_KeyPair        = (AsymmetricCipherKeyPair) new PemReader(reader).ReadObject();
                }

                using (var reader = File.OpenText(serverCA_ECC_certificateFile))
                {
                    serverCA_ECC_Certificate    = (X509Certificate)         new PemReader(reader).ReadObject();
                }

                using (var reader = File.OpenText(serverCA_RSA_privateKeyFile))
                {
                    serverCA_RSA_KeyPair        = (AsymmetricCipherKeyPair) new PemReader(reader).ReadObject();
                }

                using (var reader = File.OpenText(serverCA_RSA_certificateFile))
                {
                    serverCA_RSA_Certificate    = (X509Certificate)         new PemReader(reader).ReadObject();
                }


                // Client CA
                using (var reader = File.OpenText(clientCA_ECC_privateKeyFile))
                {
                    clientCA_ECC_KeyPair        = (AsymmetricCipherKeyPair) new PemReader(reader).ReadObject();
                }

                using (var reader = File.OpenText(clientCA_ECC_certificateFile))
                {
                    clientCA_ECC_Certificate    = (X509Certificate)         new PemReader(reader).ReadObject();
                }

                using (var reader = File.OpenText(clientCA_RSA_privateKeyFile))
                {
                    clientCA_RSA_KeyPair        = (AsymmetricCipherKeyPair) new PemReader(reader).ReadObject();
                }

                using (var reader = File.OpenText(clientCA_RSA_certificateFile))
                {
                    clientCA_RSA_Certificate    = (X509Certificate)         new PemReader(reader).ReadObject();
                }


                // Firmware CA
                using (var reader = File.OpenText(firmwareCA_ECC_privateKeyFile))
                {
                    firmwareCA_ECC_KeyPair      = (AsymmetricCipherKeyPair) new PemReader(reader).ReadObject();
                }

                using (var reader = File.OpenText(firmwareCA_ECC_certificateFile))
                {
                    firmwareCA_ECC_Certificate  = (X509Certificate)         new PemReader(reader).ReadObject();
                }

                using (var reader = File.OpenText(firmwareCA_RSA_privateKeyFile))
                {
                    firmwareCA_RSA_KeyPair      = (AsymmetricCipherKeyPair) new PemReader(reader).ReadObject();
                }

                using (var reader = File.OpenText(firmwareCA_RSA_certificateFile))
                {
                    firmwareCA_RSA_Certificate  = (X509Certificate)         new PemReader(reader).ReadObject();
                }


                // Server #1
                using (var reader = File.OpenText(server1_ECC_privateKeyFile))
                {
                    server1_ECC_KeyPair         = (AsymmetricCipherKeyPair) new PemReader(reader).ReadObject();
                }

                using (var reader = File.OpenText(server1_ECC_certificateFile))
                {
                    server1_ECC_Certificate     = (X509Certificate)         new PemReader(reader).ReadObject();
                }

                using (var reader = File.OpenText(server1_RSA_privateKeyFile))
                {
                    server1_RSA_KeyPair         = (AsymmetricCipherKeyPair) new PemReader(reader).ReadObject();
                }

                using (var reader = File.OpenText(server1_RSA_certificateFile))
                {
                    server1_RSA_Certificate     = (X509Certificate)         new PemReader(reader).ReadObject();
                }


                // Client #1
                using (var reader = File.OpenText(client1_ECC_privateKeyFile))
                {
                    client1_ECC_KeyPair         = (AsymmetricCipherKeyPair) new PemReader(reader).ReadObject();
                }

                using (var reader = File.OpenText(client1_ECC_certificateFile))
                {
                    client1_ECC_Certificate     = (X509Certificate)         new PemReader(reader).ReadObject();
                }

                using (var reader = File.OpenText(client1_RSA_privateKeyFile))
                {
                    client1_RSA_KeyPair         = (AsymmetricCipherKeyPair) new PemReader(reader).ReadObject();
                }

                using (var reader = File.OpenText(client1_RSA_certificateFile))
                {
                    client1_RSA_Certificate     = (X509Certificate)         new PemReader(reader).ReadObject();
                }

            }
            catch
            { }

            #endregion


            if (rootCA_ECC_KeyPair         is null)
            {

                var keyPairGenerator = new ECKeyPairGenerator();
                keyPairGenerator.Init(eccKeyGenParams);
                rootCA_ECC_KeyPair = keyPairGenerator.GenerateKeyPair();

                using (var writer = new StreamWriter(rootCA_ECC_privateKeyFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(rootCA_ECC_KeyPair.Private);
                    pemWriter.Writer.Flush();
                }

            }

            if (rootCA_ECC_Certificate     is null)
            {

                var certificateGenerator  = new X509V3CertificateGenerator();
                var subjectDN             = new X509Name("CN=Open Charging Cloud - Root CA (ECC), O=GraphDefined GmbH, OU=TestCA, L=Jena, C=Germany");

                certificateGenerator.SetIssuerDN     (subjectDN); // self-signed
                certificateGenerator.SetSubjectDN    (subjectDN);
                certificateGenerator.SetNotBefore    (Timestamp.Now.AddDays (-3).DateTime);
                certificateGenerator.SetNotAfter     (Timestamp.Now.AddYears(23).DateTime);
                certificateGenerator.SetPublicKey    (rootCA_ECC_KeyPair.Public);
                certificateGenerator.SetSerialNumber (BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), secureRandom));

                certificateGenerator.AddExtension    (X509Extensions.BasicConstraints,
                                                      critical: true,
                                                      new BasicConstraints(cA: true));

                certificateGenerator.AddExtension    (X509Extensions.KeyUsage,
                                                      critical: true,
                                                      new KeyUsage(
                                                          KeyUsage.DigitalSignature |
                                                          KeyUsage.KeyCertSign |
                                                          KeyUsage.CrlSign
                                                      ));

                rootCA_ECC_Certificate = certificateGenerator.Generate(new Asn1SignatureFactory(eccSignatureAlgorithm, rootCA_ECC_KeyPair.Private, secureRandom));

                using (var writer = new StreamWriter(rootCA_ECC_certificateFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(rootCA_ECC_Certificate);
                    pemWriter.Writer.Flush();
                }

            }

            if (rootCA_RSA_KeyPair         is null)
            {

                var rsaKeyPairGenerator = new RsaKeyPairGenerator();
                rsaKeyPairGenerator.Init(new KeyGenerationParameters(secureRandom, 4096));
                rootCA_RSA_KeyPair = rsaKeyPairGenerator.GenerateKeyPair();

                using (var writer = new StreamWriter(rootCA_RSA_privateKeyFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(rootCA_RSA_KeyPair.Private);
                    pemWriter.Writer.Flush();
                }

            }

            if (rootCA_RSA_Certificate     is null)
            {

                var certificateGenerator  = new X509V3CertificateGenerator();
                var subjectDN             = new X509Name("CN=Open Charging Cloud - Root CA (RSA), O=GraphDefined GmbH, OU=TestCA, L=Jena, C=Germany");

                certificateGenerator.SetIssuerDN     (subjectDN); // self-signed
                certificateGenerator.SetSubjectDN    (subjectDN);
                certificateGenerator.SetNotBefore    (Timestamp.Now.AddDays (-3).DateTime);
                certificateGenerator.SetNotAfter     (Timestamp.Now.AddYears(23).DateTime);
                certificateGenerator.SetPublicKey    (rootCA_RSA_KeyPair.Public);
                certificateGenerator.SetSerialNumber (BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), secureRandom));

                certificateGenerator.AddExtension    (X509Extensions.BasicConstraints,
                                                      critical: true,
                                                      new BasicConstraints(cA: true));

                certificateGenerator.AddExtension    (X509Extensions.KeyUsage,
                                                      critical: true,
                                                      new KeyUsage(
                                                          KeyUsage.DigitalSignature |
                                                          KeyUsage.KeyCertSign |
                                                          KeyUsage.CrlSign
                                                      ));

                rootCA_RSA_Certificate = certificateGenerator.Generate(new Asn1SignatureFactory(rsaSignatureAlgorithm, rootCA_RSA_KeyPair.Private, secureRandom));

                using (var writer = new StreamWriter(rootCA_RSA_certificateFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(rootCA_RSA_Certificate);
                    pemWriter.Writer.Flush();
                }

            }


            if (serverCA_ECC_KeyPair       is null)
            {

                var keyPairGenerator = new ECKeyPairGenerator();
                keyPairGenerator.Init(eccKeyGenParams);
                serverCA_ECC_KeyPair = keyPairGenerator.GenerateKeyPair();

                using (var writer = new StreamWriter(serverCA_ECC_privateKeyFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(serverCA_ECC_KeyPair.Private);
                    pemWriter.Writer.Flush();
                }

            }

            if (serverCA_ECC_Certificate   is null)
            {

                var certificateGenerator  = new X509V3CertificateGenerator();

                certificateGenerator.SetIssuerDN     (new X509Name(rootCA_ECC_Certificate.SubjectDN.ToString()));
                certificateGenerator.SetSubjectDN    (new X509Name("CN=Open Charging Cloud - Server CA (ECC), O=GraphDefined GmbH, OU=TestCA, L=Jena, C=Germany"));
                certificateGenerator.SetNotBefore    (Timestamp.Now.AddDays (-2).DateTime);
                certificateGenerator.SetNotAfter     (Timestamp.Now.AddYears(+5).DateTime);
                certificateGenerator.SetPublicKey    (serverCA_ECC_KeyPair.Public);
                certificateGenerator.SetSerialNumber (BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), secureRandom));

                certificateGenerator.AddExtension    (X509Extensions.BasicConstraints,
                                                      critical: true,
                                                      // A CA certificate, but it cannot be used to sign other CA certificates,
                                                      // only end-entity certificates.
                                                      new BasicConstraints(0));

                certificateGenerator.AddExtension    (X509Extensions.KeyUsage,
                                                      critical: true,
                                                      new KeyUsage(
                                                          KeyUsage.DigitalSignature |
                                                          KeyUsage.KeyCertSign |
                                                          KeyUsage.CrlSign
                                                      ));

                serverCA_ECC_Certificate = certificateGenerator.Generate(new Asn1SignatureFactory(eccSignatureAlgorithm, rootCA_ECC_KeyPair.Private, secureRandom));

                using (var writer = new StreamWriter(serverCA_ECC_certificateFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(serverCA_ECC_Certificate);
                    pemWriter.Writer.Flush();
                }

            }

            if (serverCA_RSA_KeyPair       is null)
            {

                var rsaKeyPairGenerator = new RsaKeyPairGenerator();
                rsaKeyPairGenerator.Init(new KeyGenerationParameters(secureRandom, 4096));
                serverCA_RSA_KeyPair = rsaKeyPairGenerator.GenerateKeyPair();

                using (var writer = new StreamWriter(serverCA_RSA_privateKeyFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(serverCA_RSA_KeyPair.Private);
                    pemWriter.Writer.Flush();
                }

            }

            if (serverCA_RSA_Certificate   is null)
            {

                var certificateGenerator  = new X509V3CertificateGenerator();

                certificateGenerator.SetIssuerDN     (new X509Name(rootCA_RSA_Certificate.SubjectDN.ToString()));
                certificateGenerator.SetSubjectDN    (new X509Name("CN=Open Charging Cloud - Server CA (RSA), O=GraphDefined GmbH, OU=TestCA, L=Jena, C=Germany"));
                certificateGenerator.SetNotBefore    (Timestamp.Now.AddDays (-3).DateTime);
                certificateGenerator.SetNotAfter     (Timestamp.Now.AddYears(23).DateTime);
                certificateGenerator.SetPublicKey    (serverCA_RSA_KeyPair.Public);
                certificateGenerator.SetSerialNumber (BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), secureRandom));

                certificateGenerator.AddExtension    (X509Extensions.BasicConstraints,
                                                      critical: true,
                                                      // A CA certificate, but it cannot be used to sign other CA certificates,
                                                      // only end-entity certificates.
                                                      new BasicConstraints(0));

                certificateGenerator.AddExtension    (X509Extensions.KeyUsage,
                                                      critical: true,
                                                      new KeyUsage(
                                                          KeyUsage.DigitalSignature |
                                                          KeyUsage.KeyCertSign |
                                                          KeyUsage.CrlSign
                                                      ));

                serverCA_RSA_Certificate = certificateGenerator.Generate(new Asn1SignatureFactory(rsaSignatureAlgorithm, rootCA_RSA_KeyPair.Private, secureRandom));

                using (var writer = new StreamWriter(serverCA_RSA_certificateFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(serverCA_RSA_Certificate);
                    pemWriter.Writer.Flush();
                }

            }


            if (clientCA_ECC_KeyPair       is null)
            {

                var keyPairGenerator = new ECKeyPairGenerator();
                keyPairGenerator.Init(eccKeyGenParams);
                clientCA_ECC_KeyPair = keyPairGenerator.GenerateKeyPair();

                using (var writer = new StreamWriter(clientCA_ECC_privateKeyFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(clientCA_ECC_KeyPair.Private);
                    pemWriter.Writer.Flush();
                }

            }

            if (clientCA_ECC_Certificate   is null)
            {

                var certificateGenerator  = new X509V3CertificateGenerator();

                certificateGenerator.SetIssuerDN     (new X509Name(rootCA_ECC_Certificate.SubjectDN.ToString()));
                certificateGenerator.SetSubjectDN    (new X509Name("CN=Open Charging Cloud - Client CA (ECC), O=GraphDefined GmbH, OU=TestCA, L=Jena, C=Germany"));
                certificateGenerator.SetNotBefore    (Timestamp.Now.AddDays (-2).DateTime);
                certificateGenerator.SetNotAfter     (Timestamp.Now.AddYears(+5).DateTime);
                certificateGenerator.SetPublicKey    (clientCA_ECC_KeyPair.Public);
                certificateGenerator.SetSerialNumber (BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), secureRandom));

                certificateGenerator.AddExtension    (X509Extensions.BasicConstraints,
                                                      critical: true,
                                                      // A CA certificate, but it cannot be used to sign other CA certificates,
                                                      // only end-entity certificates.
                                                      new BasicConstraints(0));

                certificateGenerator.AddExtension    (X509Extensions.KeyUsage,
                                                      critical: true,
                                                      new KeyUsage(
                                                          KeyUsage.DigitalSignature |
                                                          KeyUsage.KeyCertSign |
                                                          KeyUsage.CrlSign
                                                      ));

                clientCA_ECC_Certificate = certificateGenerator.Generate(new Asn1SignatureFactory(eccSignatureAlgorithm, rootCA_ECC_KeyPair.Private, secureRandom));

                using (var writer = new StreamWriter(clientCA_ECC_certificateFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(clientCA_ECC_Certificate);
                    pemWriter.Writer.Flush();
                }

            }

            if (clientCA_RSA_KeyPair       is null)
            {

                var rsaKeyPairGenerator = new RsaKeyPairGenerator();
                rsaKeyPairGenerator.Init(new KeyGenerationParameters(secureRandom, 4096));
                clientCA_RSA_KeyPair = rsaKeyPairGenerator.GenerateKeyPair();

                using (var writer = new StreamWriter(clientCA_RSA_privateKeyFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(clientCA_RSA_KeyPair.Private);
                    pemWriter.Writer.Flush();
                }

            }

            if (clientCA_RSA_Certificate   is null)
            {

                var certificateGenerator  = new X509V3CertificateGenerator();

                certificateGenerator.SetIssuerDN     (new X509Name(rootCA_ECC_Certificate.SubjectDN.ToString()));
                certificateGenerator.SetSubjectDN    (new X509Name("CN=Open Charging Cloud - Client CA (RSA), O=GraphDefined GmbH, OU=TestCA, L=Jena, C=Germany"));
                certificateGenerator.SetNotBefore    (Timestamp.Now.AddDays (-3).DateTime);
                certificateGenerator.SetNotAfter     (Timestamp.Now.AddYears(23).DateTime);
                certificateGenerator.SetPublicKey    (clientCA_RSA_KeyPair.Public);
                certificateGenerator.SetSerialNumber (BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), secureRandom));

                certificateGenerator.AddExtension    (X509Extensions.BasicConstraints,
                                                      critical: true,
                                                      // A CA certificate, but it cannot be used to sign other CA certificates,
                                                      // only end-entity certificates.
                                                      new BasicConstraints(0));

                certificateGenerator.AddExtension    (X509Extensions.KeyUsage,
                                                      critical: true,
                                                      new KeyUsage(
                                                          KeyUsage.DigitalSignature |
                                                          KeyUsage.KeyCertSign |
                                                          KeyUsage.CrlSign
                                                      ));

                clientCA_RSA_Certificate = certificateGenerator.Generate(new Asn1SignatureFactory(rsaSignatureAlgorithm, rootCA_RSA_KeyPair.Private, secureRandom));

                using (var writer = new StreamWriter(clientCA_RSA_certificateFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(clientCA_RSA_Certificate);
                    pemWriter.Writer.Flush();
                }

            }


            if (firmwareCA_ECC_KeyPair     is null)
            {

                var keyPairGenerator = new ECKeyPairGenerator();
                keyPairGenerator.Init(eccKeyGenParams);
                firmwareCA_ECC_KeyPair = keyPairGenerator.GenerateKeyPair();

                using (var writer = new StreamWriter(firmwareCA_ECC_privateKeyFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(firmwareCA_ECC_KeyPair.Private);
                    pemWriter.Writer.Flush();
                }

            }

            if (firmwareCA_ECC_Certificate is null)
            {

                var certificateGenerator  = new X509V3CertificateGenerator();

                certificateGenerator.SetIssuerDN     (new X509Name(rootCA_ECC_Certificate.SubjectDN.ToString()));
                certificateGenerator.SetSubjectDN    (new X509Name("CN=Open Charging Cloud - Firmware Signing CA (ECC), O=GraphDefined GmbH, OU=TestCA, L=Jena, C=Germany"));
                certificateGenerator.SetNotBefore    (Timestamp.Now.AddDays (-2).DateTime);
                certificateGenerator.SetNotAfter     (Timestamp.Now.AddYears(+5).DateTime);
                certificateGenerator.SetPublicKey    (firmwareCA_ECC_KeyPair.Public);
                certificateGenerator.SetSerialNumber (BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), secureRandom));

                certificateGenerator.AddExtension    (X509Extensions.BasicConstraints,
                                                      critical: true,
                                                      // A CA certificate, but it cannot be used to sign other CA certificates,
                                                      // only end-entity certificates.
                                                      new BasicConstraints(0));

                certificateGenerator.AddExtension    (X509Extensions.KeyUsage,
                                                      critical: true,
                                                      new KeyUsage(
                                                          KeyUsage.DigitalSignature |
                                                          KeyUsage.KeyCertSign |
                                                          KeyUsage.CrlSign
                                                      ));

                firmwareCA_ECC_Certificate = certificateGenerator.Generate(new Asn1SignatureFactory(eccSignatureAlgorithm, rootCA_ECC_KeyPair.Private, secureRandom));

                using (var writer = new StreamWriter(firmwareCA_ECC_certificateFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(firmwareCA_ECC_Certificate);
                    pemWriter.Writer.Flush();
                }

            }

            if (firmwareCA_RSA_KeyPair     is null)
            {

                var rsaKeyPairGenerator = new RsaKeyPairGenerator();
                rsaKeyPairGenerator.Init(new KeyGenerationParameters(secureRandom, 4096));
                firmwareCA_RSA_KeyPair = rsaKeyPairGenerator.GenerateKeyPair();

                using (var writer = new StreamWriter(firmwareCA_RSA_privateKeyFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(firmwareCA_RSA_KeyPair.Private);
                    pemWriter.Writer.Flush();
                }

            }

            if (firmwareCA_RSA_Certificate is null)
            {

                var certificateGenerator  = new X509V3CertificateGenerator();

                certificateGenerator.SetIssuerDN     (new X509Name(rootCA_ECC_Certificate.SubjectDN.ToString()));
                certificateGenerator.SetSubjectDN    (new X509Name("CN=Open Charging Cloud - Firmware Signing CA (RSA), O=GraphDefined GmbH, OU=TestCA, L=Jena, C=Germany"));
                certificateGenerator.SetNotBefore    (Timestamp.Now.AddDays (-3).DateTime);
                certificateGenerator.SetNotAfter     (Timestamp.Now.AddYears(23).DateTime);
                certificateGenerator.SetPublicKey    (firmwareCA_RSA_KeyPair.Public);
                certificateGenerator.SetSerialNumber (BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), secureRandom));

                certificateGenerator.AddExtension    (X509Extensions.BasicConstraints,
                                                      critical: true,
                                                      // A CA certificate, but it cannot be used to sign other CA certificates,
                                                      // only end-entity certificates.
                                                      new BasicConstraints(0));

                certificateGenerator.AddExtension    (X509Extensions.KeyUsage,
                                                      critical: true,
                                                      new KeyUsage(
                                                          KeyUsage.DigitalSignature |
                                                          KeyUsage.KeyCertSign |
                                                          KeyUsage.CrlSign
                                                      ));

                firmwareCA_RSA_Certificate = certificateGenerator.Generate(new Asn1SignatureFactory(rsaSignatureAlgorithm, rootCA_RSA_KeyPair.Private, secureRandom));

                using (var writer = new StreamWriter(firmwareCA_RSA_certificateFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(firmwareCA_RSA_Certificate);
                    pemWriter.Writer.Flush();
                }

            }


            // -------------------------------------------


            if (server1_ECC_KeyPair        is null)
            {

                var keyPairGenerator = new ECKeyPairGenerator();
                keyPairGenerator.Init(eccKeyGenParams);
                server1_ECC_KeyPair = keyPairGenerator.GenerateKeyPair();

                using (var writer = new StreamWriter(server1_ECC_privateKeyFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(server1_ECC_KeyPair.Private);
                    pemWriter.Writer.Flush();
                }

            }

            if (server1_ECC_Certificate    is null)
            {

                var certificateGenerator  = new X509V3CertificateGenerator();

                certificateGenerator.SetIssuerDN     (new X509Name(serverCA_ECC_Certificate.SubjectDN.ToString()));
                certificateGenerator.SetSubjectDN    (new X509Name("CN=api1.charging.cloud, O=GraphDefined GmbH, OU=ECC, L=Jena, C=Germany"));
                certificateGenerator.SetNotBefore    (Timestamp.Now.AddDays  (-1).DateTime);
                certificateGenerator.SetNotAfter     (Timestamp.Now.AddMonths(+3).DateTime);
                certificateGenerator.SetPublicKey    (server1_ECC_KeyPair.Public);
                certificateGenerator.SetSerialNumber (BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), secureRandom));

                certificateGenerator.AddExtension    (X509Extensions.KeyUsage,         critical: true, new KeyUsage        (KeyUsage.DigitalSignature | KeyUsage.KeyEncipherment));
                certificateGenerator.AddExtension    (X509Extensions.ExtendedKeyUsage, critical: true, new ExtendedKeyUsage(KeyPurposeID.id_kp_serverAuth));

                certificateGenerator.AddExtension    (X509Extensions.SubjectAlternativeName, critical: false, new GeneralNames([
                                                                                                                                   new (GeneralName.DnsName,   "api1.charging.cloud"),
                                                                                                                                   new (GeneralName.IPAddress, "127.0.0.1"),
                                                                                                                                   new (GeneralName.IPAddress, "172.23.144.1")
                                                                                                                               ]));

                server1_ECC_Certificate = certificateGenerator.Generate(new Asn1SignatureFactory(eccSignatureAlgorithm, serverCA_ECC_KeyPair.Private, secureRandom));

                using (var writer = new StreamWriter(server1_ECC_certificateFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(server1_ECC_Certificate);
                    pemWriter.Writer.Flush();
                }

            }

            if (server1_RSA_KeyPair        is null)
            {

                var rsaKeyPairGenerator = new RsaKeyPairGenerator();
                rsaKeyPairGenerator.Init(new KeyGenerationParameters(secureRandom, 2048));
                server1_RSA_KeyPair = rsaKeyPairGenerator.GenerateKeyPair();

                using (var writer = new StreamWriter(server1_RSA_privateKeyFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(server1_RSA_KeyPair.Private);
                    pemWriter.Writer.Flush();
                }

            }

            if (server1_RSA_Certificate    is null)
            {

                var certificateGenerator  = new X509V3CertificateGenerator();

                certificateGenerator.SetIssuerDN     (new X509Name(serverCA_RSA_Certificate.SubjectDN.ToString()));
                certificateGenerator.SetSubjectDN    (new X509Name("CN=api1.charging.cloud, O=GraphDefined GmbH, OU=RSA, L=Jena, C=Germany"));
                certificateGenerator.SetNotBefore    (Timestamp.Now.AddDays  (-1).DateTime);
                certificateGenerator.SetNotAfter     (Timestamp.Now.AddMonths(+3).DateTime);
                certificateGenerator.SetPublicKey    (server1_RSA_KeyPair.Public);
                certificateGenerator.SetSerialNumber (BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), secureRandom));

                certificateGenerator.AddExtension    (X509Extensions.KeyUsage,               critical: true,  new KeyUsage        (KeyUsage.DigitalSignature | KeyUsage.KeyEncipherment));
                certificateGenerator.AddExtension    (X509Extensions.ExtendedKeyUsage,       critical: true,  new ExtendedKeyUsage(KeyPurposeID.id_kp_serverAuth));

                certificateGenerator.AddExtension    (X509Extensions.SubjectAlternativeName, critical: false, new GeneralNames([
                                                                                                                                   new (GeneralName.DnsName,   "api1.charging.cloud"),
                                                                                                                                   new (GeneralName.IPAddress, "127.0.0.1"),
                                                                                                                                   new (GeneralName.IPAddress, "172.23.144.1")
                                                                                                                               ]));

                server1_RSA_Certificate = certificateGenerator.Generate(new Asn1SignatureFactory(rsaSignatureAlgorithm, serverCA_RSA_KeyPair.Private, secureRandom));

                using (var writer = new StreamWriter(server1_RSA_certificateFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(server1_RSA_Certificate);
                    pemWriter.Writer.Flush();
                }

            }


            if (client1_ECC_KeyPair        is null)
            {

                var keyPairGenerator = new ECKeyPairGenerator();
                keyPairGenerator.Init(eccKeyGenParams);
                client1_ECC_KeyPair = keyPairGenerator.GenerateKeyPair();

                using (var writer = new StreamWriter(client1_ECC_privateKeyFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(client1_ECC_KeyPair.Private);
                    pemWriter.Writer.Flush();
                }

            }

            if (client1_ECC_Certificate    is null)
            {

                var certificateGenerator  = new X509V3CertificateGenerator();

                certificateGenerator.SetIssuerDN     (new X509Name(clientCA_ECC_Certificate.SubjectDN.ToString()));
                certificateGenerator.SetSubjectDN    (new X509Name("CN=client1, O=GraphDefined GmbH, OU=ECC, L=Jena, C=Germany"));
                certificateGenerator.SetNotBefore    (Timestamp.Now.AddDays  (-1).DateTime);
                certificateGenerator.SetNotAfter     (Timestamp.Now.AddMonths(+3).DateTime);
                certificateGenerator.SetPublicKey    (client1_ECC_KeyPair.Public);
                certificateGenerator.SetSerialNumber (BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), secureRandom));

                certificateGenerator.AddExtension    (X509Extensions.KeyUsage,         critical: true, new KeyUsage        (KeyUsage.NonRepudiation | KeyUsage.DigitalSignature | KeyUsage.KeyEncipherment));
                certificateGenerator.AddExtension    (X509Extensions.ExtendedKeyUsage, critical: true, new ExtendedKeyUsage(KeyPurposeID.id_kp_clientAuth));

                client1_ECC_Certificate = certificateGenerator.Generate(new Asn1SignatureFactory(eccSignatureAlgorithm, clientCA_ECC_KeyPair.Private, secureRandom));

                using (var writer = new StreamWriter(client1_ECC_certificateFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(client1_ECC_Certificate);
                    pemWriter.Writer.Flush();
                }

            }

            if (client1_RSA_KeyPair        is null)
            {

                var rsaKeyPairGenerator = new RsaKeyPairGenerator();
                rsaKeyPairGenerator.Init(new KeyGenerationParameters(secureRandom, 2048));
                client1_RSA_KeyPair = rsaKeyPairGenerator.GenerateKeyPair();

                using (var writer = new StreamWriter(client1_RSA_privateKeyFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(client1_RSA_KeyPair.Private);
                    pemWriter.Writer.Flush();
                }

            }

            if (client1_RSA_Certificate    is null)
            {

                var certificateGenerator  = new X509V3CertificateGenerator();

                certificateGenerator.SetIssuerDN     (new X509Name(clientCA_RSA_Certificate.SubjectDN.ToString()));
                certificateGenerator.SetSubjectDN    (new X509Name("CN=client1, O=GraphDefined GmbH, OU=RSA, L=Jena, C=Germany"));
                certificateGenerator.SetNotBefore    (Timestamp.Now.AddDays  (-1).DateTime);
                certificateGenerator.SetNotAfter     (Timestamp.Now.AddMonths(+3).DateTime);
                certificateGenerator.SetPublicKey    (client1_RSA_KeyPair.Public);
                certificateGenerator.SetSerialNumber (BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), secureRandom));

                certificateGenerator.AddExtension    (X509Extensions.KeyUsage,         critical: true, new KeyUsage        (KeyUsage.NonRepudiation | KeyUsage.DigitalSignature | KeyUsage.KeyEncipherment));
                certificateGenerator.AddExtension    (X509Extensions.ExtendedKeyUsage, critical: true, new ExtendedKeyUsage(KeyPurposeID.id_kp_clientAuth));

                client1_RSA_Certificate = certificateGenerator.Generate(new Asn1SignatureFactory(rsaSignatureAlgorithm, clientCA_RSA_KeyPair.Private, secureRandom));

                using (var writer = new StreamWriter(client1_RSA_certificateFile))
                {
                    var pemWriter = new PemWriter(writer);
                    pemWriter.WriteObject(client1_RSA_Certificate);
                    pemWriter.Writer.Flush();
                }

            }

            #endregion

            #region Setup Central System v1.6

            var testCentralSystemV1_6 = new OCPPv1_6.TestCentralSystemNode(
                                            Id:              NetworkingNode_Id.Parse("CentralSystem"),
                                            VendorName:      "GraphDefined GmbH",
                                            Model:           "OCPPv1.6 Test Central System",
                                            HTTPUploadPort:  IPPort.Parse(8801),
                                            DNSClient:       dnsClient
                                        );

            testCentralSystemV1_6.AttachWebSocketServer(
                TCPPort:                         IPPort.Parse(8800),
                Description:                     I18NString.Create("OCPP v1.6 without internal security, but maybe with external TLS termination"),
                RequireAuthentication:           false,
                DisableWebSocketPings:           false,
                //SlowNetworkSimulationDelay:      TimeSpan.FromMilliseconds(10),
                AutoStart:                       true
            );

            //testCentralSystemV1_6.AttachSOAPService(
            //    TCPPort:                      IPPort.Parse(8800),
            //    DNSClient:                    dnsClient,
            //    AutoStart:                    true
            //);

            //testCentralSystemV1_6.AddHTTPBasicAuth(NetworkingNode_Id.Parse("CP001"), "test1234test1234");


            #region HTTP Web Socket connections

            //testCentralSystemV1_6.OnNewTCPConnection             += async (timestamp, server, connection,              eventTrackingId,                     cancellationToken) => {

            //    await DebugLog(
            //        $"New TCP connection from {connection.RemoteSocket}",
            //        cancellationToken
            //    );

            //    await WriteToLogfileV1_6(
            //        $"{timestamp.ToISO8601()}\tNEW TCP\t-\t{connection.RemoteSocket}",
            //        cancellationToken
            //    );

            //};

            //testCentralSystemV1_6.OnNewWebSocketConnection       += async (timestamp, server, connection, chargeBoxId, sharedSubprotocols, eventTrackingId, cancellationToken) => {

            //    await DebugLog(
            //        $"New HTTP web socket connection from '{chargeBoxId}' ({connection.RemoteSocket}) using '{sharedSubprotocols.AggregateWith(", ")}'",
            //        cancellationToken
            //    );

            //    await WriteToLogfileV1_6(
            //        $"{timestamp.ToISO8601()}\tNEW WS\t{chargeBoxId}\t{connection.RemoteSocket}",
            //        cancellationToken
            //    );

            //};

            //testCentralSystemV1_6.OnCloseMessageReceived         += async (timestamp, server, connection, chargeBoxId, eventTrackingId, statusCode, reason, cancellationToken) => {

            //    await DebugLog(
            //        $"'{chargeBoxId}' wants to close its HTTP web socket connection ({connection.RemoteSocket}): {statusCode}{(reason is not null ? $", '{reason}'" : "")}",
            //        cancellationToken
            //    );

            //    await WriteToLogfileV1_6(
            //        $"{timestamp.ToISO8601()}\tCLOSE\t{chargeBoxId}\t{connection.RemoteSocket}",
            //        cancellationToken
            //    );

            //};

            //testCentralSystemV1_6.OnTCPConnectionClosed          += async (timestamp, server, connection, chargeBoxId, eventTrackingId, reason,             cancellationToken) => {

            //    await DebugLog(
            //        $"'{chargeBoxId}' closed its HTTP web socket connection ({connection.RemoteSocket}){(reason is not null ? $": '{reason}'" : "")}",
            //        cancellationToken
            //    );

            //    await WriteToLogfileV1_6(
            //        $"{timestamp.ToISO8601()}\tCLOSED\t{chargeBoxId}\t{connection.RemoteSocket}",
            //        cancellationToken
            //    );

            //};

            #endregion

            #region JSON Messages

            //testCentralSystemV1_6.CentralSystemServers.First().OnJSONMessageRequestReceived += async (timestamp,
            //                                                                                          server,
            //                                                                                          connection,
            //                                                                                          destinationId,
            //                                                                                          networkPath,
            //                                                                                          eventTrackingId,
            //                                                                                          requestTimestamp,
            //                                                                                          requestMessage,
            //                                                                                          cancellationToken) => {

            //    DebugX.Log($"Received a web socket JSON message: '{requestMessage.ToString(Formatting.None)}'!");

            //    var chargeBoxId = "xxx";

            //    //await DebugLog(
            //    //    $"Received a JSON web socket request from '{chargeBoxId}': '{requestMessage.ToString(Formatting.None)}'!",
            //    //    cancellationToken
            //    //);

            //    //await WriteToLogfileV1_6(
            //    //    $"{requestTimestamp.ToISO8601()}\tREQ IN\t{chargeBoxId}\t{connection.RemoteSocket}\t{requestMessage.ToString(Formatting.None)}",
            //    //    cancellationToken
            //    //);

            //    //lock (testCSMSv1_6)
            //    //{
            //    //    File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "TextMessages.log"),
            //    //                       String.Concat(timestamp.ToISO8601(), "\tIN\t", connection.TryGetCustomData("chargingStationId"), "\t", connection.RemoteSocket, "\t", requestMessage, Environment.NewLine));
            //    //}

            //};

            //testCentralSystemV1_6.CentralSystemServers.First().OnJSONMessageRequestSent += async (timestamp,
            //                                                                                      server,
            //                                                                                      connection,
            //                                                                                      destinationId,
            //                                                                                      networkPath,
            //                                                                                      eventTrackingId,
            //                                                                                      requestTimestamp,
            //                                                                                      requestMessage,
            //                                                                                      cancellationToken) => {

            //    DebugX.Log($"Sent     a web socket TEXT message: '{requestMessage.ToString(Formatting.None)}'!");

            //    //lock (testCentralSystemV1_6)
            //    //{
            //    //    File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "TextMessages.log"),
            //    //                       String.Concat(timestamp.ToISO8601(), "\tOUT\t", connection.TryGetCustomData("chargingStationId"), "\t", connection.RemoteSocket, "\t", requestMessage, Environment.NewLine));
            //    //}

            //};




            //testCentralSystemV1_6.OnJSONMessageRequestReceived   += async (timestamp, server, connection, eventTrackingId, requestTimestamp, requestMessage,     cancellationToken) => {

            //    await DebugLog(
            //        $"Received a JSON web socket request: '{requestMessage.ToString(Formatting.None)}'!",
            //        cancellationToken
            //    );

            //    await WriteToLogfileV1_6(
            //        $"{requestTimestamp.ToISO8601()}\tREQ IN\t{connection.TryGetCustomData("chargingStationId")}\t{connection.RemoteSocket}\t{requestMessage.ToString(Formatting.None)}",
            //        cancellationToken
            //    );

            //};

            //testCentralSystemV1_6.OnJSONMessageResponseSent      += async (timestamp, server, connection, eventTrackingId, requestTimestamp, jsonRequestMessage, binaryRequestMessage, responseTimestamp, jsonResponseMessage)   => {

            //    var cancellationToken = CancellationToken.None;

            //    await DebugLog(
            //        $"Sent a JSON web socket response: '{jsonResponseMessage.ToString(Formatting.None)}'!",
            //        cancellationToken
            //    );

            //    await WriteToLogfileV1_6(
            //        $"{responseTimestamp.ToISO8601()}\tRES OUT\t{connection.TryGetCustomData("chargingStationId")}\t{connection.RemoteSocket}\t{jsonResponseMessage.ToString(Formatting.None)}",
            //        cancellationToken
            //    );

            //};


            //testCentralSystemV1_6.OnJSONMessageRequestSent       += async (timestamp, server, connection, eventTrackingId, requestTimestamp, requestMessage,     cancellationToken) => {

            //    await DebugLog(
            //        $"Sent a JSON web socket request: '{requestMessage.ToString(Formatting.None)}'!",
            //        cancellationToken
            //    );

            //    await WriteToLogfileV1_6(
            //        $"{requestTimestamp.ToISO8601()}\tREQ OUT\t{connection.TryGetCustomData("chargingStationId")}\t{connection.RemoteSocket}\t{requestMessage.ToString(Formatting.None)}",
            //        cancellationToken
            //    );

            //};

            //testCentralSystemV1_6.OnJSONMessageResponseReceived  += async (timestamp, server, connection, eventTrackingId, requestTimestamp, jsonRequestMessage, binaryRequestMessage, responseTimestamp, jsonResponseMessage)   => {

            //    var cancellationToken = CancellationToken.None;

            //    await DebugLog(
            //        $"Received a JSON web socket response: '{jsonResponseMessage.ToString(Formatting.None)}'!",
            //        cancellationToken
            //    );

            //    await WriteToLogfileV1_6(
            //        $"{responseTimestamp.ToISO8601()}\tRES IN\t{connection.TryGetCustomData("chargingStationId")}\t{connection.RemoteSocket}\t{jsonResponseMessage.ToString(Formatting.None)}",
            //        cancellationToken
            //    );

            //};

            #endregion

            #endregion

            #region Setup CSMS v2.1

            var testCSMSv2_1 = new OCPPv2_1.CSMS.TestCSMSNode(

                                   Id:                      NetworkingNode_Id.Parse("OCPPv2.1-CSMS-01"),
                                   VendorName:              "GraphDefined GmbH",
                                   Model:                   "vCSMS",
                                   Description:             I18NString.Create(Languages.en, "Our first virtual CSMS!"),
                                   SerialNumber:            "SN-CSMS0001",
                                   SoftwareVersion:         "v0.1",
                                   DisableSendHeartbeats:   true,

                                   HTTPAPI_Port:            IPPort.Parse(7000),
                                   //HTTPAPI_Disabled:        false,

                                   //WebAPI:                  csms => new OCPPv2_1.CSMS.WebAPI(
                                   //                                     CSMS:   csms,
                                   //                                     HTTPServer:  csms.HTTPAPI.DevelopmentServers
                                   //                                 ),

                                   DNSClient :              dnsClient

                               );

            testCSMSv2_1.AddControlWebSocketServer(
                new WebSocketServer(
                    HTTPPort:               IPPort.Parse(7001),
                    Description:            I18NString.Create(Languages.en, "Logging HTTP WebSocket Server"),
                    HTTPServerName:         "OCPP CSMS Logging WebSocket Server",
                    RequireAuthentication:  false,
                    AutoStart:              true
                )
            );

            #region 8820 - OCPP v2.1 without internal security, but maybe with external TLS termination

            testCSMSv2_1.AttachWebSocketServer(
                TCPPort:                         IPPort.Parse(8820),
                Description:                     I18NString.Create("OCPP v2.1 without internal security, but maybe with external TLS termination"),
                RequireAuthentication:           false,
                DisableWebSocketPings:           false,
                WebSocketPingEvery:              TimeSpan.FromMinutes(1),
                //ClientCAKeyPair:             clientCA_RSA_KeyPair,
                //ClientCACertificate:         clientCA_RSA_Certificate,
                //SlowNetworkSimulationDelay:  TimeSpan.FromMilliseconds(10),
                AutoStart:                       true
            );

            #endregion


            #region 8821 - OCPP v2.1 with internal TLS termination using a private ECC PKI

            // cat serverCA.cert rootCA.cert > caChain.cert
            // openssl s_client -connect 127.0.0.1:9921 -CAfile caChain.cert -showcerts
            // openssl ec   -in server1ECC.key  -pubout 2>/dev/null | openssl dgst -sha256
            // openssl x509 -in server1ECC.cert -pubkey -noout      | openssl dgst -sha256
            //
            // openssl pkcs12 -export -out server1ECC.pfx -inkey server1ECC.key -in server1ECC.cert -certfile caChain.cert
            // openssl pkcs12 -in server1ECC.pfx - nokeys - passin pass:
            //
            // openssl ecparam -name secp256r1 -genkey -out secp256r1key.pem
            // MSYS_NO_PATHCONV=1 openssl req -new -key secp256r1key.pem -out secp256r1req.pem -subj '/C=US/ST=YourState/L=YourCity/O=YourOrganization/CN=yourname'
            // openssl x509 -req -in secp256r1req.pem -CA serverCA.cert -CAkey serverCA.key -CAcreateserial -out secp256r1cert.pem -days 365 -sha256

            // https://stackoverflow.com/questions/72096812/loading-x509certificate2-from-pem-file-results-in-no-credentials-are-available
            // https://www.daimto.com/how-to-use-x509certificate2-with-pem-file/
            // The TLS layer on Windows requires that the private key be written to disk (in a particular way).
            // The PEM-based certificate loading doesn't do that, only PFX-loading does.
            // The easiest way to make the TLS layer happy is to do:
            //     cert = new X509Certificate2(cert.Export(X509ContentType.Pfx));
            // That is, export the cert+key to a PFX, then import it again immediately (to get the side effect of the key being (temporarily)
            // written to disk in a way that SChannel can find it). You shouldn't need to bother with changing the PFX load flags off of the defaults,
            // though some complicatedly constrained users might need to use MachineKeySet.
            testCSMSv2_1.AttachWebSocketServer(
                TCPPort:                     IPPort.Parse(8821),
                Description:                 I18NString.Create("OCPP v2.1 with internal TLS termination using a private ECC PKI"),
                RequireAuthentication:       true,
                ServerCertificateSelector:   () => //new System.Security.Cryptography.X509Certificates.X509Certificate2(server1ECC_pfx, "", System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.PersistKeySet),
                                                   new System.Security.Cryptography.X509Certificates.X509Certificate2(
                                                       System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPemFile(
                                                           server1_ECC_certificateFile,
                                                           server1_ECC_privateKeyFile
                                                       ).Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx)
                                                   ),

                                                   /// Authentication failed because the platform does not support ephemeral keys.
                                                   //System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPemFile(
                                                   //    Path.Combine(AppContext.BaseDirectory, "pki", "secp256r1cert.pem"),
                                                   //    Path.Combine(AppContext.BaseDirectory, "pki", "secp256r1key.pem")
                                                   //),
                DisableWebSocketPings:       false,

                //SlowNetworkSimulationDelay:  TimeSpan.FromMilliseconds(10),
                AutoStart:                   true
            );

            #endregion

            #region 8822 - OCPP v2.1 with internal TLS termination using a private RSA PKI

            // cat serverCA_RSA.cert rootCA_RSA.cert > caChain_RSA.cert
            // openssl s_client -connect 127.0.0.1:9922 -CAfile caChain_RSA.cert -showcerts
            // CONNECTED(00000160)
            // Can't use SSL_get_servername
            // depth=2 CN = Open Charging Cloud - Root CA, O = GraphDefined GmbH, L = Jena, C = Germany
            // verify return:1
            // depth=1 CN = Open Charging Cloud - Server CA, O = GraphDefined GmbH, L = Jena, C = Germany
            // verify return:1
            // depth=0 CN = api1.charging.cloud, O = GraphDefined GmbH, L = Jena, C = Germany
            // verify return:1
            // ---
            // Certificate chain
            //  0 s:CN = api1.charging.cloud, O = GraphDefined GmbH, L = Jena, C = Germany
            //    i:CN = Open Charging Cloud - Server CA, O = GraphDefined GmbH, L = Jena, C = Germany
            //    a:PKEY: rsaEncryption, 2048 (bit); sigalg: RSA-SHA256
            //    v:NotBefore: Mar  2 00:34:59 2024 GMT; NotAfter: Jun  3 00:34:59 2024 GMT
            testCSMSv2_1.AttachWebSocketServer(
                TCPPort:                     IPPort.Parse(8822),
                Description:                 I18NString.Create("OCPP v2.1 with internal TLS termination using a private RSA PKI"),
                RequireAuthentication:       true,
                ServerCertificateSelector:   () => ToDotNet(server1_RSA_Certificate, server1_RSA_KeyPair.Private)!,
                                                   //NotWorking:  System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPemFile(server1RSA_certificateFile, server1RSA_privateKeyFile),
                                                   //NotWorking:  ConvertToX509Certificate2(server1RSA_Certificate, server1RSA_KeyPair.Private),
                                                   //IsWorking:   new System.Security.Cryptography.X509Certificates.X509Certificate2(server1RSA_pfx, "", System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.PersistKeySet),
                AllowedTLSProtocols:         System.Security.Authentication.SslProtocols.Tls12,

                DisableWebSocketPings:       false,
                //SlowNetworkSimulationDelay:  TimeSpan.FromMilliseconds(10),
                AutoStart:                   true
            );

            #endregion



            #region 8823 - OCPP v2.1 with internal TLS termination using a private RSA PKI enforcing TLS client authentication

            // Show client certificate details: openssl.exe x509 -in client1RSA.cert -text -noout
            //
            // cat serverCA_RSA.cert clientCA_RSA.cert rootCA_RSA.cert > caChain_RSA.cert
            // openssl s_client -connect 127.0.0.1:9923 -cert client1_RSA.cert -key client1_RSA.key -CAfile caChain_RSA.cert -showcerts
            testCSMSv2_1.AttachWebSocketServer(

                TCPPort:                      IPPort.Parse(8823),
                Description:                  I18NString.Create("OCPP v2.1 with internal TLS termination using a private RSA PKI enforcing TLS client authentication"),
                RequireAuthentication:        true,
                ServerCertificateSelector:    () => ToDotNet(server1_RSA_Certificate, server1_RSA_KeyPair.Private)!,
                                                    //NotWorking:  System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPemFile(server1RSA_certificateFile, server1RSA_privateKeyFile),
                                                    //NotWorking:  ConvertToX509Certificate2(server1RSA_Certificate, server1RSA_KeyPair.Private),
                                                    //IsWorking:   new System.Security.Cryptography.X509Certificates.X509Certificate2(server1RSA_pfx, "", System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.PersistKeySet),
                AllowedTLSProtocols:          System.Security.Authentication.SslProtocols.Tls12 |
                                              System.Security.Authentication.SslProtocols.Tls13,

                ClientCertificateRequired:    true,
                ClientCertificateValidator:   (sender,
                                               certificate,
                                               certificateChain,
                                               webSocketServer,
                                               policyErrors) => {

                                                   if (certificate      is not null &&
                                                       certificateChain is not null)
                                                   {

                                                       if (webSocketServer.TrustedClientCertificates.Contains(certificate))
                                                           return TLSValidationResult.Success();

                                                       return TLSValidationResult.Failed("Could not validate the received TLS client certificate!");

                                                   }

                                                   return TLSValidationResult.Failed("Missing or invalid TLS client certificate!");

                                               },

                LocalCertificateSelector:     (sender,
                                               targetHost,
                                               localCertificates,
                                               remoteCertificate,
                                               acceptableIssuers) => {
                                                   return localCertificates.First();
                                               },

                DisableWebSocketPings:        false,
                //SlowNetworkSimulationDelay:  TimeSpan.FromMilliseconds(10),
                AutoStart:                    true

            );

            #endregion



            #region Connect to LocalController

            //var ocppGatewayConnectResult1    = await testCSMSv2_1.ConnectOCPPWebSocketClient(

            //                                       RemoteURL:                    URL.Parse($"ws://127.0.0.1:9920"),
            //                                       VirtualHostname:              null,
            //                                       Description:                  I18NString.Create("CSMS to LC"),
            //                                       PreferIPv4:                   null,
            //                                       RemoteCertificateValidator:   null,
            //                                       LocalCertificateSelector:     null,
            //                                       ClientCert:                   null,
            //                                       TLSProtocol:                  null,
            //                                       HTTPUserAgent:                null,
            //                                       HTTPAuthentication:           HTTPBasicAuthentication.Create(
            //                                                                         "csms1",
            //                                                                         "csms2lc_12345678!"
            //                                                                     ),
            //                                       RequestTimeout:               null,
            //                                       TransmissionRetryDelay:       null,
            //                                       MaxNumberOfRetries:           3,
            //                                       InternalBufferSize:           null,

            //                                       SecWebSocketProtocols:        null,
            //                                       NetworkingMode:               NetworkingMode.OverlayNetwork,
            //                                       NextHopNetworkingNodeId:      NetworkingNode_Id.Parse("lc1"),

            //                                       DisableWebSocketPings:        false,
            //                                       WebSocketPingEvery:           null,
            //                                       SlowNetworkSimulationDelay:   null,

            //                                       DisableMaintenanceTasks:      false,
            //                                       MaintenanceEvery:             null,

            //                                       LoggingPath:                  null,
            //                                       LoggingContext:               String.Empty,
            //                                       LogfileCreator:               null,
            //                                       HTTPLogger:                   null,
            //                                       DNSClient:                    null

            //                                   );

            #endregion


            #region HowTo test using Win11 + WSL

            // Win11:
            //  - Import RootCA to "Trusted Root Certification Authorities" for the entire computer
            //  - Import ServerCA to "Intermediate Certification Authorities" for the entire computer
            //  - Verify via "certmgr"

            // Win11 WSL (Debian):
            //  - sudo wget -qO /usr/local/bin/websocat https://github.com/vi/websocat/releases/latest/download/websocat.x86_64-unknown-linux-musl
            //  - chmod +x /usr/local/bin/websocat
            //
            // Note: The IPv4 address of your host (here: 172.23.144.1) might be different!

            // $ websocat --protocol ocpp2.1 --basic-auth a:b -v ws://172.23.144.1:9920
            // [INFO  websocat::lints] Auto-inserting the line mode
            // [INFO  websocat::stdio_threaded_peer] get_stdio_peer (threaded)
            // [INFO  websocat::ws_client_peer] get_ws_client_peer
            // [INFO  websocat::ws_client_peer] Connected to ws
            //
            // Paste the following line:
            // [2,"100000","BootNotification",{"chargingStation":{"model":"aa","vendorName":"bb"},"reason":"ApplicationReset"}]
            //
            // [3,"100000",{"status":"Rejected","currentTime":"2024-03-03T11:46:59.076Z","interval":30}]
            // [INFO  websocat::ws_peer] Received WebSocket ping

            // $ cat serverCA_RSA.cert rootCA_RSA.cert > caChain_RSA.cert
            // $ export SSL_CERT_FILE=/home/ahzf/OCPPTests/caChain_RSA.cert
            // $ websocat --protocol ocpp2.1 --basic-auth a:b -v wss://172.23.144.1:9922
            // [INFO  websocat::lints] Auto-inserting the line mode
            // [INFO  websocat::stdio_threaded_peer] get_stdio_peer (threaded)
            // [INFO  websocat::ws_client_peer] get_ws_client_peer
            // [INFO  websocat::ws_client_peer] Connected to ws
            //
            // Paste the following line:
            // [2,"100000","BootNotification",{"chargingStation":{"model":"aa","vendorName":"bb"},"reason":"ApplicationReset"}]
            //
            // [3,"100000",{"status":"Rejected","currentTime":"2024-03-03T11:43:54.364Z","interval":30}]
            // [INFO  websocat::ws_peer] Received WebSocket ping

            #endregion


            #region HTTP Web Socket connections

            testCSMSv2_1.OnNewWebSocketTCPConnection            += async (timestamp, server, connection,                   eventTrackingId,                     cancellationToken) => {

                await DebugLog(
                    $"New TCP connection from {connection.RemoteSocket}",
                    cancellationToken
                );

                await WriteToLogfileV2_1(
                    $"{timestamp.ToISO8601()}\tNEW TCP\t-\t{connection.RemoteSocket}",
                    cancellationToken
                );
            };

            testCSMSv2_1.OnNewWebSocketServerConnection         += async (timestamp, server, connection, sharedSubprotocols, selectedSubprotocol, eventTrackingId, cancellationToken) => {

                await DebugLog(
                    $"New HTTP web socket connection from '{connection.Login}' ({connection.RemoteSocket}) using '{selectedSubprotocol}' [{sharedSubprotocols.AggregateWith(", ")}]",
                    cancellationToken
                );

                await WriteToLogfileV2_1(
                    $"{timestamp.ToISO8601()}\tNEW WS\t{connection.Login}\t{connection.RemoteSocket}",
                    cancellationToken
                );

            };

            testCSMSv2_1.OnWebSocketServerCloseMessageReceived  += async (timestamp, server, connection, frame, eventTrackingId, statusCode, reason, cancellationToken) => {

                await DebugLog(
                    $"'{connection.Login}' wants to close its HTTP web socket connection ({connection.RemoteSocket}): {statusCode}{(reason is not null ? $", '{reason}'" : "")}",
                    cancellationToken
                );

                await WriteToLogfileV2_1(
                    $"{timestamp.ToISO8601()}\tCLOSE\t{connection.Login}\t{connection.RemoteSocket}",
                    cancellationToken
                );

            };

            testCSMSv2_1.OnWebSocketServerTCPConnectionClosed   += async (timestamp, server, connection, eventTrackingId, reason, cancellationToken) => {

                await DebugLog(
                    $"'{connection.Login}' closed its HTTP web socket connection ({connection.RemoteSocket}){(reason is not null ? $": '{reason}'" : "")}",
                    cancellationToken
                );

                await WriteToLogfileV2_1(
                    $"{timestamp.ToISO8601()}\tCLOSED\t{connection.Login}\t{connection.RemoteSocket}",
                    cancellationToken
                );

            };

            #endregion

            #region HTTP Web Socket Pings/Pongs

            //(testCSMSv2_1.CSMSServers.First() as WebSocketServer).OnPingMessageReceived += async (timestamp, server, connection, eventTrackingId, frame) => {
            //    DebugX.Log(nameof(WebSocketServer) + ": Ping received: '" + frame.Payload.ToUTF8String() + "' (" + connection.TryGetCustomData("chargingStationId") + ", " + connection.RemoteSocket + ")");
            //    lock (testCSMSv2_1)
            //    {
            //        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "TextMessages.log"),
            //                           String.Concat(timestamp.ToISO8601(), "\tPING IN\t", connection.TryGetCustomData("chargingStationId"), "\t", connection.RemoteSocket, Environment.NewLine));
            //    }
            //};

            //(testCSMSv2_1.CSMSServers.First() as WebSocketServer).OnPingMessageSent += async (timestamp, server, connection, eventTrackingId, frame) => {
            //    DebugX.Log(nameof(WebSocketServer) + ": Ping sent:     '" + frame.Payload.ToUTF8String() + "' (" + connection.TryGetCustomData("chargingStationId") + ", " + connection.RemoteSocket + ")");
            //    lock (testCSMSv2_1)
            //    {
            //        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "TextMessages.log"),
            //                           String.Concat(timestamp.ToISO8601(), "\tPING OUT\t", connection.TryGetCustomData("chargingStationId"), "\t", connection.RemoteSocket, Environment.NewLine));
            //    }
            //};

            //(testCSMSv2_1.CSMSServers.First() as WebSocketServer).OnPongMessageReceived += async (timestamp, server, connection, eventTrackingId, frame) => {
            //    DebugX.Log(nameof(WebSocketServer) + ": Pong received: '" + frame.Payload.ToUTF8String() + "' (" + connection.TryGetCustomData("chargingStationId") + ", " + connection.RemoteSocket + ")");
            //    lock (testCSMSv2_1)
            //    {
            //        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "TextMessages.log"),
            //                           String.Concat(timestamp.ToISO8601(), "\tPONG IN\t", connection.TryGetCustomData("chargingStationId"), "\t", connection.RemoteSocket, Environment.NewLine));
            //    }
            //};

            #endregion

            #region JSON Messages

            //testCSMSv2_1.OnJSONMessageSent += async (timestamp, server, connection, messageTimestamp, eventTrackingId, message, sentStatus, ct) =>
            //{
            //    await WriteToLogfileV2_1(
            //        $"{messageTimestamp.ToISO8601()}\tMSG OUT\t-\t{connection.RemoteSocket}\t{message.ToString(Formatting.None)}",
            //        ct
            //    );
            //};

            //testCSMSv2_1.OnJSONMessageReceived += async (timestamp, server, connection, messageTimestamp, eventTrackingId, sourceNodeId, message, ct) =>
            //{
            //    await WriteToLogfileV2_1(
            //        $"{messageTimestamp.ToISO8601()}\tMSG IN\t{sourceNodeId}\t{connection.RemoteSocket}\t{message.ToString(Formatting.None)}",
            //        ct
            //    );
            //};

            testCSMSv2_1.OCPP.IN.OnJSONRequestMessageReceived += async (timestamp, server, connection, request, ct) =>
            {
                await WriteToLogfileV2_1(
                    $"{request.RequestTimestamp.ToISO8601()}\tREQ IN\t{request.NetworkPath.Source}\t{connection?.RemoteSocket}\t{request.RequestId}\t{request.Action}\t{request.Payload.ToString(Formatting.None)}",
                    ct
                );
            };

            testCSMSv2_1.OCPP.OUT.OnJSONResponseMessageSent += async (timestamp, sender, connection, response, sentMessageResult, ct) =>
            {
                await WriteToLogfileV2_1(
                    $"{response.ResponseTimestamp.ToISO8601()}\tRES OUT\t{response.Destination}\t{connection?.RemoteSocket}\t{response.RequestId}\t-\t{response.Payload.ToString(Formatting.None)}",
                    ct
                );
            };


            testCSMSv2_1.OCPP.OUT.OnJSONRequestMessageSent += async (timestamp, sender, connection, request, sentMessageResult, ct) =>
            {
                await WriteToLogfileV2_1(
                    $"{request.RequestTimestamp.ToISO8601()}\tREQ OUT\t{request.Destination}\t{connection?.RemoteSocket}\t{request.RequestId}\t{request.Action}\t{request.Payload.ToString(Formatting.None)}",
                    ct
                );
            };

            testCSMSv2_1.OCPP.IN.OnJSONResponseMessageReceived += async (timestamp, server, connection, response, ct) =>
            {
                await WriteToLogfileV2_1(
                    $"{response.ResponseTimestamp.ToISO8601()}\tRES IN\t{response.NetworkPath.Source}\t{connection?.RemoteSocket}\t{response.RequestId}\t-\t{response.Payload.ToString(Formatting.None)}",
                    ct
                );
            };

            //testCSMSv2_1.OnJSONMessageRequestReceived     += async (timestamp, server, connection, destinationId, networkPath, eventTrackingId, requestTimestamp, requestMessage,     cancellationToken) => {

            //    await DebugLog(
            //        $"Received a JSON web socket request from '{destinationId}': '{requestMessage.ToString(Formatting.None)}'!",
            //        cancellationToken
            //    );

            //    await WriteToLogfileV2_1(
            //        $"{requestTimestamp.ToISO8601()}\tREQ IN\t{destinationId}\t{connection.RemoteSocket}\t{requestMessage.ToString(Formatting.None)}",
            //        cancellationToken
            //    );

            //};

            //testCSMSv2_1.OnJSONMessageResponseSent        += async (timestamp, server, connection, networkingNodeId, networkPath, eventTrackingId, requestTimestamp, jsonRequestMessage, binaryRequestMessage, responseTimestamp, jsonResponseMessage, cancellationToken)   => {

            //    await DebugLog(
            //        $"Sent a JSON web socket response to '{networkingNodeId}': '{jsonResponseMessage.ToString(Formatting.None)}'!",
            //        cancellationToken
            //    );

            //    await WriteToLogfileV2_1(
            //        $"{responseTimestamp.ToISO8601()}\tRES OUT\t{networkingNodeId}\t{connection.RemoteSocket}\t{jsonResponseMessage.ToString(Formatting.None)}",
            //        cancellationToken
            //    );

            //};



            //testCSMSv2_1.OnJSONMessageRequestSent         += async (timestamp, server, connection, destinationId, networkPath, eventTrackingId, requestTimestamp, requestMessage,     cancellationToken) => {

            //    await DebugLog(
            //        $"Sent a JSON web socket request to '{destinationId}': '{requestMessage.ToString(Formatting.None)}'!",
            //        cancellationToken
            //    );

            //    await WriteToLogfileV2_1(
            //        $"{requestTimestamp.ToISO8601()}\tREQ OUT\t{destinationId}\t{connection.RemoteSocket}\t{requestMessage.ToString(Formatting.None)}",
            //        cancellationToken
            //    );

            //};

            //testCSMSv2_1.OnJSONMessageResponseReceived    += async (timestamp, server, connection, networkingNodeId, networkPath, eventTrackingId, requestTimestamp, jsonRequestMessage, binaryRequestMessage, responseTimestamp, jsonResponseMessage, cancellationToken)   => {

            //    await DebugLog(
            //        $"Received a JSON web socket response from '{networkingNodeId}': '{jsonResponseMessage.ToString(Formatting.None)}'!",
            //        cancellationToken
            //    );

            //    await WriteToLogfileV2_1(
            //        $"{responseTimestamp.ToISO8601()}\tRES IN\t{networkingNodeId}\t{connection.RemoteSocket}\t{jsonResponseMessage.ToString(Formatting.None)}",
            //        cancellationToken
            //    );

            //};

            //testCSMSv2_1.OnJSONErrorResponseReceived  += async (timestamp, server, connection,
            //                                                    //networkingNodeId, networkPath,
            //                                                    eventTrackingId,
            //                                                    requestTimestamp,
            //                                                    textRequestMessage, //ToDo: Just be JSON!
            //                                                    binaryRequestMessage,
            //                                                    responseTimestamp,
            //                                                    textResponseMessage, //ToDo: Just be JSON!
            //                                                    cancellationToken)   => {

            //    var networkingNodeId = "-";

            //    await DebugLog(
            //        $"Received a JSON web socket response from '{networkingNodeId}': '{textResponseMessage}'!",
            //        cancellationToken
            //    );

            //    await WriteToLogfileV2_1(
            //        $"{responseTimestamp.ToISO8601()}\tERR IN\t{networkingNodeId}\t{connection.RemoteSocket}\t{textResponseMessage}",
            //        cancellationToken
            //    );

            //};

            #endregion

            #region Binary Messages

            //testCSMSv2_1.OnBinaryMessageRequestReceived   += async (timestamp, server, connection, destinationId, networkPath, eventTrackingId, requestTimestamp, requestMessage,     cancellationToken) => {

            //    await DebugLog(
            //        $"Received a binary web socket request from '{destinationId}': '{requestMessage.ToBase64()}'!",
            //        cancellationToken
            //    );

            //    await WriteToLogfileV2_1(
            //        $"{requestTimestamp.ToISO8601()}\tREQ IN\t{destinationId}\t{connection.RemoteSocket}\t{requestMessage.ToBase64()}",
            //        cancellationToken
            //    );

            //};

            //testCSMSv2_1.OnBinaryMessageResponseSent      += async (timestamp, server, connection, destinationId, networkPath, eventTrackingId, requestTimestamp, jsonRequestMessage, binaryRequestMessage, responseTimestamp, binaryResponseMessage, cancellationToken) => {

            //    await DebugLog(
            //        $"Sent a binary web socket response to '{destinationId}': '{binaryResponseMessage.ToBase64()}'!",
            //        cancellationToken
            //    );

            //    await WriteToLogfileV2_1(
            //        $"{responseTimestamp.ToISO8601()}\tRES OUT\t{destinationId}\t{connection.RemoteSocket}\t{binaryResponseMessage.ToBase64()}",
            //        cancellationToken
            //    );

            //};

            //testCSMSv2_1.OnBinaryMessageRequestSent       += async (timestamp, server, connection, destinationId, networkPath, eventTrackingId, requestTimestamp, requestMessage,     cancellationToken) => {

            //    await DebugLog(
            //        $"Sent a binary web socket request to '{destinationId}': '{requestMessage.ToBase64()}'!",
            //        cancellationToken
            //    );

            //    await WriteToLogfileV2_1(
            //        $"{requestTimestamp.ToISO8601()}\tREQ OUT\t{destinationId}\t{connection.RemoteSocket}\t{requestMessage.ToBase64()}",
            //        cancellationToken
            //    );

            //};

            //testCSMSv2_1.OnBinaryMessageResponseReceived  += async (timestamp, server, connection, destinationId, networkPath, eventTrackingId, requestTimestamp, jsonRequestMessage, binaryRequestMessage, responseTimestamp, binaryResponseMessage, cancellationToken) => {

            //    await DebugLog(
            //        $"Received a binary web socket response from '{destinationId}': '{binaryResponseMessage.ToBase64()}'!",
            //        cancellationToken
            //    );

            //    await WriteToLogfileV2_1(
            //        $"{responseTimestamp.ToISO8601()}\tRES IN\t{destinationId}\t{connection.RemoteSocket}\t{binaryResponseMessage.ToBase64()}",
            //        cancellationToken
            //    );

            //};

            #endregion

            // ERRORS!!!

            #endregion



            var cli = new CSMSTestCLI(
                          testCentralSystemV1_6,
                          testCSMSv2_1
                      );

            await cli.Run();


            #region Shutdown

            await testCentralSystemV1_6.Shutdown();
            await testCSMSv2_1.         Stop();

            foreach (var DebugListener in Trace.Listeners)
                (DebugListener as TextWriterTraceListener)?.Flush();

            #endregion


        }

    }

}
