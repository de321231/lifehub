using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DESD.LifeHub.Core.Abstractions;
using DESD.LifeHub.Core.Licensing;
using DESD.LifeHub.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DESD.LifeHub.Infrastructure.Licensing;

/// <summary>
/// HTTP-Client für den öffentlichen OpenLicensd-Endpunkt <c>POST /api/v1/validate</c>.
///
/// Umfang der Anbindung: OpenLicensd stellt öffentlich ausschliesslich
/// <c>/api/v1/validate</c> (und optional <c>/api/v1/registry-credentials</c>) bereit.
/// Ein öffentlicher Deaktivierungsendpunkt existiert nicht – es werden daher keine
/// erfundenen Endpunkte aufgerufen. Aktivierungen werden über den <c>fingerprint</c>
/// gezählt, den OpenLicensd bei aktivierungsbegrenzten Lizenzen auswertet.
///
/// Der Lizenzschlüssel wird ausschliesslich im Request-Body übertragen und niemals
/// protokolliert, in URLs geschrieben oder in Fehlermeldungen eingebettet.
/// </summary>
public sealed class OpenLicensdClient : ILicenseValidator
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly OpenLicensdOptions _options;
    private readonly ILogger<OpenLicensdClient> _logger;

    public OpenLicensdClient(HttpClient http, IOptions<OpenLicensdOptions> options, ILogger<OpenLicensdClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LicenseCheckResult> ValidateAsync(LicenseValidationRequest request, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return LicenseCheckResult.Malformed("Lizenzprüfung ist deaktiviert (OpenLicensd:Enabled=false).");
        }

        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            return LicenseCheckResult.Malformed("OpenLicensd:BaseUrl ist keine gültige absolute URL.");
        }

        var path = _options.ValidatePath.StartsWith('/') ? _options.ValidatePath : "/" + _options.ValidatePath;
        var uri = new Uri(baseUri, path);

        var payload = new ValidateRequestDto
        {
            Key = request.LicenseKey,
            Product = request.Product,
            Fingerprint = request.Fingerprint,
            Hostname = request.Hostname,
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 120)));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await _http.PostAsJsonAsync(uri, payload, SerializerOptions, timeoutCts.Token)
                .ConfigureAwait(false);

            stopwatch.Stop();
            var status = (int)response.StatusCode;

            if (response.StatusCode == HttpStatusCode.OK)
            {
                ValidateResponseDto? dto;
                try
                {
                    dto = await response.Content.ReadFromJsonAsync<ValidateResponseDto>(SerializerOptions, timeoutCts.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException)
                {
                    // Kein Geheimnis in der Meldung; nur technische Klasse.
                    _logger.LogWarning(
                        "OpenLicensd lieferte für Produkt {Product} eine nicht interpretierbare Antwort (HTTP {Status}).",
                        request.Product, status);
                    return LicenseCheckResult.Malformed("Unerwartetes Antwortformat von OpenLicensd.", status);
                }

                if (dto is null)
                {
                    return LicenseCheckResult.Malformed("Leere Antwort von OpenLicensd.", status);
                }

                var expiresAt = dto.ExpiresAt;

                if (dto.Valid)
                {
                    _logger.LogInformation(
                        "Lizenzprüfung erfolgreich für Produkt {Product} (Policy {Policy}, Kulanzfrist {Grace}, {Elapsed} ms).",
                        dto.Product ?? request.Product, dto.Policy ?? "-", dto.InGracePeriod, stopwatch.ElapsedMilliseconds);

                    return LicenseCheckResult.Success(
                        productCode: dto.Product,
                        policyName: dto.Policy,
                        expiresAt: expiresAt,
                        inGracePeriod: dto.InGracePeriod,
                        activationCount: dto.ActivationCount,
                        maxActivations: dto.MaxActivations,
                        httpStatusCode: status);
                }

                var reason = string.IsNullOrWhiteSpace(dto.Reason) ? "invalid" : dto.Reason;

                _logger.LogInformation(
                    "Lizenzprüfung abgelehnt für Produkt {Product}: {Reason} (HTTP {Status}).",
                    dto.Product ?? request.Product, reason, status);

                return LicenseCheckResult.Rejected(
                    reason: reason,
                    diagnostic: reason,
                    httpStatusCode: status,
                    productCode: dto.Product,
                    policyName: dto.Policy,
                    expiresAt: expiresAt,
                    inGracePeriod: dto.InGracePeriod,
                    activationCount: dto.ActivationCount,
                    maxActivations: dto.MaxActivations);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                // Eigener Fehler (ungültiger Request), kein Lizenzproblem.
                _logger.LogWarning("OpenLicensd lehnte die Anfrage ab (HTTP 400). Bitte Request-Format prüfen.");
                return LicenseCheckResult.Malformed("Lizenzserver lehnte die Anfrage ab (HTTP 400).", status);
            }

            if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
            {
                return LicenseCheckResult.Malformed("Anfrage war zu gross für den Lizenzserver (HTTP 413).", status);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Lizenzserver drosselt Anfragen (HTTP 429).");
                return LicenseCheckResult.Transient("Lizenzserver drosselt Anfragen (HTTP 429).", status);
            }

            if ((int)response.StatusCode >= 500)
            {
                _logger.LogWarning("Lizenzserver meldet Serverfehler (HTTP {Status}).", status);
                return LicenseCheckResult.Transient($"Lizenzserver meldet Serverfehler (HTTP {status}).", status);
            }

            _logger.LogWarning("Unerwarteter Status vom Lizenzserver: HTTP {Status}.", status);
            return LicenseCheckResult.Transient($"Unerwarteter Status vom Lizenzserver (HTTP {status}).", status);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning("Zeitüberschreitung bei der Lizenzprüfung nach {Elapsed} ms.", stopwatch.ElapsedMilliseconds);
            return LicenseCheckResult.Transient("Zeitüberschreitung bei der Lizenzprüfung.");
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            // Nur die technische Ursache protokollieren, niemals Request-Inhalte.
            _logger.LogWarning(ex, "Lizenzserver nicht erreichbar.");
            return LicenseCheckResult.Transient("Lizenzserver nicht erreichbar.");
        }
    }

    private sealed class ValidateRequestDto
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("product")]
        public string? Product { get; set; }

        [JsonPropertyName("fingerprint")]
        public string? Fingerprint { get; set; }

        [JsonPropertyName("hostname")]
        public string? Hostname { get; set; }
    }

    private sealed class ValidateResponseDto
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("expires_at")]
        public DateTimeOffset? ExpiresAt { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("product")]
        public string? Product { get; set; }

        [JsonPropertyName("policy")]
        public string? Policy { get; set; }

        [JsonPropertyName("in_grace_period")]
        public bool InGracePeriod { get; set; }

        [JsonPropertyName("activation_count")]
        public long? ActivationCount { get; set; }

        [JsonPropertyName("max_activations")]
        public long? MaxActivations { get; set; }
    }
}
