using System;
using System.Linq;
using Fiddler;
using Freezium.Core;
using Freezium.Core.Interfaces;
using Freezium.Infrastructure.Crypto;
using Freezium.Services;
using Newtonsoft.Json.Linq;

namespace Freezium.Infrastructure.Proxy
{
    /// <summary>
    /// Handles the FiddlerCore BeforeRequest event.
    /// Manipulates the request based on needs and activates response buffering.
    /// </summary>
    public class RequestInterceptor
    {
        private readonly IAnimeRepository _repository;

        public event Action<string> LogMessage;

        /// <summary>
        /// Initializes a new instance of <see cref="RequestInterceptor"/> with the given anime
        /// repository, which is used to persist watchlist, follow, and favorite state changes
        /// that are detected within intercepted request bodies.
        /// </summary>
        /// <param name="repository">The repository used to read and write local anime list data.</param>
        public RequestInterceptor(IAnimeRepository repository)
        {
            _repository = repository;
        }

        /// <summary>
        /// Entry point for the FiddlerCore <c>BeforeRequest</c> event. Filters out sessions
        /// that do not target the configured API host, and then dispatches the session to each
        /// specialized handler in sequence. Any unhandled exception is caught and reported via
        /// the <see cref="LogMessage"/> event so that a single bad request cannot crash the
        /// proxy pipeline.
        /// </summary>
        /// <param name="session">The FiddlerCore session representing the intercepted HTTP(S) request.</param>
        public void Handle(Session session)
        {
            try
            {
                if (!session.fullUrl.Contains(Constants.TargetApiHost))
                    return;

                CaptureHeaders(session);
                HandleUserGet(session);
                HandleWatchList(session);
                HandleFollow(session);
                HandleFavorite(session);
                HandlePages(session);
                HandleUserDetails(session);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"An Error Occurred, Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Scans the request headers for the <c>cf-control</c> header (case-insensitive) and,
        /// if present, stores its value in <see cref="AppSettingsService.Current.CfControl"/>.
        /// This header carries a Cloudflare control token that may be required for subsequent
        /// API calls or crypto operations.
        /// </summary>
        /// <param name="session">The session whose request headers are inspected.</param>
        private void CaptureHeaders(Session session)
        {
            var cfHeader = session.RequestHeaders
                .FirstOrDefault(x => x.Name.Equals("cf-control", StringComparison.OrdinalIgnoreCase));

            if (cfHeader != null)
            {
                AppSettingsService.Current.CfControl = cfHeader.Value;
            }
        }

        /// <summary>
        /// Handles requests targeting the <c>user/get</c> endpoint. Removes the
        /// <c>Accept-Encoding</c> header so that the server returns a plain, uncompressed
        /// response body, and enables response buffering so that the full response is available
        /// to the <see cref="ResponseInterceptor"/> before being forwarded to the client.
        /// </summary>
        /// <param name="session">The session to inspect and potentially modify.</param>
        private void HandleUserGet(Session session)
        {
            if (!session.uriContains("user/get")) return;

            RemoveAcceptEncoding(session);
            session.bBufferResponse = true;
            LogMessage?.Invoke("endpoint found buffering response");
        }

        /// <summary>
        /// Handles POST requests to the <c>anime/watch-list</c> endpoint when watch-list
        /// manipulation is enabled in settings. Delegates to
        /// <see cref="ProcessManipulationRequest"/> to decrypt the request body, persist the
        /// add/remove operation locally, and conditionally enable response buffering.
        /// </summary>
        /// <param name="session">The session to inspect and potentially modify.</param>
        private void HandleWatchList(Session session)
        {
            if (!session.uriContains("anime/watch-list") ||
                !AppSettingsService.Current.ManipulateWL ||
                session.RequestMethod != "POST")
                return;

            ProcessManipulationRequest(session, "watch-list");
        }

        /// <summary>
        /// Handles POST requests to the <c>anime/follow</c> endpoint when watch-list
        /// manipulation is enabled. Delegates to <see cref="ProcessManipulationRequest"/> to
        /// decrypt the payload, persist the follow/unfollow state locally, and enable response
        /// buffering as needed.
        /// </summary>
        /// <param name="session">The session to inspect and potentially modify.</param>
        private void HandleFollow(Session session)
        {
            if (!session.uriContains("anime/follow") ||
                !AppSettingsService.Current.ManipulateWL ||
                session.RequestMethod != "POST")
                return;

            ProcessManipulationRequest(session, "follow");
        }

        /// <summary>
        /// Handles POST requests to the <c>anime/favorite</c> endpoint when watch-list
        /// manipulation is enabled. Delegates to <see cref="ProcessManipulationRequest"/> to
        /// decrypt the payload, persist the add/remove favorite state locally, and enable
        /// response buffering as needed.
        /// </summary>
        /// <param name="session">The session to inspect and potentially modify.</param>
        private void HandleFavorite(Session session)
        {
            if (!session.uriContains("anime/favorite") ||
                !AppSettingsService.Current.ManipulateWL ||
                session.RequestMethod != "POST")
                return;

            ProcessManipulationRequest(session, "favorite");
        }

        /// <summary>
        /// Core logic for handling watch-list, follow, and favorite manipulation requests.
        /// Reads the raw request body, extracts and decrypts the encrypted <c>d</c> field using
        /// <see cref="CryptoHelper.Decrypt"/>, then parses the anime ID and transaction type
        /// (<c>"add"</c> or <c>"delete"</c>) from the decrypted JSON payload.
        /// <para>
        /// The transaction value is attached to the session as a custom <c>islem</c> header so
        /// that the <see cref="ResponseInterceptor"/> can later determine which success message
        /// to inject without having to re-parse the request body.
        /// </para>
        /// <para>
        /// For <c>"add"</c> transactions the corresponding repository method is called, and
        /// response buffering is enabled only if the repository reports a successful insert
        /// (e.g. the entry did not already exist). For <c>"delete"</c> transactions the entry
        /// is removed unconditionally and response buffering is always enabled.
        /// </para>
        /// </summary>
        /// <param name="session">The session whose request body will be decrypted and processed.</param>
        /// <param name="type">
        /// The list type being manipulated. Accepted values are <c>"watch-list"</c>,
        /// <c>"follow"</c>, and <c>"favorite"</c>.
        /// </param>
        private void ProcessManipulationRequest(Session session, string type)
        {
            var body = session.GetRequestBodyAsString();
            var decrypted = CryptoHelper.Decrypt(
                JObject.Parse(body)["d"].Value<string>(),
                Constants.AniziumEncryptionKey);

            if (decrypted == null) return;

            var jobj = JObject.Parse(decrypted);
            var animeId = jobj["id"].Value<string>();
            var transaction = jobj["transaction"].Value<string>();

            session.RequestHeaders.Add("islem", transaction);

            if (transaction == "add")
            {
                bool success = false;
                switch (type)
                {
                    case "watch-list": success = _repository.AddWatchList(animeId); break;
                    case "follow": success = _repository.AddFollow(animeId); break;
                    case "favorite": success = _repository.AddFavorite(animeId); break;
                }
                if (success)
                    session.bBufferResponse = true;
            }
            else if (transaction == "delete")
            {
                switch (type)
                {
                    case "watch-list": _repository.RemoveWatchList(animeId); break;
                    case "follow": _repository.RemoveFollow(animeId); break;
                    case "favorite": _repository.RemoveFavorite(animeId); break;
                }
                session.bBufferResponse = true;
            }
        }

        /// <summary>
        /// Enables response buffering for paginated watch-list and favorite-list page requests
        /// when watch-list manipulation is active. Buffering is required so that the
        /// <see cref="ResponseInterceptor"/> can replace the server's response with locally
        /// stored data before it reaches the client.
        /// </summary>
        /// <param name="session">The session to inspect and potentially modify.</param>
        private void HandlePages(Session session)
        {
            if (!AppSettingsService.Current.ManipulateWL) return;

            if (session.uriContains("page/watch-list") ||
                session.uriContains("page/favorite-list"))
            {
                session.bBufferResponse = true;
            }
        }

        /// <summary>
        /// Handles requests to the <c>anime/user-details</c> endpoint when watch-list
        /// manipulation is enabled. Strips the <c>Accept-Encoding</c> header to prevent
        /// a compressed response body, and enables response buffering so the
        /// <see cref="ResponseInterceptor"/> can override the watch-list, follow, and favorite
        /// flags with locally stored state.
        /// </summary>
        /// <param name="session">The session to inspect and potentially modify.</param>
        private void HandleUserDetails(Session session)
        {
            if (!session.uriContains("anime/user-details") ||
                !AppSettingsService.Current.ManipulateWL)
                return;

            RemoveAcceptEncoding(session);
            session.bBufferResponse = true;
        }

        /// <summary>
        /// Removes the <c>Accept-Encoding</c> header from the request (case-insensitive) if it
        /// is present. This forces the server to return a plain, uncompressed response body,
        /// which is necessary before the response interceptor attempts to parse and modify the
        /// raw JSON string.
        /// </summary>
        /// <param name="session">The session whose request headers will be modified.</param>
        private static void RemoveAcceptEncoding(Session session)
        {
            if (session.RequestHeaders.Any(x =>
                x.Name.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase)))
            {
                session.RequestHeaders.Remove("Accept-Encoding");
            }
        }
    }
}
