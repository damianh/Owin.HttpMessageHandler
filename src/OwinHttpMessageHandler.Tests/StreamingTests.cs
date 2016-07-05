namespace System.Net.Http
{
    using LibOwin;
    using System.Threading.Tasks;
    using Xunit;

    using AppFunc = System.Func<Collections.Generic.IDictionary<string, object>, System.Threading.Tasks.Task>;

    public class StreamingTests
    {
        [Fact]
        public async Task Should_complete_when_headers_are_flushed()
        {
            var tcs = new TaskCompletionSource<int>(0);
            AppFunc appFunc = env =>
            {
                var context = new OwinContext(env);
                context.Response.StatusCode = 200;
                context.Response.Write("Blurg"); // Writing to response stream should flush the headers.
                return tcs.Task;
            };

            var httpClient = new HttpClient(new OwinHttpMessageHandler(appFunc));

            var responseTask = httpClient.GetAsync("http://example.com", HttpCompletionOption.ResponseHeadersRead);
            if (await Task.WhenAny(responseTask, Task.Delay(5000)) != responseTask)
            {
                throw new TimeoutException("responseTask did not complete");
            }
            tcs.SetResult(0);
        }
    }
}