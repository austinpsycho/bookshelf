using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Http;

namespace NzbDrone.Core.MetadataSource.Goodreads
{
    public interface IGoodreadsSearchProxy
    {
        public List<SearchJsonResource> Search(string query);
    }

    public class GoodreadsSearchProxy : IGoodreadsSearchProxy
    {
        private const int MaxRetries = 2;
        private const int MaxBackoffSeconds = 5;
        private const int DefaultBackoffSeconds = 5;

        private readonly ICachedHttpResponseService _cachedHttpClient;
        private readonly IMetadataRequestBuilder _metadataRequestBuilder;
        private readonly Logger _logger;

        public GoodreadsSearchProxy(ICachedHttpResponseService cachedHttpClient,
            IMetadataRequestBuilder metadataRequestBuilder,
            Logger logger)
        {
            _cachedHttpClient = cachedHttpClient;
            _metadataRequestBuilder = metadataRequestBuilder;
            _logger = logger;
        }

        public List<SearchJsonResource> Search(string query)
        {
            try
            {
                for (var attempt = 0; ; attempt++)
                {
                    var httpRequest = _metadataRequestBuilder.GetRequestBuilder().Create()
                        .SetSegment("route", "search")
                        .AddQueryParam("q", query)
                        .Build();

                    // handle the backoff ourselves so a rate limited metadata server
                    // doesn't look like a search that returned nothing
                    httpRequest.SuppressHttpErrorStatusCodes = new[] { HttpStatusCode.TooManyRequests };

                    var response = _cachedHttpClient.Get(httpRequest, false, TimeSpan.FromDays(5));

                    if (response.StatusCode != HttpStatusCode.TooManyRequests)
                    {
                        return new HttpResponse<List<SearchJsonResource>>(response).Resource;
                    }

                    var backoff = GetRetryAfter(response);

                    // a search is interactive, so don't sit on the request thread
                    // waiting out a long cooldown
                    if (attempt >= MaxRetries || backoff > MaxBackoffSeconds)
                    {
                        throw new GoodreadsException("Search for '{0}' failed. Metadata source is rate limited, try again in {1}s.", query, backoff);
                    }

                    _logger.Info("Metadata server returned 429, backing off for {0}s", backoff);

                    Thread.Sleep(TimeSpan.FromSeconds(backoff));
                }
            }
            catch (GoodreadsException)
            {
                throw;
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex);
                throw new GoodreadsException("Search for '{0}' failed. Unable to communicate with metadata source.", ex, query);
            }
            catch (WebException ex)
            {
                _logger.Warn(ex);
                throw new GoodreadsException("Search for '{0}' failed. Unable to communicate with metadata source.", ex, query, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex);
                throw new GoodreadsException("Search for '{0}' failed. Invalid response received from metadata source.", ex, query);
            }
        }

        private static int GetRetryAfter(HttpResponse response)
        {
            if (response.Headers.ContainsKey("Retry-After") &&
                int.TryParse(response.Headers["Retry-After"], out var seconds))
            {
                return seconds;
            }

            return DefaultBackoffSeconds;
        }
    }
}
