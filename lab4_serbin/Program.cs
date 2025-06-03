using Microsoft.AspNetCore.Http;
using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddCors();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
app.UseRouting();
app.UseWebSockets();
app.UseCookiePolicy();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/signin-casdoor", async (HttpContext context) =>
{
    var code = context.Request.Query["code"];
    if (string.IsNullOrEmpty(code)) return Results.BadRequest("Missing code");

    var tokenResponse = await new HttpClient().PostAsync("https://localhost:8443/api/login/oauth/access_token", new FormUrlEncodedContent(new Dictionary<string, string>
    {
        { "client_id", "fce836b52844f980c73c" },
        { "client_secret", "0bed9e1936ba755001ef4d0c3bef67cd3edbdcd4" },
        { "code", code },
        { "grant_type", "authorization_code" },
        { "redirect_uri", "https://localhost:5001/signin-casdoor" }
    }));

    var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
    var token = JsonDocument.Parse(tokenJson).RootElement.GetProperty("access_token").GetString();

    context.Response.Cookies.Append("jwt_token", token, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict
    });

    return Results.Redirect("/menu.html");
});

app.MapGet("/userinfo", (HttpContext context) =>
{
    if (!context.Request.Cookies.TryGetValue("jwt_token", out var token))
        return Results.Unauthorized();

    var handler = new JwtSecurityTokenHandler();
    var jwtToken = handler.ReadJwtToken(token);

    var userInfo = jwtToken.Payload
        .Where(kvp => kvp.Key != "exp" && kvp.Key != "iat")
        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString());

    return Results.Json(userInfo);
});

var cryptoService = new CryptoPriceService();
cryptoService.Start();

app.Map("/ws", async (HttpContext context) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var socket = await context.WebSockets.AcceptWebSocketAsync();
        Console.WriteLine("Client connected to WebSocket!");
        cryptoService.AddClient(socket);

        var buffer = new byte[1024 * 4];
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None);
                break;
            }
        }

        cryptoService.RemoveClient(socket);
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});

app.Run();
