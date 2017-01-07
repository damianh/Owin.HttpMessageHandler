namespace System.Net.Http
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http.Headers;
    using System.Threading.Tasks;
    using Microsoft.Owin;
    using Shouldly;
    using Xunit;

    using AppFunc = System.Func<
        Collections.Generic.IDictionary<string, object>,
        System.Threading.Tasks.Task>;

    public class AutoRedirectTests
    {
        private readonly OwinHttpMessageHandler _handler;

        public AutoRedirectTests()
        {
            var responders = new Dictionary<string, Action<IOwinContext>>
            {
                { "/redirect-301-absolute", context =>
                    {
                        context.Response.StatusCode = 301;
                        context.Response.ReasonPhrase = "Moved Permanently";
                        context.Response.Headers.Add("Location", new [] { "http://localhost/redirect" });
                    }
                },
                { "/redirect-301-absolute-setcookie", context =>
                    {
                        context.Response.StatusCode = 302;
                        context.Response.ReasonPhrase = "Moved Permanently";
                        context.Response.Headers.Add("Location", new [] { "http://localhost/redirect" });
                        context.Response.Headers.Add("Set-Cookie", new []{ "foo=bar"});
                    }
                },
                { "/redirect-302-absolute", context =>
                    {
                        context.Response.StatusCode = 302;
                        context.Response.ReasonPhrase = "Found";
                        context.Response.Headers.Add("Location", new [] { "http://localhost/redirect" });
                    }
                },
                { "/redirect-302-relative", context =>
                    {
                        context.Response.StatusCode = 302;
                        context.Response.ReasonPhrase = "Found";
                        context.Response.Headers.Add("Location", new [] { "redirect" });
                    }
                },
                { "/redirect-302-relative-setcookie", context =>
                    {
                        context.Response.StatusCode = 302;
                        context.Response.ReasonPhrase = "Found";
                        context.Response.Headers.Add("Location", new [] { "redirect" });
                        context.Response.Headers.Add("Set-Cookie", new []{ "foo=bar"});
                    }
                },
                { "/redirect-303-absolute", context =>
                    {
                        context.Response.StatusCode = 303;
                        context.Response.ReasonPhrase = "See Other";
                        context.Response.Headers.Add("Location", new [] { "http://localhost/redirect" });
                    }
                },
                { "/redirect-307-absolute", context =>
                    {
                        context.Response.StatusCode = 307;
                        context.Response.ReasonPhrase = "Temporary Redirect";
                        context.Response.Headers.Add("Location", new [] { "http://localhost/redirect" });
                    }
                },
                { "/redirect-loop", context =>
                    {
                        context.Response.StatusCode = 302;
                        context.Response.ReasonPhrase = "Found";
                        context.Response.Headers.Add("Location", new[] { "http://localhost/redirect-loop" });
                    }
                },
                {
                    "/redirect", context =>
                    {
                        context.Response.StatusCode = 200;
                    }
                }
            };
            AppFunc appFunc = env =>
            {
                var context = new OwinContext(env);
                responders[context.Request.Path.Value](context);
                return Task.FromResult((object)null);
            };
            _handler = new OwinHttpMessageHandler(appFunc)
            {
                AllowAutoRedirect = true
            };
        }

        [Theory]
        [InlineData(301)]
        [InlineData(302)]
        [InlineData(303)]
        [InlineData(307)]        
        public async Task Can_auto_redirect_with_absolute_location(int code)
        {
            using (var client = new HttpClient(_handler)
            {
                BaseAddress = new Uri("http://localhost")
            })
            {
                var response = await client.GetAsync($"/redirect-{code}-absolute");

                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                response.RequestMessage.RequestUri.AbsoluteUri.ShouldBe("http://localhost/redirect");
            }
        }

        [Theory]
        [InlineData("Accept", "application/json")]
        [InlineData("Accept-Charset", "utf-8")]
        [InlineData("Accept-Encoding", "gzip, deflate")]
        [InlineData("Cache-Control", "no-cache")]
        [InlineData("User-Agent", "Mozilla/5.0 (X11; Linux x86_64; rv:12.0) Gecko/20100101 Firefox/21.0")]
        public async Task On_redirect_retains_request_headers(string header, string value)
        {
            using (var client = new HttpClient(_handler)
            {
                BaseAddress = new Uri("http://localhost")
            })
            {
                client.DefaultRequestHeaders.Add(header, value);
                var response = await client.GetAsync("/redirect-301-absolute");

                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                response.RequestMessage.Headers.GetValues(header).ShouldBe(client.DefaultRequestHeaders.GetValues(header));
            }
        }

        [Fact]
        public async Task On_redirect_strips_authorization_header()
        {
            using (var client = new HttpClient(_handler)
            {
                BaseAddress = new Uri("http://localhost")
            })
            {
                client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse("foo");
                var response = await client.GetAsync("/redirect-301-absolute");

                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                response.RequestMessage.Headers.Authorization.ShouldBeNull();
            }
        }

        [Fact]
        public async Task Does_not_redirect_on_POST_and_307()
        {
            using (var client = new HttpClient(_handler)
            {
                BaseAddress = new Uri("http://localhost")
            })
            {
                var response = await client.PostAsync("/redirect-307-absolute", new StringContent("the-body"));

                response.StatusCode.ShouldBe(HttpStatusCode.TemporaryRedirect);
                response.RequestMessage.RequestUri.AbsoluteUri.ShouldBe("http://localhost/redirect-307-absolute");
            }
        }

        [Fact]
        public async Task Keeps_method_on_a_307()
        {
            using (var client = new HttpClient(_handler)
            {
                BaseAddress = new Uri("http://localhost")
            })
            {
                var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/redirect-307-absolute"));

                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                response.RequestMessage.RequestUri.AbsoluteUri.ShouldBe("http://localhost/redirect");
                response.RequestMessage.Method.ShouldBe(HttpMethod.Head);
            }
        }

        [Fact]
        public async Task Can_auto_redirect_with_relative_location()
        {
            using (var client = new HttpClient(_handler)
            {
                BaseAddress = new Uri("http://localhost")
            })
            {
                var response = await client.GetAsync("/redirect-302-relative");

                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                response.RequestMessage.RequestUri.AbsoluteUri.ShouldBe("http://localhost/redirect");
            }
        }

        [Fact]
        public async Task When_caught_in_a_redirect_loop_should_throw()
        {
            using (var client = new HttpClient(_handler)
            {
                BaseAddress = new Uri("http://localhost")
            })
            {
                Func<Task> act = () => client.GetAsync("/redirect-loop");

                var exception = await act.ShouldThrowAsync<InvalidOperationException>();

                exception.Message.ShouldContain("Limit = 20");
            }
        }

        [Fact]
        public async Task Can_set_redirect_limit()
        {
            _handler.AutoRedirectLimit = 10;
            using (var client = new HttpClient(_handler)
            {
                BaseAddress = new Uri("http://localhost")
            })
            {
                Func<Task> act = () => client.GetAsync("/redirect-loop");

                var exception = await act.ShouldThrowAsync<InvalidOperationException>();
                
                exception.Message.ShouldContain("Limit = 10");
            }
        }

        [Theory]
        [InlineData("/redirect-301-absolute-setcookie")]
        [InlineData("/redirect-302-relative-setcookie")]
        public async Task Should_set_cookie_on_redirect(string path)
        {
            _handler.UseCookies = true;
            using (var client = new HttpClient(_handler)
            {
                BaseAddress = new Uri("http://localhost")
            })
            {
                var response = await client.GetAsync(path);
                response.RequestMessage.Headers.GetValues("Cookie").Single().ShouldBe("foo=bar");
            }
        }
    }
}