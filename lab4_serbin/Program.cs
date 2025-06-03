using Microsoft.AspNetCore.Http;
using System.Text;
using System.IdentityModel.Tokens.Jwt;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddCors();
var app = builder.Build();

app.UseHttpsRedirection();
app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
app.UseRouting();
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
    var token = System.Text.Json.JsonDocument.Parse(tokenJson).RootElement.GetProperty("access_token").GetString();

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

app.Run();
