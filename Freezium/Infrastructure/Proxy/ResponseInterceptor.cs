using System;
using System.Linq;
using Fiddler;
using Freezium.Core;
using Freezium.Core.Interfaces;
using Freezium.Helpers;
using Freezium.Services;
using Newtonsoft.Json.Linq;

namespace Freezium.Infrastructure.Proxy
{
    /// <summary>
    /// Handles the FiddlerCore BeforeResponse event.
    /// Manipulates response bodies to inject premium, watchlist, follow, and favorite data.
    /// </summary>
    public class ResponseInterceptor
    {
        private readonly IAnimeRepository _repository;

        public event Action<string> LogMessage;

        /// <summary>
        /// Initializes a new instance of <see cref="ResponseInterceptor"/> with the given anime
        /// repository, which is used to supply locally stored watch-list, follow, and favorite
        /// data when overriding API responses.
        /// </summary>
        /// <param name="repository">The repository used to read local anime list data.</param>
        public ResponseInterceptor(IAnimeRepository repository)
        {
            _repository = repository;
        }

        /// <summary>
        /// Entry point for the FiddlerCore <c>BeforeResponse</c> event. Filters out sessions
        /// that do not originate from the configured API host, then dispatches the session to
        /// each specialized response handler in sequence. Any unhandled exception is caught and
        /// reported via the <see cref="LogMessage"/> event so that a single bad response cannot
        /// crash the proxy pipeline.
        /// </summary>
        /// <param name="session">The FiddlerCore session representing the intercepted HTTP(S) response.</param>
        public void Handle(Session session)
        {
            try
            {
                if (!session.fullUrl.Contains(Constants.TargetApiHost))
                    return;

                HandlePremiumInjection(session);
                HandleWatchListResponse(session);
                HandleFollowResponse(session);
                HandleFavoriteResponse(session);
                HandlePageResponses(session);
                HandleUserDetailsResponse(session);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"An Error Occurred, Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Intercepts a successful <c>user/get</c> response and injects premium subscription
        /// data into the response body before it reaches the client. Specifically:
        /// <list type="bullet">
        ///   <item><description>Sets <c>subscription</c> to <c>true</c>.</description></item>
        ///   <item><description>Replaces the <c>premium</c> object with a newly created active
        ///   subscription valid for 30 days (2,592,000,000 ms).</description></item>
        ///   <item><description>Replaces <c>premium_plan</c> with a standard plan object.</description></item>
        ///   <item><description>Sets <c>infinity</c> and <c>staff</c> flags to <c>true</c>.</description></item>
        /// </list>
        /// The method is a no-op if the URI does not contain <c>user/get</c>, the HTTP status
        /// code is not 200, or the response JSON does not carry a <c>success: true</c> field.
        /// </summary>
        /// <param name="session">The session whose response body will be decoded and modified.</param>
        private void HandlePremiumInjection(Session session)
        {
            if (!session.uriContains("user/get") || session.responseCode != 200)
                return;

            session.utilDecodeResponse();
            var body = session.GetResponseBodyAsString();
            var jobj = JObject.Parse(body);

            if (jobj["success"]?.Value<bool>() != true)
                return;

            var data = jobj["data"] as JObject;
            data["subscription"] = true;

            // Inject premium information
            data.Remove("premium");
            data.Add("premium", new JObject
            {
                { "created", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                { "time", 2592000000 },
                { "active", true }
            });

            data.Remove("premium_plan");
            data.Add("premium_plan", new JObject
            {
                { "ID", "standart" },
                { "name", "Standart" }
            });

            data["infinity"] = true;
            data["staff"] = true;

            session.utilSetResponseBody(jobj.ToString(Newtonsoft.Json.Formatting.None));
        }

        /// <summary>
        /// Handles responses to POST requests on the <c>anime/watch-list</c> endpoint when
        /// watch-list manipulation is enabled. Replaces the server's response body with a
        /// synthetic success JSON message that reflects whether the anime was added to or
        /// removed from the local watch-list.
        /// </summary>
        /// <param name="session">The session whose response will be inspected and potentially replaced.</param>
        private void HandleWatchListResponse(Session session)
        {
            if (!session.uriContains("anime/watch-list") ||
                !AppSettingsService.Current.ManipulateWL ||
                session.RequestMethod != "POST")
                return;

            RespondToManipulation(session, "Successfully added to the list.", "Successfully removed from the list.");
        }

        /// <summary>
        /// Handles responses to POST requests on the <c>anime/follow</c> endpoint when
        /// watch-list manipulation is enabled. Replaces the server's response body with a
        /// synthetic success JSON message indicating whether the anime was followed or unfollowed.
        /// </summary>
        /// <param name="session">The session whose response will be inspected and potentially replaced.</param>
        private void HandleFollowResponse(Session session)
        {
            if (!session.uriContains("anime/follow") ||
                !AppSettingsService.Current.ManipulateWL ||
                session.RequestMethod != "POST")
                return;

            RespondToManipulation(session, "Successfully followed.", "Successfully unfollowed.");
        }

        /// <summary>
        /// Handles responses to POST requests on the <c>anime/favorite</c> endpoint when
        /// watch-list manipulation is enabled. Unlike the generic
        /// <see cref="RespondToManipulation"/> helper, this method constructs a response that
        /// also includes a <c>total</c> field reflecting the resulting favorite count (1 after
        /// an add, 0 after a delete). The transaction type is read from the custom
        /// <c>islem</c> header injected by <see cref="RequestInterceptor"/>.
        /// </summary>
        /// <param name="session">The session whose response will be inspected and potentially replaced.</param>
        private void HandleFavoriteResponse(Session session)
        {
            if (!session.uriContains("anime/favorite") ||
                !AppSettingsService.Current.ManipulateWL ||
                session.RequestMethod != "POST")
                return;

            var islem = GetTransactionHeader(session);
            if (islem == null) return;

            if (islem == "add")
            {
                session.responseCode = 200;
                session.utilSetResponseBody("{\"success\":true,\"total\":1,\"msg\":\"Added to your favorites.\"}");
            }
            else if (islem == "delete")
            {
                session.responseCode = 200;
                session.utilSetResponseBody("{\"success\":true,\"total\":0,\"msg\":\"Removed from your favorites.\"}");
            }
        }

        /// <summary>
        /// Handles paginated list page responses when watch-list manipulation is enabled.
        /// For <c>page/watch-list</c> and <c>page/favorite-list</c> URIs, completely replaces
        /// the server's response body with a JSON object containing the locally stored entries
        /// retrieved from the repository. This ensures the client always renders the local
        /// (potentially modified) dataset rather than whatever the server returned.
        /// </summary>
        /// <param name="session">The session whose response will be inspected and potentially replaced.</param>
        private void HandlePageResponses(Session session)
        {
            if (!AppSettingsService.Current.ManipulateWL) return;

            if (session.uriContains("page/watch-list"))
            {
                var list = _repository.GetWatchList().ToArray();
                var response = new JObject
                {
                    { "success", true },
                    { "data", JArray.FromObject(list) }
                };
                session.responseCode = 200;
                session.utilSetResponseBody(response.ToString(Newtonsoft.Json.Formatting.None));
            }

            if (session.uriContains("page/favorite-list"))
            {
                var list = _repository.GetFavoriteList().ToArray();
                var response = new JObject
                {
                    { "success", true },
                    { "data", JArray.FromObject(list) }
                };
                session.responseCode = 200;
                session.utilSetResponseBody(response.ToString(Newtonsoft.Json.Formatting.None));
            }
        }

        /// <summary>
        /// Handles responses from the <c>anime/user-details</c> endpoint when watch-list
        /// manipulation is enabled. Parses the response body as JSON, extracts the anime ID
        /// from the request URL query string, and overwrites the <c>watch_list</c>,
        /// <c>follow</c>, and <c>favorite</c> boolean fields with the current values stored
        /// in the local repository. This guarantees that the UI reflects local state even
        /// when the server returns outdated or mismatched data.
        /// The method is a no-op if the response body cannot be parsed as a valid JSON object.
        /// </summary>
        /// <param name="session">The session whose response body will be inspected and modified.</param>
        private void HandleUserDetailsResponse(Session session)
        {
            if (!session.uriContains("anime/user-details") ||
                !AppSettingsService.Current.ManipulateWL)
                return;

            var data = session.GetResponseBodyAsString();
            if (!JsonHelper.TryParseJObject(data, out var jobj))
                return;

            var id = System.Web.HttpUtility.ParseQueryString(
                new Uri(session.fullUrl).Query)["id"];

            jobj["watch_list"] = _repository.IsInWatchList(id);
            jobj["follow"] = _repository.IsInFollow(id);
            jobj["favorite"] = _repository.IsInFavorite(id);

            session.utilSetResponseBody(jobj.ToString(Newtonsoft.Json.Formatting.None));
        }

        #region Helpers

        /// <summary>
        /// Shared helper that constructs and injects a synthetic success response for add/delete
        /// manipulation operations. Reads the transaction type from the custom <c>islem</c>
        /// header (set by <see cref="RequestInterceptor"/>) and sets the response body to either
        /// <paramref name="addMsg"/> or <paramref name="deleteMsg"/> wrapped in a
        /// <c>{"success":true,"msg":"..."}</c> JSON envelope. The method is a no-op if the
        /// <c>islem</c> header is absent.
        /// </summary>
        /// <param name="session">The session whose response body will be replaced.</param>
        /// <param name="addMsg">The message text to embed when the transaction type is <c>"add"</c>.</param>
        /// <param name="deleteMsg">The message text to embed when the transaction type is <c>"delete"</c>.</param>
        private static void RespondToManipulation(Session session, string addMsg, string deleteMsg)
        {
            var islem = GetTransactionHeader(session);
            if (islem == null) return;

            if (islem == "add")
            {
                session.responseCode = 200;
                session.utilSetResponseBody($"{{\"success\":true,\"msg\":\"{addMsg}\"}}");
            }
            else if (islem == "delete")
            {
                session.responseCode = 200;
                session.utilSetResponseBody($"{{\"success\":true,\"msg\":\"{deleteMsg}\"}}");
            }
        }

        /// <summary>
        /// Retrieves the value of the custom <c>islem</c> request header that was injected by
        /// <see cref="RequestInterceptor.ProcessManipulationRequest"/> to communicate the
        /// transaction type (<c>"add"</c> or <c>"delete"</c>) to the response interceptor.
        /// Returns <c>null</c> if the header is not present on the session.
        /// </summary>
        /// <param name="session">The session whose request headers are searched.</param>
        /// <returns>The transaction value (<c>"add"</c> or <c>"delete"</c>), or <c>null</c> if the header is missing.</returns>
        private static string GetTransactionHeader(Session session)
        {
            var header = session.RequestHeaders.FirstOrDefault(x => x.Name == "islem");
            return header?.Value;
        }

        #endregion
    }
}
