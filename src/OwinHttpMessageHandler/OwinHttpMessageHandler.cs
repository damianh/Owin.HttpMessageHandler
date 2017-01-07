namespace System.Net.Http
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http.LibOwin;
    using System.Threading;
    using System.Threading.Tasks;

    using AppFunc = System.Func<
        System.Collections.Generic.IDictionary<string, object>,
        System.Threading.Tasks.Task>;

    using MidFunc = System.Func<System.Func<System.Collections.Generic.IDictionary<string, object>,
            System.Threading.Tasks.Task
        >, System.Func<System.Collections.Generic.IDictionary<string, object>,
            System.Threading.Tasks.Task
        >
    >;

    /// <summary>
    /// Represents an HttpMessageHanlder that can invoke a request directly against an OWIN pipeline (an 'AppFunc').
    /// </summary>
    public class OwinHttpMessageHandler : HttpMessageHandler
    {
        /// <summary>
        /// The default number of redirects that will be auto followed.
        /// </summary>
        public const int DefaultAutoRedirectLimit = 20;
        private readonly AppFunc _appFunc;
        private CookieContainer _cookieContainer = new CookieContainer();
        private bool _useCookies;
        private bool _operationStarted; //popsicle immutability
        private bool _disposed;
        private bool _allowAutoRedirect;
        private int _autoRedirectLimit = DefaultAutoRedirectLimit;

        /// <summary>
        /// Initializes a new instance of the <see cref="OwinHttpMessageHandler"/> class.
        /// </summary>
        /// <param name="midFunc">An OWIN middleware function that will be terminated with a 404 Not Found.</param>
        /// <exception cref="System.ArgumentNullException">midFunc</exception>
        public OwinHttpMessageHandler(MidFunc midFunc)
        {
            if (midFunc == null)
            {
                throw new ArgumentNullException(nameof(midFunc));
            }

            _appFunc = midFunc(env =>
            {
                var context = new OwinContext(env);
                context.Response.StatusCode = 404;
                context.Response.ReasonPhrase = "Not Found";
                return Task.FromResult(0);
            });
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OwinHttpMessageHandler"/> class.
        /// </summary>
        /// <param name="appFunc">An OWIN application function.</param>
        /// <exception cref="System.ArgumentNullException">appFunc</exception>
        public OwinHttpMessageHandler(AppFunc appFunc)
        {
            if (appFunc == null)
            {
                throw new ArgumentNullException(nameof(appFunc));
            }

            _appFunc = appFunc;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _disposed = true;
        }

        /// <summary>
        ///     Gets or sets a value that indicates whether the handler uses the 
        ///     <see cref="P:System.Net.Http.HttpClientHandler.CookieContainer"/> property
        ///     to store server cookies and uses these cookies when sending requests.
        /// </summary>
        /// <returns>
        ///     Returns <see cref="T:System.Boolean"/>.true if the if the handler supports
        ///     uses the <see cref="P:System.Net.Http.HttpClientHandler.CookieContainer"/> property
        ///     to store server cookies and uses these cookies when sending requests; otherwise false.
        ///     The default value is true.
        /// </returns>
        public bool UseCookies
        {
            get { return _useCookies; }
            set
            {
                CheckDisposedOrStarted();
                _useCookies = value;
            }
        }

        /// <summary>
        ///     Gets or sets a value that indicates whether the handler should follow redirection responses.
        ///     Authorization headers are stripped on redirect.
        /// </summary>
        /// <returns>
        ///     Returns <see cref="T:System.Boolean"/>.true if the if the handler should follow redirection
        ///     responses; otherwise false. The default value is true.
        /// </returns>
        public bool AllowAutoRedirect
        {
            get { return _allowAutoRedirect; }
            set
            {
                CheckDisposedOrStarted();
                _allowAutoRedirect = value;
            }
        }

        /// <summary>
        ///     Gets or sets the automatic redirect limit. Default is <see cref="DefaultAutoRedirectLimit"/>.
        /// </summary>
        /// <value>
        ///     The automatic redirect limit.
        /// </value>
        public int AutoRedirectLimit
        {
            get { return _autoRedirectLimit; }
            set
            {
                CheckDisposedOrStarted();
                if(value < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Auto redirect limit must be greater than or equal to one.");
                }
                _autoRedirectLimit = value;
            }
        }

        /// <summary>
        ///     Gets or sets the cookie container used to store server cookies by the handler.
        /// </summary>
        /// <returns>
        ///     Returns <see cref="T:System.Net.CookieContainer"/>.The cookie container used to store
        ///     server cookies by the handler.
        /// </returns>
        public CookieContainer CookieContainer 
        {
            get { return _cookieContainer; }
            set
            {
                CheckDisposedOrStarted();
                _cookieContainer = value;
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            _operationStarted = true;

            var response = await SendInternalAsync(request, cancellationToken).NotOnCapturedContext();

            int redirectCount = 0;
            int statusCode = (int)response.StatusCode;

            while (_allowAutoRedirect && 
                (IsRedirectToGet(statusCode) || 
                (IsBodylessRequest(request) && statusCode == 307)))
            {
                if(redirectCount >= _autoRedirectLimit)
                {
                    throw new InvalidOperationException($"Too many redirects. Limit = {redirectCount}");
                }
                var location = response.Headers.Location;
                if (!location.IsAbsoluteUri)
                {
                    location = new Uri(response.RequestMessage.RequestUri, location);
                }

                var redirectMethod = IsRedirectToGet(statusCode) ? HttpMethod.Get : request.Method;
                request.RequestUri = location;
                request.Method = redirectMethod;
                request.Headers.Authorization = null;
                CheckSetCookie(request, response);

                response = await SendInternalAsync(request, cancellationToken).NotOnCapturedContext();

                statusCode = (int) response.StatusCode;
                redirectCount++;
            }
            return response;
        }

        private static bool IsRedirectToGet(int code)
        {            
            return code == 301 || code == 302 || code == 303; 
        }

        private static bool IsBodylessRequest(HttpRequestMessage req)
        {
            return req.Method == HttpMethod.Get 
                || req.Method == HttpMethod.Head 
                || req.Method == HttpMethod.Delete;
        }

        private async Task<HttpResponseMessage> SendInternalAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_useCookies)
            {
                string cookieHeader = _cookieContainer.GetCookieHeader(request.RequestUri);
                if (!string.IsNullOrEmpty(cookieHeader))
                {
                    request.Headers.Add("Cookie", cookieHeader);
                }
            }

            var state = new RequestState(request, cancellationToken);

            HttpContent requestContent = request.Content ?? new StreamContent(Stream.Null);
            Stream body = await requestContent.ReadAsStreamAsync().NotOnCapturedContext();
            if (body.CanSeek)
            {
                // This body may have been consumed before, rewind it.
                body.Seek(0, SeekOrigin.Begin);
            }
            state.OwinContext.Request.Body = body;
            CancellationTokenRegistration registration = cancellationToken.Register(state.Abort);

            // Async offload, don't let the test code block the caller.
            Task offload = Task.Run(async () =>
            {
                try
                {
                    await _appFunc(state.Environment).NotOnCapturedContext();
                    state.CompleteResponse();
                }
                catch (Exception ex)
                {
                    state.Abort(ex);
                }
                finally
                {
                    registration.Dispose();
                    state.Dispose();
                }
            }, cancellationToken);

            HttpResponseMessage response = await state.ResponseTask.NotOnCapturedContext();
            CheckSetCookie(request, response);
            return response;
        }

        private void CheckSetCookie(HttpRequestMessage request, HttpResponseMessage response)
        {
            if(_useCookies && response.Headers.Contains("Set-Cookie"))
            {
                var cookieHeader = string.Join(",", response.Headers.GetValues("Set-Cookie"));
                _cookieContainer.SetCookies(request.RequestUri, cookieHeader);
            }
        }

        private void CheckDisposedOrStarted()
        {
            CheckDisposed();
            if (_operationStarted)
            {
                throw new InvalidOperationException("Handler has started operations");
            }
        }

        private void CheckDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        private class RequestState : IDisposable
        {
            private readonly HttpRequestMessage _request;
            private Action _sendingHeaders;
            private readonly TaskCompletionSource<HttpResponseMessage> _responseTcs;
            private readonly ResponseStream _responseStream;

            internal RequestState(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                _request = request;
                _responseTcs = new TaskCompletionSource<HttpResponseMessage>();
                _sendingHeaders = () => { };

                request.Headers.Host = request.RequestUri.IsDefaultPort 
                    ? request.RequestUri.Host
                    : request.RequestUri.GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped);

                OwinContext = new OwinContext();
                OwinContext.Set("owin.Version", "1.0");
                IOwinRequest owinRequest = OwinContext.Request;
                owinRequest.Protocol = "HTTP/" + request.Version.ToString(2);
                owinRequest.Scheme = request.RequestUri.Scheme;
                owinRequest.Method = request.Method.ToString();
                owinRequest.Path = PathString.FromUriComponent(request.RequestUri);
                owinRequest.PathBase = PathString.Empty;
                owinRequest.QueryString = QueryString.FromUriComponent(request.RequestUri);
                owinRequest.CallCancelled = cancellationToken;
                owinRequest.Set<Action<Action<object>, object>>("server.OnSendingHeaders", (callback, state) =>
                {
                    var prior = _sendingHeaders;
                    _sendingHeaders = () =>
                    {
                        callback(state);
                        prior();
                    };
                });

                foreach (var header in request.Headers)
                {
                    owinRequest.Headers.AppendValues(header.Key, header.Value.ToArray());
                }
                if (request.Content != null)
                {
                    // Need to touch the ContentLength property for it to be calculated and added
                    // to the request.Content.Headers collection.
                    var d = request.Content.Headers.ContentLength;

                    foreach (var header in request.Content.Headers)
                    {
                        owinRequest.Headers.AppendValues(header.Key, header.Value.ToArray());
                    }
                }

                _responseStream = new ResponseStream(CompleteResponse);
                OwinContext.Response.Body = _responseStream;
                OwinContext.Response.StatusCode = 200;
            }

            public IOwinContext OwinContext { get; private set; }

            public IDictionary<string, object> Environment
            {
                get { return OwinContext.Environment; }
            }

            public Task<HttpResponseMessage> ResponseTask
            {
                get { return _responseTcs.Task; }
            }

            internal void CompleteResponse()
            {
                if (!_responseTcs.Task.IsCompleted)
                {
                    HttpResponseMessage response = GenerateResponse();
                    // Dispatch, as TrySetResult will synchronously execute the waiters callback and block our Write.
                    Task.Factory.StartNew(() => _responseTcs.TrySetResult(response));
                }
            }

            internal HttpResponseMessage GenerateResponse()
            {
                _sendingHeaders();

                var response = new HttpResponseMessage
                {
                    StatusCode = (HttpStatusCode) OwinContext.Response.StatusCode,
                    ReasonPhrase = OwinContext.Response.ReasonPhrase,
                    RequestMessage = _request,
                    Content = new StreamContent(_responseStream)
                };
                // response.Version = owinResponse.Protocol;

                foreach (var header in OwinContext.Response.Headers)
                {
                    if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    {
                        response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }
                return response;
            }

            internal void Abort()
            {
                Abort(new OperationCanceledException());
            }

            internal void Abort(Exception exception)
            {
                _responseStream.Abort(exception);
                _responseTcs.TrySetException(exception);
            }

            public void Dispose()
            {
                _responseStream.Dispose();
                // Do not dispose the request, that will be disposed by the caller.
            }
        }
    }
}