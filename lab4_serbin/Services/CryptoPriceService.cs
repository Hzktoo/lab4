using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public class CryptoPriceService
{
    private readonly string[] symbols = { "btcusdt", "ethusdt", "xrpusdt", "bnbusdt" };

    private readonly ConcurrentDictionary<string, decimal> prices = new();
    private readonly List<WebSocket> clients = new();
    private readonly object lockObj = new();

    private ClientWebSocket binanceSocket;
    private CancellationTokenSource cts;

    public IReadOnlyDictionary<string, decimal> Prices => prices;

    public void Start()
    {
        cts = new CancellationTokenSource();
        _ = Task.Run(() => ConnectToBinanceAsync(cts.Token));
    }

    private async Task ConnectToBinanceAsync(CancellationToken cancellationToken)
    {
        binanceSocket = new ClientWebSocket();

        var streams = string.Join("/", symbols).ToLower() + "@trade";
        var streamParams = string.Join('/', Array.ConvertAll(symbols, s => $"{s.ToLower()}@trade"));
        var uri = new Uri($"wss://stream.binance.com:9443/stream?streams={streamParams}");

        await binanceSocket.ConnectAsync(uri, cancellationToken);

        var buffer = new byte[8192];

        while (binanceSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(1000);
            var result = await binanceSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await binanceSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);
                break;
            }
            else if (result.MessageType == WebSocketMessageType.Text)
            {
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                ProcessMessage(message);
            }
        }
    }

    private void ProcessMessage(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);

            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var data)) return;
            if (!root.TryGetProperty("stream", out var streamName)) return;

            var symbol = streamName.GetString().Split('@')[0].ToUpper();
            Console.WriteLine($"Received: {message}");

            if (data.TryGetProperty("p", out var priceEl))
            {
                if (decimal.TryParse(priceEl.GetString(), out var price))
                {
                    prices[symbol] = price;
                    BroadcastPrices();
                }
            }
        }
        catch
        {

        }
    }

    public void AddClient(WebSocket client)
    {
        lock (lockObj)
        {
            clients.Add(client);
        }
    }

    public void RemoveClient(WebSocket client)
    {
        lock (lockObj)
        {
            clients.Remove(client);
        }
    }

    private void BroadcastPrices()
    {
        var json = JsonSerializer.Serialize(prices);
        var buffer = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(buffer);

        lock (lockObj)
        {
            var disconnectedClients = new List<WebSocket>();

            foreach (var client in clients)
            {
                if (client.State == WebSocketState.Open)
                {
                    try
                    {
                        client.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None).Wait();
                    }
                    catch
                    {
                        disconnectedClients.Add(client);
                    }
                }
                else
                {
                    disconnectedClients.Add(client);
                }
            }

            foreach (var dc in disconnectedClients)
            {
                clients.Remove(dc);
            }
        }
    }

    public async Task StopAsync()
    {
        cts?.Cancel();

        if (binanceSocket != null && (binanceSocket.State == WebSocketState.Open || binanceSocket.State == WebSocketState.CloseReceived))
        {
            await binanceSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Service stopped", CancellationToken.None);
            binanceSocket.Dispose();
        }
    }
}
