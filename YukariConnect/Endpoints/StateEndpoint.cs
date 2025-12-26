using YukariConnect.Scaffolding;

namespace YukariConnect.Endpoints
{
    public static class StateEndpoint
    {
        public record StateResponse(
            string State,
            string? Role = null,
            string? Room = null,
            int? ProfileIndex = null,
            List<Scaffolding.Models.ScaffoldingProfile>? Profiles = null,
            string? Url = null,
            string? Difficulty = null,
            int? ExceptionType = null
        );

        public static void Map(WebApplication app)
        {
            app.MapGet("/state", (RoomController roomController) =>
            {
                var status = roomController.GetStatus();
                var state = MapRoomStateToTerracottaState(status.State);

                string? role = null;
                string? room = null;
                List<Scaffolding.Models.ScaffoldingProfile>? profiles = null;
                string? url = null;
                int? profileIndex = 0;

                if (status.Role != null)
                {
                    role = status.Role.ToString();
                }

                if (status.RoomCode != null)
                {
                    room = status.RoomCode;
                }

                if (status.Players != null && status.Players.Count > 0)
                {
                    profiles = status.Players.ToList();
                }

                // For guest mode, provide the localhost URL
                if (status.State == Scaffolding.RoomStateKind.Guest_Running && status.MinecraftPort != null)
                {
                    url = status.MinecraftPort == 25565
                        ? "127.0.0.1"
                        : $"127.0.0.1:{status.MinecraftPort}";
                }

                var payload = new StateResponse(
                    State: state,
                    Role: role,
                    Room: room,
                    ProfileIndex: profileIndex,
                    Profiles: profiles,
                    Url: url
                );

                return TypedResults.Ok(payload);
            });
        }

        private static string MapRoomStateToTerracottaState(Scaffolding.RoomStateKind state)
        {
            // Map Yukari states to Terracotta-compatible state names
            return state.Value switch
            {
                "Idle" => "waiting",
                "Host_Prepare" => "host-scanning",
                "Host_EasyTierStarting" => "host-starting",
                "Host_ScaffoldingStarting" => "host-starting",
                "Host_MinecraftDetecting" => "host-starting",
                "Host_Running" => "host-ok",
                "Guest_Prepare" => "guest-connecting",
                "Guest_EasyTierStarting" => "guest-starting",
                "Guest_DiscoveringCenter" => "guest-starting",
                "Guest_ConnectingScaffolding" => "guest-starting",
                "Guest_Running" => "guest-ok",
                "Stopping" => "waiting",
                "Error" => "exception",
                _ => state.Value
            };
        }
    }
}
