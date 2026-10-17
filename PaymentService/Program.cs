using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PaymentService.Data;
using PaymentService.Services;
using Polly;
using Polly.Extensions.Http;
using StackExchange.Redis;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<PaymentDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("PaymentDb")));

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"]!));

builder.Services.AddSingleton<IIdempotencyService, RedisIdempotencyService>();

// --- Polly: retry with exponential backoff, then circuit breaker ---
// Retry handles transient blips (a timeout, a 502). Circuit breaker handles SUSTAINED failure:
// if Paystack is genuinely down, stop hammering it and fail fast instead of queueing up threads
// waiting on doomed calls.
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError() // 5xx and 408
        .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))); // 2s, 4s, 8s

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5, // trip after 5 consecutive failures
            durationOfBreak: TimeSpan.FromSeconds(30)); // stay open (fail fast) for 30s, then half-open

builder.Services.AddHttpClient<IPaystackClient, PaystackClient>()
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());

// --- MassTransit / RabbitMQ: event bus between services ---
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<PaymentWebhookConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"], "/", h =>
        {
            h.Username(builder.Configuration["RabbitMq:Username"]!);
            h.Password(builder.Configuration["RabbitMq:Password"]!);
        });

        // MassTransit's retry middleware: 3 attempts with backoff before the message is
        // moved to <queue>_error — that error queue IS the dead letter queue for this consumer.
        cfg.ReceiveEndpoint("payment-webhook-received", e =>
        {
            e.UseMessageRetry(r => r.Intervals(
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30)));

            e.ConfigureConsumer<PaymentWebhookConsumer>(context);
        });

        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddHostedService<ReconciliationJob>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]!))
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddHealthChecks();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
