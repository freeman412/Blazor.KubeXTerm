using System.Security.Claims;
using System.Text.Json;
using Blazor.KubeXTerm.Demo.Components;
using Blazor.KubeXTerm.Demo.Services;
using MudBlazor.Services;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Load configuration from appsettings.json & environment variables
var configuration = builder.Configuration.SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true) // Load appsettings.json
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true) // Load appsettings.Development.json
    .AddEnvironmentVariables() // Override with environment variables if set
    .Build();

var useKeycloak = !string.Equals(configuration["USE_KEYCLOAK"], "false", StringComparison.OrdinalIgnoreCase);

HttpClient httpClient;
if (configuration["HTTPCLIENT_VALIDATE_EXTERNAL_CERTIFICATES"] == "false")
{
    // Needed locally when Keycloak is not assigned any proper certificates
    var handler = new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    };
    httpClient = new HttpClient(handler);
}
else
{
    httpClient = new HttpClient();
}
builder.Services.AddSingleton(httpClient);
if (useKeycloak)
{
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;

            var backendIdpUrl = configuration["OIDC_IDP_ADDRESS_FOR_SERVER"];
            var clientIdpUrl = configuration["OIDC_IDP_ADDRESS_FOR_USERS"];

            options.Configuration = new()
            {
                Issuer = backendIdpUrl,
                AuthorizationEndpoint = $"{clientIdpUrl}/protocol/openid-connect/auth",
                TokenEndpoint = $"{backendIdpUrl}/protocol/openid-connect/token",
                JwksUri = $"{backendIdpUrl}/protocol/openid-connect/certs",
                JsonWebKeySet = FetchJwks($"{backendIdpUrl}/protocol/openid-connect/certs"),
                EndSessionEndpoint = $"{clientIdpUrl}/protocol/openid-connect/logout",
            };

            Console.WriteLine("Jwks: " + options.Configuration.JsonWebKeySet);
            foreach (var key in options.Configuration.JsonWebKeySet.GetSigningKeys())
            {
                options.Configuration.SigningKeys.Add(key);
                Console.WriteLine("Added SigningKey: " + key.KeyId);
            }

            options.ClientId = configuration["OIDC_CLIENT_ID"];
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuers = [clientIdpUrl, backendIdpUrl],
                NameClaimType = "name",
                RoleClaimType = ClaimTypes.Role
            };
            options.RequireHttpsMetadata = configuration["OIDC_REQUIRE_HTTPS_METADATA"] != "false";
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.SaveTokens = true;
            options.MapInboundClaims = true;

            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.Scope.Add("roles");
            
            // Custom claim mapping
            options.Events = new OpenIdConnectEvents
            {
                OnTokenValidated = context =>
                {
                    var identity = (ClaimsIdentity)context.Principal!.Identity!;
                    var accessToken = context.TokenEndpointResponse!.AccessToken;
                    if (!string.IsNullOrEmpty(accessToken))
                    {
                        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                        var token = handler.ReadJwtToken(accessToken);
                        var resourceAccess = token.Claims.FirstOrDefault(c => c.Type == "resource_access")?.Value;
                        if (!string.IsNullOrEmpty(resourceAccess))
                        {
                            using var jsonDoc = JsonDocument.Parse(resourceAccess);
                            if (jsonDoc.RootElement.TryGetProperty("kube_xterm_demo", out var kubeXTermDemoRoles) &&
                                kubeXTermDemoRoles.TryGetProperty("roles", out var roles))
                            {
                                foreach (var role in roles.EnumerateArray())
                                {
                                    identity.AddClaim(new Claim(ClaimTypes.Role, role.GetString()!));
                                }
                            }
                        }
                    }
                    return Task.CompletedTask;
                }
            };
        })
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);
}
else
{
    // Fake authentication for "Demo User"
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("DemoPolicy", policy => policy.RequireAuthenticatedUser());
    });
}


JsonWebKeySet FetchJwks(string url)
{
    var result = httpClient.GetAsync(url).Result;
    if (!result.IsSuccessStatusCode || result.Content is null)
    {
        throw new Exception(
            $"Getting token issuers (Keycloaks) JWKS from {url} failed. Status code {result.StatusCode}");
    }

    var jwks = result.Content.ReadAsStringAsync().Result;
    return new JsonWebKeySet(jwks);
}

//Authentication and Authorization Services
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

// Add MudBlazor services
builder.Services.AddMudServices();
// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<HttpClient>(sp => new HttpClient
{
    BaseAddress = new Uri("https://localhost:44317/") // Set to your API's base URL
});

// Register KubeXTermK8SManager as a service
builder.Services.AddScoped<KubeXTermK8SManager>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    // Check if we are NOT using Keycloak and the user is NOT authenticated
    if (!useKeycloak && !context.User.Identity!.IsAuthenticated && context.Request.Path != "/login")
    {
        await next(); // Allow unauthenticated access
        return;
    }
    
    await next();
});


app.MapPost("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    if (useKeycloak)
    {
        await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);

        var clientIdpUrl = configuration["OIDC_IDP_ADDRESS_FOR_USERS"];
        var redirectUri = configuration["OIDC_LOGOUT_REDIRECT_URI"] ?? "/"; // Default to home if not set

        // Retrieve the ID token from the authentication properties
        var idToken = await context.GetTokenAsync("id_token");
        
        //I would never use this logout method accross the internet since the id_token_hint has too much personal info
        var keycloakLogoutUrl = string.IsNullOrEmpty(idToken)
            ? $"{clientIdpUrl}/protocol/openid-connect/logout?post_logout_redirect_uri={Uri.EscapeDataString(redirectUri)}"
            : $"{clientIdpUrl}/protocol/openid-connect/logout?post_logout_redirect_uri={Uri.EscapeDataString(redirectUri)}&id_token_hint={Uri.EscapeDataString(idToken)}";

        context.Response.Redirect(keycloakLogoutUrl);
        return; // Prevent further redirects
    }

    context.Response.Redirect("/"); // Redirect to home for non-Keycloak users
});

//You would need to implement the settings in keycload first - not sure i'm goign to use this
app.MapPost("/logout/backchannel", async (HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync();
    
    Console.WriteLine("Backchannel logout request received: " + body);

    var form = System.Web.HttpUtility.ParseQueryString(body);
    var sessionId = form["sid"]; // Keycloak sends session ID

    if (string.IsNullOrEmpty(sessionId))
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("Missing session ID");
        return;
    }

    // Invalidate session in your app
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);

    context.Response.StatusCode = 200;
    await context.Response.WriteAsync("Logout successful");
});


app.MapPost("/login", async (HttpContext context) =>
{
    if (useKeycloak)
    {
        var redirectUri = "/"; // Redirect after login
        await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
        {
            RedirectUri = redirectUri
        });
        return;
    }
    
    //DEMO User Section
    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, "Demo User"),
        new(ClaimTypes.Role, "KubeXAdmin"),
        new("preferred_username", "demo")
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);

    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    Console.WriteLine("Demo User logged in.");
    
    context.Response.Redirect("/"); // Redirect after login
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

