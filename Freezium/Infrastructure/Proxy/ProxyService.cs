using System;
using System.IO;
using System.Threading.Tasks;
using Fiddler;
using Freezium.Core;

namespace Freezium.Infrastructure.Proxy
{
    /// <summary>
    /// Manages the FiddlerCore proxy: certificate checks, start and stop processes.
    /// </summary>
    public class ProxyService
    {
        private readonly RequestInterceptor _requestInterceptor;
        private readonly ResponseInterceptor _responseInterceptor;

        public event Action<string> LogMessage;
        public event Action<string> StatusChanged;

        public bool IsRunning => FiddlerApplication.IsStarted();

        /// <summary>
        /// Initializes a new instance of <see cref="ProxyService"/> with the given request and
        /// response interceptors. These interceptors are hooked into the FiddlerCore event
        /// pipeline when the proxy is started and unhooked when it is stopped.
        /// </summary>
        /// <param name="requestInterceptor">
        /// The interceptor responsible for processing and optionally modifying outgoing requests
        /// before they are forwarded to the target server.
        /// </param>
        /// <param name="responseInterceptor">
        /// The interceptor responsible for processing and optionally modifying incoming responses
        /// before they are returned to the client.
        /// </param>
        public ProxyService(RequestInterceptor requestInterceptor, ResponseInterceptor responseInterceptor)
        {
            _requestInterceptor = requestInterceptor;
            _responseInterceptor = responseInterceptor;
        }

        /// <summary>
        /// Checks the Root CA certificate, creates it if not found, requests trust if not trusted.
        /// </summary>
        public bool EnsureCertificate()
        {
            var certDir = Path.GetDirectoryName(Constants.CertLocation);
            if (!Directory.Exists(certDir))
            {
                Directory.CreateDirectory(certDir);
            }

            var certMaker = new BCCertMaker.BCCertMaker();
            CertMaker.oCertProvider = certMaker;

            if (!File.Exists(Constants.CertLocation))
            {
                certMaker.CreateRootCertificate();
                certMaker.WriteRootCertificateAndPrivateKeyToPkcs12File(
                    Constants.CertLocation, Constants.CertPassword);
            }
            else
            {
                certMaker.ReadRootCertificateAndPrivateKeyFromPkcs12File(
                    Constants.CertLocation, Constants.CertPassword);
            }

            if (!CertMaker.rootCertIsTrusted())
            {
                CertMaker.trustRootCert();
            }

            return CertMaker.rootCertIsTrusted();
        }

        /// <summary>
        /// Starts the proxy. May wait for certificate trust confirmation.
        /// </summary>
        public async Task StartAsync()
        {
            if (IsRunning) return;

            LogMessage?.Invoke("Creating Internet Proxy");

            while (!EnsureCertificate())
            {
                StatusChanged?.Invoke("Waiting for you to trust certificate");
                await Task.Delay(3000);
            }

            var settings = new FiddlerCoreStartupSettingsBuilder()
                .ListenOnPort(Constants.ProxyPort)
                .RegisterAsSystemProxy()
                .ChainToUpstreamGateway()
                .DecryptSSL()
                .OptimizeThreadPool()
                .Build();

#pragma warning disable CS0618
            CONFIG.sHostsThatBypassFiddler = Constants.BypassHost;

            FiddlerApplication.BeforeRequest += _requestInterceptor.Handle;
            FiddlerApplication.BeforeResponse += _responseInterceptor.Handle;
            FiddlerApplication.Startup(settings);

            LogMessage?.Invoke($"Internet Proxy Enabled ({FiddlerApplication.oProxy.ListenPort})");
            StatusChanged?.Invoke("Running");
        }

        /// <summary>
        /// Gracefully stops the running proxy by detaching the request and response interceptors
        /// from the FiddlerCore event pipeline and then shutting down the FiddlerCore engine.
        /// Raises <see cref="LogMessage"/> and <see cref="StatusChanged"/> events to notify
        /// listeners that the proxy has been stopped. If the proxy is not currently running,
        /// the method returns immediately without performing any action.
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;

            FiddlerApplication.BeforeRequest -= _requestInterceptor.Handle;
            FiddlerApplication.BeforeResponse -= _responseInterceptor.Handle;
            FiddlerApplication.Shutdown();

            LogMessage?.Invoke("Proxy Stopped");
            StatusChanged?.Invoke("Stopped");
        }

        /// <summary>
        /// Forces an immediate shutdown of the FiddlerCore engine regardless of the proxy's
        /// current state. Unlike <see cref="Stop"/>, this method does not detach event handlers
        /// or fire status-changed notifications. It is intended to be used during application
        /// exit to ensure that FiddlerCore is fully cleaned up and system proxy settings are
        /// restored, even if <see cref="Stop"/> was not called beforehand.
        /// </summary>
        public void Shutdown()
        {
            FiddlerApplication.Shutdown();
        }
    }
}
