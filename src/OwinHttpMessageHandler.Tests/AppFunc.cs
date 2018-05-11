namespace System.Net.Http
{
    using System.Threading.Tasks;

    using AppFunc = System.Func<System.Collections.Generic.IDictionary<string, object>, System.Threading.Tasks.Task>;

    internal static class AppFuncHelpers
    {
        internal static readonly AppFunc NoopAppFunc = _ => Task.FromResult(0);
    }
}