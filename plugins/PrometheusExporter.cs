using System;
using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Prometheus Exporter", "j4fgames", "0.2.0")]
    [Description("Exposes Prometheus metrics endpoint")]
    class PrometheusExporter : RustPlugin
    {
        private HttpListener listener;
        private Configuration config;

        private class Configuration
        {
            [JsonProperty("Listening address")]
            public string ListenAddress { get; set; } = "127.0.0.1";

            [JsonProperty("Listening port")]
            public int MetricsPort { get; set; } = 9101;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
            }
            catch
            {
                PrintError("Configuration file is corrupt, creating new one!");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void SaveConfig() => Config.WriteObject(config, true);

        void OnServerInitialized()
        {
            if (config.MetricsPort < 1 || config.MetricsPort > 65535)
            {
                PrintError($"Invalid port number: {config.MetricsPort}. Must be between 1 and 65535.");
                return;
            }

            var address = config.ListenAddress?.Trim();
            if (string.IsNullOrEmpty(address))
            {
                PrintError("Listening address is empty.");
                return;
            }

            if (address == "0.0.0.0")
                address = "+";

            if (address != "+" && address != "*" && !IPAddress.TryParse(address, out _))
            {
                PrintError($"Invalid listening address: {config.ListenAddress}");
                return;
            }

            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://{address}:{config.MetricsPort}/metrics/");
                listener.Start();
                Puts($"Prometheus metrics listening on {address}:{config.MetricsPort}");
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
            
            // Per-player metrics
            sb.AppendLine("# HELP rust_players_info Current players on the server");
            sb.AppendLine("# TYPE rust_players_info gauge");
            sb.AppendLine("# HELP rust_players_connected_seconds Seconds each player has been connected");
            sb.AppendLine("# TYPE rust_players_connected_seconds gauge");
            sb.AppendLine("# HELP rust_players_ping_milliseconds Average ping per player in milliseconds");
            sb.AppendLine("# TYPE rust_players_ping_milliseconds gauge");
            foreach (var player in BasePlayer.activePlayerList)
            {
                var name = player.displayName.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
                var steamId = player.UserIDString;
                var ping = Network.Net.sv.GetAveragePing(player.net.connection);
                var connectedSeconds = (int)player.net.connection.GetSecondsConnected();
                var ip = player.net.connection.ipaddress.Split(':')[0];
                sb.AppendLine($"rust_players_info{{player_name=\"{name}\",steam_id=\"{steamId}\",ip=\"{ip}\"}} 1");
                sb.AppendLine($"rust_players_connected_seconds{{player_name=\"{name}\",steam_id=\"{steamId}\"}} {connectedSeconds}");
                sb.AppendLine($"rust_players_ping_milliseconds{{player_name=\"{name}\",steam_id=\"{steamId}\"}} {ping}");
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
