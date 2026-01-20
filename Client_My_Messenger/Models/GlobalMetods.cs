using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;

namespace Client_My_Messenger.Models
{
    public class GlobalMetods
    {
        // Метод отправки JSON
        public static async Task SendJsonAsync(ClientWebSocket ws, CancellationTokenSource cts, string json)
        {
            if (ws.State == WebSocketState.Open)
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    cts.Token);
            }
        }

        // Метод приема JSON с поддержкой больших сообщений
        public static async Task<string> ReceiveJsonAsync(ClientWebSocket ws, CancellationTokenSource cts)
        {
            try
            {
                var buffer = new byte[8192 * 4];
                var result = await ws.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cts.Token);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    if (result.EndOfMessage)
                    {
                        return Encoding.UTF8.GetString(buffer, 0, result.Count);
                    }
                    else
                    {
                        var allBytes = new List<byte>();
                        allBytes.AddRange(buffer.Take(result.Count));

                        while (!result.EndOfMessage)
                        {
                            result = await ws.ReceiveAsync(
                                new ArraySegment<byte>(buffer),
                                cts.Token);
                            allBytes.AddRange(buffer.Take(result.Count));
                        }

                        return Encoding.UTF8.GetString(allBytes.ToArray());
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }
            }
            catch (OperationCanceledException)
            {
                // Игнорируем отмену
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"Ошибка приема: {ex.Message}");
            }

            return null;
        }
    }
}