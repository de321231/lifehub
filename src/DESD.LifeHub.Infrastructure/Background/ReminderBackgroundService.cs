using DESD.LifeHub.Infrastructure.Options;
using DESD.LifeHub.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DESD.LifeHub.Infrastructure.Background;

/// <summary>
/// Hintergrunddienst für Erinnerungen. Läuft in derselben Anwendung (keine
/// zusätzlichen Dienste nötig) und beansprucht fällige Erinnerungen atomar.
///
/// Die eigentliche Logik liegt in <see cref="IReminderService.ProcessDueAsync"/>;
/// dadurch ist sie ohne Hosting-Umgebung testbar.
/// </summary>
public sealed class ReminderBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReminderOptions _options;
    private readonly ILogger<ReminderBackgroundService> _logger;

    public ReminderBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<ReminderOptions> options,
        ILogger<ReminderBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(_options.PollSeconds, 5, 3600));
        _logger.LogInformation("Erinnerungsdienst gestartet (Intervall {Interval}s).", interval.TotalSeconds);

        // Kleine Anlaufverzögerung, damit die Datenbankmigration abgeschlossen ist.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var reminders = scope.ServiceProvider.GetRequiredService<IReminderService>();
                var processed = await reminders.ProcessDueAsync(_options.BatchSize, stoppingToken).ConfigureAwait(false);

                if (processed > 0)
                {
                    _logger.LogInformation("{Count} Erinnerung(en) verarbeitet.", processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Ein Fehler darf den Dienst nicht beenden.
                _logger.LogError(ex, "Fehler bei der Verarbeitung von Erinnerungen.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Erinnerungsdienst beendet.");
    }
}
