using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.Reflection;
using System.Threading.Tasks;
using ReversedServer = System.Object;

namespace eventpiper
{
    /* We need to reverse a bit before ReversedDiagnosticsServer becomes public */
    static class UnofficialDiagnosticsClientApi
    {
        static readonly Type diagClientType = typeof(DiagnosticsClient);
        static readonly Type reversedServerType = diagClientType.Assembly.GetType("Microsoft.Diagnostics.NETCore.Client.ReversedDiagnosticsServer");
        static readonly MethodInfo serverStart = reversedServerType.GetMethod("Start", Array.Empty<Type>());
        static readonly MethodInfo serverAccept = reversedServerType.GetMethod("Accept");
        static readonly MethodInfo disposeAsync = reversedServerType.GetMethod("DisposeAsync");

        static readonly Type ipcEndpointInfoType = diagClientType.Assembly.GetType("Microsoft.Diagnostics.NETCore.Client.IpcEndpointInfo");
        static readonly PropertyInfo ipcEndpointInfoEndpointProperty = ipcEndpointInfoType.GetProperty("Endpoint");
        static readonly PropertyInfo ipcEndpointInfoProcessIdProperty = ipcEndpointInfoType.GetProperty("ProcessId");
        static readonly MethodInfo clientResumeRuntime = typeof(DiagnosticsClient).GetMethod("ResumeRuntime", BindingFlags.NonPublic | BindingFlags.Instance);

        public static ReversedServer CreateReversedServer(string diagPortName)
        {
            return Activator.CreateInstance(reversedServerType, new[] { diagPortName });
        }

        public static void Start(ReversedServer server)
        {
            serverStart.Invoke(server, Array.Empty<object>());
        }

        public static ValueTask DisposeAsync(ReversedServer server) {
            return (ValueTask)disposeAsync.Invoke(server, Array.Empty<object>());
        }

        public static DiagnosticsClient WaitForProcessToConnect(ReversedServer server, int pid)
        {
            var endpointInfo = serverAccept.Invoke(server, new object[] { TimeSpan.FromSeconds(15.0) });
            while ((int)ipcEndpointInfoProcessIdProperty.GetValue(endpointInfo)! != pid)
            {
                endpointInfo = serverAccept.Invoke(server, new object[] { TimeSpan.FromSeconds(15.0) });
            }
            var endpoint = ipcEndpointInfoEndpointProperty.GetValue(endpointInfo);
            return (DiagnosticsClient)Activator.CreateInstance(diagClientType, BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { endpoint }, null);
        }

        public static void ResumeRuntime(DiagnosticsClient client)
        {
            clientResumeRuntime.Invoke(client, Array.Empty<object>());
        }
    }
}
