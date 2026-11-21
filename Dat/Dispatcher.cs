using System;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ChatApp.Server
{
    /// <summary>
    /// Định tuyến request JSON nhận được theo trường `type` tới handler tương ứng.
    /// Kiểm tra chuẩn envelope (`v == 1`, `type`, `id`) và quyền xác thực.
    /// </summary>
    public sealed class Dispatcher
    {
        public delegate Task<JsonObject> RequestHandler(ClientConnection conn, JsonObject data);

        private readonly ConcurrentDictionary<string, (RequestHandler Handler, bool RequiresAuth)> _handlers = new();

        public void RegisterHandler(string type, RequestHandler handler, bool requiresAuth = true)
        {
            _handlers[type] = (handler, requiresAuth);
        }

        public async Task DispatchAsync(ClientConnection conn, JsonObject frame)
        {
            // Wrong-typed fields must give BAD_REQUEST, not an exception that drops the connection.
            int version = frame["v"] is JsonValue vNode && vNode.TryGetValue<int>(out int v) ? v : 0;
            string? type = frame["type"] is JsonValue tNode && tNode.TryGetValue<string>(out string? tText) ? tText : null;
            long reqId = frame["id"] is JsonValue idNode && idNode.TryGetValue<long>(out long id) ? id : -1;

            if (version != 1 || string.IsNullOrWhiteSpace(type) || reqId < 0)
            {
                await conn.SendAsync(MakeError(reqId, "BAD_REQUEST", "Invalid envelope: require v=1, type, id"))
                          .ConfigureAwait(false);
                return;
            }

            if (!_handlers.TryGetValue(type, out var entry))
            {
                await conn.SendAsync(MakeError(reqId, "NOT_FOUND", $"Unknown message type: {type}"))
                          .ConfigureAwait(false);
                return;
            }

            if (entry.RequiresAuth && !conn.IsAuthenticated)
            {
                await conn.SendAsync(MakeError(reqId, "UNAUTHORIZED", "Authentication required"))
                          .ConfigureAwait(false);
                return;
            }

            JsonObject data = frame["data"] as JsonObject ?? new JsonObject();
            JsonObject result;
            try
            {
                result = await entry.Handler(conn, data).ConfigureAwait(false);
            }
            catch (ApiException ex)
            {
                result = MakeError(reqId, ex.Code, ex.Message);
            }
            catch (Exception ex)
            {
                // Details stay in the server log; the client only gets a generic message.
                Console.WriteLine($"[Dispatcher] {type} failed: {ex}");
                result = MakeError(reqId, "INTERNAL", "Internal server error");
            }

            result["v"] = 1;
            result["re"] = reqId;
            if (!result.ContainsKey("ok"))
                result["ok"] = true;

            await conn.SendAsync(result).ConfigureAwait(false);

            while (conn.AfterReply.TryDequeue(out var after))
            {
                try { await after().ConfigureAwait(false); }
                catch (Exception ex) { Console.WriteLine($"[Dispatcher] after-reply of {type} failed: {ex.Message}"); }
            }
        }

        public static JsonObject MakeSuccess(long reqId = 0, JsonObject? data = null)
        {
            return new JsonObject
            {
                ["v"] = 1,
                ["re"] = reqId,
                ["ok"] = true,
                ["data"] = data ?? new JsonObject()
            };
        }

        public static JsonObject MakeError(long reqId, string code, string message)
        {
            return new JsonObject
            {
                ["v"] = 1,
                ["re"] = reqId,
                ["ok"] = false,
                ["error"] = new JsonObject
                {
                    ["code"] = code,
                    ["message"] = message
                }
            };
        }
    }
}
