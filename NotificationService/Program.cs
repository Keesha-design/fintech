using MassTransit;
using NotificationService.Consumers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<PaymentVerifiedConsumer>();
    x.AddConsumer<PaymentFailedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"], "/", h =>
        {
            h.Username(builder.Configuration["RabbitMq:Username"]!);
            h.Password(builder.Configuration["RabbitMq:Password"]!);
        });
        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddHealthChecks();

var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();
