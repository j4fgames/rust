using System;
using System.Net;
using System.Text;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("PrometheusExporter", "j4fgames", "0.1.0")]
    [Description("Exposes Prometheus metrics endpoint")]
    class PrometheusExporter : RustPlugin
    {
        private HttpListener listener;
        private const int MetricsPort = 9101;
        
        void OnServerInitialized()
        {
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://+:{MetricsPort}/metrics/");
                listener.Start();
                Puts($"Prometheus metrics listening on port {MetricsPort}");
                listener.BeginGetContext(HandleRequest, null);
            }
            catch (Exception ex)
            {
                PrintError($"Failed to start metrics server: {ex.Message}");
            }
        }
        
        void HandleRequest(IAsyncResult result)
        {
            try
            {
                var context = listener.EndGetContext(result);
                listener.BeginGetContext(HandleRequest, null);
                
                var metrics = GenerateMetrics();
                var buffer = Encoding.UTF8.GetBytes(metrics);
                
                context.Response.ContentType = "text/plain; version=0.0.4";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.Close();
            }
            catch (Exception ex)
            {
                PrintError($"Error handling metrics request: {ex.Message}");
            }
        }
        
        string GenerateMetrics()
        {
            var sb = new StringBuilder();
            
            // Player counts
            int connected = BasePlayer.activePlayerList.Count;
            int sleepers = BasePlayer.sleepingPlayerList.Count;
            int joining = ServerMgr.Instance.connectionQueue.joining.Count;
            int queued = ServerMgr.Instance.connectionQueue.queue.Count;
            
            sb.AppendLine("# HELP rust_players_connected Number of connected players");
            sb.AppendLine("# TYPE rust_players_connected gauge");
            sb.AppendLine($"rust_players_connected {connected}");
            
            sb.AppendLine("# HELP rust_players_sleeping Number of sleeping players");
            sb.AppendLine("# TYPE rust_players_sleeping gauge");
            sb.AppendLine($"rust_players_sleeping {sleepers}");
            
            sb.AppendLine("# HELP rust_players_joining Number of players joining");
            sb.AppendLine("# TYPE rust_players_joining gauge");
            sb.AppendLine($"rust_players_joining {joining}");
            
            sb.AppendLine("# HELP rust_players_queued Number of queued players");
            sb.AppendLine("# TYPE rust_players_queued gauge");
            sb.AppendLine($"rust_players_queued {queued}");
            
            // Per-player info
            sb.AppendLine("# HELP rust_players_info Current players on the server");
            sb.AppendLine("# TYPE rust_players_info gauge");
            foreach (var player in BasePlayer.activePlayerList)
            {
                var name = player.displayName.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
                var steamId = player.UserIDString;
                var ping = Network.Net.sv.GetAveragePing(player.net.connection);
                var connectedSeconds = (int)player.net.connection.GetSecondsConnected();
                sb.AppendLine($"rust_players_info{{player_name=\"{name}\",steam_id=\"{steamId}\",ping=\"{ping}\",connected_seconds=\"{connectedSeconds}\"}} 1");
            }

            // Server info
            sb.AppendLine("# HELP rust_server_fps Server FPS");
            sb.AppendLine("# TYPE rust_server_fps gauge");
            sb.AppendLine($"rust_server_fps {Performance.report.frameRate}");
            
            return sb.ToString();
        }
        
        void Unload()
        {
            listener?.Stop();
            listener?.Close();
        }
    }
}
