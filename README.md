:warning: The package is no longer supported. If you have an application and require support, please contact me.

OwinHttpMessageHandler [![Build status](https://ci.appveyor.com/api/projects/status/vf9qrs3cdnar24rf?svg=true)](https://ci.appveyor.com/project/damianh/OwinHttpMessageHandler) [![NuGet Status](http://img.shields.io/nuget/v/OwinHttpMessageHandler.svg?style=flat)](https://www.nuget.org/packages/OwinHttpMessageHandler/)
=====================

An implementation of [System.Net.Http.HttpMessageHandler] that translates an [HttpRequestMessage] into an [OWIN] compatible environment dictionary, calls the supplied AppFunc and translates the result to an [HttpResponseMessage]. This allows you to call an OWIN application / middleware using [HttpClient] without actually hitting the network stack. Useful for [testing] and embedded scenarios.


[Install via nuget].

Using
-

```csharp
var handler = new OwinHttpMessageHandler(appFunc) // Alternatively you can pass in a MidFunc
{
    UseCookies = true, // Will send cookies on subsequent requests. Default is false.
    AllowAutoRedirect = true // The handler will auto follow 301/302
}
var httpClient = new HttpClient(handler)
{
    BaseAddress = new Uri("http://localhost")
}

var response = await httpClient.GetAsync("/");
```

By default, the OWIN environment is defined to look as though the source of the request is local. You can adjust the OWIN environment via a delegate:

```csharp
Func<IDictionary<string, object>, Task> appFunc;
...
var httpClient = new HttpClient(new OwinHttpMessageHandler(appFunc, env =>
{
    env[Constants.ServerRemoteIpAddressKey] ="10.1.1.1";
}));
```

More information on [Http Message Handlers]

Licence : [MIT]

Follow me [@randompunter]

  [System.Net.Http.HttpMessageHandler]: http://msdn.microsoft.com/en-us/library/system.net.http.httpmessagehandler.aspx
  [HttpRequestMessage]: http://msdn.microsoft.com/en-us/library/system.net.http.httprequestmessage.aspx
  [OWIN]: http://owin.org/
  [Install via nuget]: http://www.nuget.org/packages/OwinHttpMessageHandler/
  [HttpResponseMessage]: http://msdn.microsoft.com/en-us/library/system.net.http.httpresponsemessage.aspx
  [testing]: http://dhickey.ie/2015/04/testing-owin-applications-with-httpclient-and-owinhttpmessagehandler/
  [HttpClient]: http://msdn.microsoft.com/en-us/library/system.net.http.httpclient.aspx
  [Http Message Handlers]: http://www.asp.net/web-api/overview/working-with-http/http-message-handlers
  [MIT]: http://opensource.org/licenses/MIT
  [@randompunter]: http://twitter.com/randompunter
