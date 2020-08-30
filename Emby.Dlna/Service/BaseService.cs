#pragma warning disable CS1591

using Emby.Dlna.Eventing;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Emby.Dlna.Service
{
    public class BaseService : IDlnaEventManager
    {
        protected BaseService(ILogger<BaseService> logger, IHttpClient httpClient)
        {
            Logger = logger;
            HttpClient = httpClient;

            EventManager = new DlnaEventManager(logger, HttpClient);
        }

        protected IDlnaEventManager EventManager { get; }

        protected IHttpClient HttpClient { get; }

        protected ILogger Logger { get; }

        public EventSubscriptionResponse CancelEventSubscription(string subscriptionId)
        {
            return EventManager.CancelEventSubscription(subscriptionId);
        }

        public EventSubscriptionResponse RenewEventSubscription(string subscriptionId, string notificationType, string timeoutString, string callbackUrl)
        {
            return EventManager.RenewEventSubscription(subscriptionId, notificationType, timeoutString, callbackUrl);
        }

        public EventSubscriptionResponse CreateEventSubscription(string notificationType, string timeoutString, string callbackUrl)
        {
            return EventManager.CreateEventSubscription(notificationType, timeoutString, callbackUrl);
        }
    }
}
