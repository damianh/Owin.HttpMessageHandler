namespace System.Net.Http
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Threading.Tasks;
    using Shouldly;
    using Xunit;

    using AppFunc = System.Func<
        Collections.Generic.IDictionary<string, object>,
        System.Threading.Tasks.Task>;

    using MidFunc = System.Func<System.Func<System.Collections.Generic.IDictionary<string, object>,
            System.Threading.Tasks.Task
        >, System.Func<System.Collections.Generic.IDictionary<string, object>,
            System.Threading.Tasks.Task
        >
    >;
    using LibOwin;

    // ReSharper disable InconsistentNaming
    public class OwinHttpMessageHandlerTests
    {
        private readonly HttpClient _sut;
        private bool _onSendingHeadersCalled;

        public OwinHttpMessageHandlerTests()
        {
            var _responders = new Dictionary<string, Action<IOwinContext>>
            {
                {"/OK", context => context.Response.StatusCode = 200},
                {"/NotFound", context => context.Response.StatusCode = 404},
                {"/greeting", context =>
                    {
                        var form = context.Request.ReadFormAsync().Result;
                        context.Response.Write("Hello " + form["Name"]);
                    }
                }
            };
            AppFunc appFunc = env =>
            {
                var context = new OwinContext(env);
                context.Response.OnSendingHeaders(_ => _onSendingHeadersCalled = true, null);
                _responders[context.Request.Path.Value](context);
                return Task.FromResult((object) null);
            };

            _sut = new HttpClient(new OwinHttpMessageHandler(appFunc) { UseCookies = true });
        }

        [Fact]
        public void When_appfunc_paramater_is_null_Then_should_throw()
        {
            Action act = () => { var handler = new OwinHttpMessageHandler((AppFunc)null); };
            act.ShouldThrow<ArgumentNullException>();
        }

        [Fact]
        public void When_midfunc_paramater_is_null_Then_should_throw()
        {
            Action act = () => { var handler = new OwinHttpMessageHandler((MidFunc)null); };
            act.ShouldThrow<ArgumentNullException>();
        }

        [Fact]
        public async Task Should_get_status_OK()
        {
            (await _sut.GetAsync("http://sample.com/OK")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        [Fact]
        public async Task Should_get_status_NotFound()
        {
            (await _sut.GetAsync("http://sample.com/NotFound")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Should_call_OnSendingHeaders()
        {
            await _sut.GetAsync("http://sample.com/OK");
            _onSendingHeadersCalled.ShouldBeTrue();
        }

        [Fact]
        public async Task Should_handle_form_data()
        {
            HttpResponseMessage response = await _sut.PostAsync("http://sample.com/greeting",
                               new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
                                                         {
                                                             new KeyValuePair<string, string>("Name", "Damian")
                                                         }));
            (await response.Content.ReadAsStringAsync()).ShouldBe("Hello Damian");
        }

        [Fact]
        public async Task When_changing_use_cookies_after_initial_operation_then_should_throw()
        {
            var handler = new OwinHttpMessageHandler(AppFuncHelpers.NoopAppFunc);
            using (var client = new HttpClient(handler))
            {
                await client.GetAsync("http://localhost/");
            }

            Action act = () => { handler.UseCookies = true; };

            act.ShouldThrow<InvalidOperationException>();
        }

        [Fact]
        public void When_changing_use_cookies_after_disposing_then_should_throw()
        {
            var handler = new OwinHttpMessageHandler(_ => Task.FromResult(0));
            handler.Dispose();

            Action act = () => { handler.UseCookies = true; };

            act.ShouldThrow<InvalidOperationException>();
        }

        [Fact]
        public async Task When_changing_cookie_container_after_initial_operation_then_should_throw()
        {
            var handler = new OwinHttpMessageHandler(AppFuncHelpers.NoopAppFunc);
            using (var client = new HttpClient(handler))
            {
                await client.GetAsync("http://localhost/");
            }

            Action act = () => { handler.CookieContainer = new CookieContainer(); };

            act.ShouldThrow<InvalidOperationException>();
        }

        [Fact]
        public void When_changing_cookie_container_after_disposing_then_then_should_throw()
        {
            var handler = new OwinHttpMessageHandler(AppFuncHelpers.NoopAppFunc);
            handler.Dispose();

            Action act = () => { handler.CookieContainer = new CookieContainer(); };

            act.ShouldThrow<InvalidOperationException>();
        }

        [Fact]
        public async Task Environment_should_contain_content_length_header()
        {
            OwinContext owinContext = null;
            var handler = new OwinHttpMessageHandler(env =>
            {
                owinContext = new OwinContext(env);
                return Task.FromResult(0);
            });

            using (var client = new HttpClient(handler))
            {
                var stringContent = new StringContent("hello");
                await client.PostAsync("http://localhost/", stringContent);
            }

            owinContext.Request.Headers.ContainsKey("Content-Length").ShouldBeTrue();
        }
    }
    // ReSharper restore InconsistentNaming
}