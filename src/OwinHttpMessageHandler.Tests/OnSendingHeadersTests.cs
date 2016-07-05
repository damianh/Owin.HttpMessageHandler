namespace System.Net.Http
{
    using System.Linq;
    using System.Threading.Tasks;
    using Shouldly;
    using Xunit;
    using AppFunc = Func<Collections.Generic.IDictionary<string, object>, Threading.Tasks.Task>;
    using LibOwin;
#if NET452
    using Microsoft.Owin.Hosting;
    using Microsoft.Owin.Testing;
    using Nowin;
    using Owin;
#endif
    public class OnSendingHeadersTests
    {
        const string CookieName1 = "testcookie1";
        const string CookieName2 = "testcookie2";
        private readonly Uri _uri = new Uri("http://localhost:8888/");
        private readonly AppFunc _appFunc;

        public OnSendingHeadersTests()
        {
            AppFunc inner = async env =>
            {
                var context = new OwinContext(env);
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("Test");
            };

            AppFunc inner2 = async env =>
            {
                var context = new OwinContext(env);
                context.Response.OnSendingHeaders(_ =>
                {
                    if (context.Response.StatusCode ==  404)
                    {
                        context.Response.Cookies.Append(CookieName1, "c1");
                    }
                }, null);
                await inner(env);
            };

            _appFunc = async env =>
            {
                var context = new OwinContext(env);
                context.Response.OnSendingHeaders(_ =>
                {
                    if (context.Response.Headers.ContainsKey("Set-Cookie"))
                    {
                        context.Response.Cookies.Append(CookieName2, "c2");
                    }
                }, null);
                await inner2(env);
            };
        }

        [Fact]
        public async Task Using_OwinHttpMessageHandler_then_should_have_2_cookies()
        {
            var handler = new OwinHttpMessageHandler(_appFunc)
            {
                UseCookies = true
            };

            using (var client = new HttpClient(handler)
                {
                    BaseAddress = _uri
                })
            {
                var response = await client.GetAsync(_uri);

                var setCookies = response.Headers.GetValues("Set-Cookie");

                setCookies.Count().ShouldBe(2);
            }
        }

#if NET452
        [Fact]
        public async Task Using_nowin_then_should_have_2_cookies()
        {
            using (var server = ServerBuilder
                .New()
                .SetEndPoint(new IPEndPoint(IPAddress.Any, _uri.Port))
                .SetOwinApp(_appFunc)
                .Build())
            {
                server.Start();

                var handler = new HttpClientHandler
                {
                    UseCookies = true
                };
                using (var client = new HttpClient(handler)
                {
                    BaseAddress = _uri
                })
                {
                    var response = await client.GetAsync(_uri);

                    response.Headers.GetValues("Set-Cookie")
                        .Count().ShouldBe(2);
                }
            }
        }


        [Fact]
        public async Task Using_HttpListener_then_should_have_2_cookies()
        {
            using(WebApp.Start(_uri.ToString(), a => a.Run(ctx => _appFunc(ctx.Environment))))
            {
                var handler = new HttpClientHandler
                {
                    UseCookies = true
                };
                using (var client = new HttpClient(handler)
                {
                    BaseAddress = _uri
                })
                {
                    var response = await client.GetAsync(_uri);

                    response.Headers.GetValues("Set-Cookie")
                        .Count()
                        .ShouldBe(2);
                }
            }
        }

        [Fact(Skip = "Will fail because of bug in TestServer")]
        public async Task Using_TestServer_then_should_have_2_cookies()
        {
            var testServer = TestServer.Create(a1 => a1.Run(ctx => _appFunc(ctx.Environment)));
            using (var client = testServer.HttpClient)
            {
                var response = await client.GetAsync(_uri);

                response.Headers.GetValues("Set-Cookie")
                    .Count()
                    .ShouldBe(2);
            }
        }
#endif
    }
}