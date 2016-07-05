namespace System.Net.Http
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Shouldly;
    using Xunit;

    using AppFunc = System.Func<
        Collections.Generic.IDictionary<string, object>,
        System.Threading.Tasks.Task>;
    using LibOwin;

    public class AutoRedirectTests
    {
        private readonly OwinHttpMessageHandler _handler;

        public AutoRedirectTests()
        {
            var responders = new Dictionary<string, Action<IOwinContext>>
            {
                { "/redirect-absolute-302", context =>
                    {
                        context.Response.StatusCode = 302;
                        context.Response.ReasonPhrase = "Found";
                        context.Response.Headers.Add("Location", new [] { "http://localhost/redirect" });
                    }
                },
                { "/redirect-relative", context =>
                    {
                        context.Response.StatusCode = 302;
                        context.Response.ReasonPhrase = "Found";
                        context.Response.Headers.Add("Location", new [] { "redirect" });
                    }
                },
                { "/redirect-absolute-301", context =>
                    {
                        context.Response.StatusCode = 301;
                        context.Response.ReasonPhrase = "Moved Permanently";
                        context.Response.Headers.Add("Location", new [] { "http://localhost/redirect" });
                    }
                },                
                { "/redirect-absolute-303", context =>
                    {
                        context.Response.StatusCode = 303;
                        context.Response.ReasonPhrase = "See Other";
                        context.Response.Headers.Add("Location", new [] { "http://localhost/redirect" });
                    }
                },
                { "/redirect-absolute-307", context =>
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
                { "/redirect", context => context.Response.StatusCode = 200 }
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
                var response = await client.GetAsync("/redirect-absolute-"+code);

                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                response.RequestMessage.RequestUri.AbsoluteUri.ShouldBe("http://localhost/redirect");
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
                var response = await client.PostAsync("/redirect-absolute-307", new StringContent("the-body"));

                response.StatusCode.ShouldBe(HttpStatusCode.TemporaryRedirect);
                response.RequestMessage.RequestUri.AbsoluteUri.ShouldBe("http://localhost/redirect-absolute-307");
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
                var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/redirect-absolute-307"));

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
                var response = await client.GetAsync("/redirect-relative");

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
    }
}