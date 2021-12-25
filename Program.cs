using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Threading;
using System.Threading.Tasks;

namespace eventpiper
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Missing argument: process to start or PID");
                return;
            }
            using var cts = new CancellationTokenSource();

            int.TryParse(args[0], out var pid);
            await using var diagSession = pid == 0 ? StartNewProcess(args) : AttachToProcess(pid);

            var providers = new[] {
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, 0x8000) // exceptions only
            };

            using var session = diagSession.DiagClient.StartEventPipeSession(providers, false /* no rundown events */);

            Console.CancelKeyPress += (o, ev) => { ev.Cancel = true; session.Stop(); };

            if (pid == 0) {
                UnofficialDiagnosticsClientApi.ResumeRuntime(diagSession.DiagClient);
            }

            using var eventSource = new EventPipeEventSource(session.EventStream);

            eventSource.Clr.ExceptionStart += Clr_ExceptionStart;

            eventSource.Process();
        }

        static DiagnosticsSession AttachToProcess(int pid)
        {
            return new DiagnosticsSession(null, new DiagnosticsClient(pid));
        }

        static DiagnosticsSession StartNewProcess(string[] args)
        {
            var diagPortName = $"eventpiper-{Process.GetCurrentProcess().Id}-{DateTime.Now:yyyyMMdd_HHmmss}.socket";
            var server = UnofficialDiagnosticsClientApi.CreateReversedServer(diagPortName);

            UnofficialDiagnosticsClientApi.Start(server);

            var startInfo = new ProcessStartInfo(args[0], string.Join(' ', args, 1, args.Length - 1)) {
                UseShellExecute = false,
                CreateNoWindow = false
            };
            startInfo.Environment.Add("DOTNET_DiagnosticPorts", diagPortName);

            using var proc = Process.Start(startInfo);
            var client = UnofficialDiagnosticsClientApi.WaitForProcessToConnect(server, proc.Id);

            return new DiagnosticsSession(server, client);
        }

        public static void Clr_ExceptionStart(ExceptionTraceData ev)
        {
            Console.WriteLine($"Exception event: [{ev.ExceptionType}] '{ev.ExceptionMessage}'");
        }
    }

    class DiagnosticsSession : IAsyncDisposable
    {
        private readonly object diagnosticsServer;
        private readonly DiagnosticsClient diagnosticsClient;

        public DiagnosticsSession(object server, DiagnosticsClient client) {
            diagnosticsClient = client;
            diagnosticsServer = server;
        }

        public DiagnosticsClient DiagClient => diagnosticsClient;

        public ValueTask DisposeAsync()
        {
            if (diagnosticsServer is null) {
                return ValueTask.CompletedTask;
            }
            return UnofficialDiagnosticsClientApi.DisposeAsync(diagnosticsServer);
        }
    }
}
