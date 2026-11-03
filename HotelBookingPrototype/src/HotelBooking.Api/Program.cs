using System.Text;
using HotelBooking.Api.Data;
using HotelBooking.Api.Infrastructure;
using HotelBooking.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---- Konfigurasi ----
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()
          ?? throw new InvalidOperationException("Konfigurasi Jwt belum diisi.");
var otp = builder.Configuration.GetSection("Otp").Get<OtpSettings>() ?? new OtpSettings();
var auth = builder.Configuration.GetSection("Auth").Get<AuthSettings>() ?? new AuthSettings();
var booking = builder.Configuration.GetSection("Booking").Get<BookingSettings>() ?? new BookingSettings();
var integration = builder.Configuration.GetSection("Integration").Get<IntegrationSettings>()
                  ?? new IntegrationSettings();

builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton(otp);
builder.Services.AddSingleton(auth);
builder.Services.AddSingleton(booking);
builder.Services.AddSingleton(integration);

// ---- Layanan (in-memory prototype) ----
builder.Services.AddSingleton<InMemoryDb>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<OtpService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<HotelService>();
builder.Services.AddSingleton<BookingService>();
builder.Services.AddSingleton<ETicketService>();
builder.Services.AddSingleton<PaymentService>();
builder.Services.AddSingleton<CancellationService>();
builder.Services.AddSingleton<RefundService>();
builder.Services.AddHostedService<BookingExpiryService>();

// ---- Auth (JWT) ----
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = "role"
        };
        // UAT-LOG-06: access token yang sudah dicabut saat logout langsung tidak berlaku.
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var jti = context.Principal?.FindFirst("jti")?.Value;
                var db = context.HttpContext.RequestServices.GetRequiredService<InMemoryDb>();
                if (jti != null && db.RevokedAccessJtis.ContainsKey(jti))
                {
                    context.Fail("Token sudah dicabut (logout).");
                }
                return System.Threading.Tasks.Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// ---- Swagger ----
builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Hotel Booking Prototype API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
