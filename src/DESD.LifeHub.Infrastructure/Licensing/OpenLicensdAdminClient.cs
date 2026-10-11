using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DESD.LifeHub.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DESD.LifeHub.Infrastructure.Licensing;

// ---------------------------------------------------------------- Transporttypen

/// <summary>Ergebnis eines Admin-API-Aufrufs. Unterscheidet fachliche Fehler sauber von technischen.</summary>
public sealed record AdminApiResult<T>
{
    public required bool Succeeded { get; init; }

    public T? Value { get; init; }

    /// <summary>Menschlich lesbare Fehlermeldung (ohne Geheimnisse).</summary>
    public string? Error { get; init; }

    /// <summary>HTTP-Statuscode, sofern überhaupt eine Antwort vorlag.</summary>
    public int? StatusCode { get; init; }

    /// <summary>True, wenn der Server nicht erreichbar war (Timeout/Netzwerk) – kein fachlicher Fehler.</summary>
    public bool Unreachable { get; init; }

    public static AdminApiResult<T> Ok(T value) => new() { Succeeded = true, Value = value };

    public static AdminApiResult<T> Fail(string error, int? statusCode = null, bool unreachable = false)
        => new() { Succeeded = false, Error = error, StatusCode = statusCode, Unreachable = unreachable };
}

/// <summary>OpenLicensd-Produkt (Antwort von <c>GET /api/v1/products</c>).</summary>
public sealed record OpenLicensdProduct(
    string Id,
    string Name,
    string Code,
    string? Description);

/// <summary>OpenLicensd-Richtlinie (Antwort von <c>GET /api/v1/policies</c>).</summary>
public sealed record OpenLicensdPolicy(
    string Id,
    string ProductId,
    string Name,
    int? DurationDays,
    string? ExpirationBasis,
    int? GracePeriodDays,
    int? MaxActivations);

/// <summary>OpenLicensd-Lizenz ohne Rohschlüssel.</summary>
public sealed record OpenLicensdLicense(
    string Id,
    string Label,
    string? KeyPrefix,
    string? ProductId,
    string? ProductCode,
    string? ProductName,
    string? PolicyId,
    string? PolicyName,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? ActivatedAt,
    bool Revoked,
    DateTimeOffset? CreatedAt,
    long? MaxActivations,
    long? ActivationCount);

/// <summary>Antwort von <c>POST /api/v1/licenses</c> – enthält den Rohschlüssel genau einmal.</summary>
public sealed record OpenLicensdCreatedLicense(
    string Id,
    string Label,
    string? KeyPrefix,
    string? ProductId,
    string? ProductCode,
    string? ProductName,
    string? PolicyId,
    string? PolicyName,
    DateTimeOffset? ExpiresAt,
    bool Revoked,
    DateTimeOffset? CreatedAt,
    long? MaxActivations,
    long? ActivationCount,
    string Key);

internal sealed record PagedResponse<T>(IReadOnlyList<T> Items);

internal sealed record CreateLicenseRequest(
    string Label,
    string ProductId,
    string PolicyId,
    DateTimeOffset? ExpiresAt,
    int? MaxActivations);

/// <summary>Eingabe zum Anlegen einer Firmenlizenz in OpenLicensd.</summary>
public sealed record CreateCompanyLicenseInput(
    string Label,
    string ProductId,
    string PolicyId,
    DateTimeOffset? ExpiresAt,
    int? MaxActivations);

/// <summary>Vereinfachte Fehlerantwort von OpenLicensd (<c>{ "error": "..." }</c>).</summary>
internal sealed record OpenLicensdError(string? Error);

/// <summary>
/// Abstraktion der OpenLicensd-Admin-API. Macht die Firmenlizenz-Erzeugung testbar
/// (simulierter Server) und hält das Netzwerk aus der Fachlogik heraus.
/// </summary>
public interface IOpenLicensdAdmin
{
    /// <summary>Admin-API nutzbar (Token gesetzt und Basisadresse vorhanden).</summary>
    bool IsConfigured { get; }

    Task<AdminApiResult<OpenLicensdProduct>> FindProductByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<AdminApiResult<OpenLicensdPolicy>> FindPolicyAsync(string productId, CancellationToken cancellationToken = default);

    Task<AdminApiResult<OpenLicensdCreatedLicense>> CreateLicenseAsync(CreateCompanyLicenseInput input, CancellationToken cancellationToken = default);

    Task<AdminApiResult<OpenLicensdLicense>> RevokeLicenseAsync(string licenseId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Client für die <em>geschützte</em> OpenLicensd-Admin-API. Authentifiziert per
/// <c>Authorization: Bearer &lt;token&gt;</c>; Bearer-Aufrufe unterliegen keinem CSRF.
///
/// Der Client ist ausschliesslich serverseitig im Einsatz. Das API-Token und die
/// erzeugten Rohschlüssel werden niemals protokolliert oder an den Browser gegeben.
/// </summary>
public sealed class OpenLicensdAdminClient : IOpenLicensdAdmin
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly OpenLicensdOptions _options;
    private readonly ILogger<OpenLicensdAdminClient> _logger;

    public OpenLicensdAdminClient(
        HttpClient http,
        IOptions<OpenLicensdOptions> options,
        ILogger<OpenLicensdAdminClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Admin-API nutzbar (Token gesetzt und Basispfade vorhanden).</summary>
    public bool IsConfigured => _options.AdminApiConfigured && _http.BaseAddress is not null;

    // ---------------------------------------------------------------- Auflösung

    /// <summary>Sucht ein Produkt anhand des Produktcodes.</summary>
    public async Task<AdminApiResult<OpenLicensdProduct>> FindProductByCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        var path = $"{_options.ProductsPath}?search={Uri.EscapeDataString(code)}&page_size=100";
        var result = await GetPagedAsync<OpenLicensdProduct>(path, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded || result.Value is null)
        {
            return AdminApiResult<OpenLicensdProduct>.Fail(
                result.Error ?? "Produktliste konnte nicht geladen werden.",
                result.StatusCode,
                result.Unreachable);
        }

        var product = result.Value.FirstOrDefault(p =>
            string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));

        return product is null
            ? AdminApiResult<OpenLicensdProduct>.Fail($"Kein Produkt mit dem Code '{code}' gefunden.")
            : AdminApiResult<OpenLicensdProduct>.Ok(product);
    }

    /// <summary>Lädt die Richtlinien eines Produkts. Bevorzugt die konfigurierte Richtlinien-ID.</summary>
    public async Task<AdminApiResult<OpenLicensdPolicy>> FindPolicyAsync(
        string productId,
        CancellationToken cancellationToken = default)
    {
        var path = $"{_options.PoliciesPath}?product_id={Uri.EscapeDataString(productId)}&page_size=100";
        var result = await GetPagedAsync<OpenLicensdPolicy>(path, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded || result.Value is null)
        {
            return AdminApiResult<OpenLicensdPolicy>.Fail(
                result.Error ?? "Richtlinienliste konnte nicht geladen werden.",
                result.StatusCode,
                result.Unreachable);
        }

        var policies = result.Value;
        OpenLicensdPolicy? policy = null;

        if (!string.IsNullOrWhiteSpace(_options.PolicyId))
        {
            policy = policies.FirstOrDefault(p =>
                string.Equals(p.Id, _options.PolicyId, StringComparison.OrdinalIgnoreCase));
        }

        policy ??= policies.FirstOrDefault();

        return policy is null
            ? AdminApiResult<OpenLicensdPolicy>.Fail("Für dieses Produkt ist keine Richtlinie vorhanden.")
            : AdminApiResult<OpenLicensdPolicy>.Ok(policy);
    }

    // ---------------------------------------------------------------- Schreiben

    /// <summary>Erzeugt eine neue Lizenz. Der zurückgegebene Rohschlüssel wird nur einmal ausgegeben.</summary>
    public async Task<AdminApiResult<OpenLicensdCreatedLicense>> CreateLicenseAsync(
        CreateCompanyLicenseInput input,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return AdminApiResult<OpenLicensdCreatedLicense>.Fail(
                "Die OpenLicensd-Admin-API ist nicht konfiguriert (OpenLicensd:ApiToken fehlt).");
        }

        var payload = new CreateLicenseRequest(
            input.Label,
            input.ProductId,
            input.PolicyId,
            input.ExpiresAt,
            input.MaxActivations);

        try
        {
            using var request = CreateRequest(
                HttpMethod.Post,
                _options.LicensesPath,
                JsonContent.Create(payload, options: JsonOptions));

            using var response = await _http
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return AdminApiResult<OpenLicensdCreatedLicense>.Fail(
                    await DescribeErrorAsync(response, cancellationToken).ConfigureAwait(false),
                    (int)response.StatusCode);
            }

            var created = await response.Content
                .ReadFromJsonAsync<OpenLicensdCreatedLicense>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return created is null
                ? AdminApiResult<OpenLicensdCreatedLicense>.Fail("OpenLicensd lieferte eine leere Antwort.")
                : AdminApiResult<OpenLicensdCreatedLicense>.Ok(created);
        }
        catch (OperationCanceledException)
        {
            return AdminApiResult<OpenLicensdCreatedLicense>.Fail(
                "Zeitüberschreitung beim Erzeugen der Lizenz.", unreachable: true);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "OpenLicensd nicht erreichbar beim Anlegen einer Lizenz.");
            return AdminApiResult<OpenLicensdCreatedLicense>.Fail(
                "Der Lizenzserver ist derzeit nicht erreichbar.", unreachable: true);
        }
    }

    /// <summary>Sperrt eine Lizenz in OpenLicensd (Revisionsspur bleibt erhalten).</summary>
    public async Task<AdminApiResult<OpenLicensdLicense>> RevokeLicenseAsync(
        string licenseId,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return AdminApiResult<OpenLicensdLicense>.Fail(
                "Die OpenLicensd-Admin-API ist nicht konfiguriert (OpenLicensd:ApiToken fehlt).");
        }

        var path = $"{_options.LicensesPath}/{Uri.EscapeDataString(licenseId)}/revoke";

        try
        {
            using var request = CreateRequest(HttpMethod.Patch, path, content: null);

            using var response = await _http
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return AdminApiResult<OpenLicensdLicense>.Fail(
                    await DescribeErrorAsync(response, cancellationToken).ConfigureAwait(false),
                    (int)response.StatusCode);
            }

            var license = await response.Content
                .ReadFromJsonAsync<OpenLicensdLicense>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return license is null
                ? AdminApiResult<OpenLicensdLicense>.Fail("OpenLicensd lieferte eine leere Antwort.")
                : AdminApiResult<OpenLicensdLicense>.Ok(license);
        }
        catch (OperationCanceledException)
        {
            return AdminApiResult<OpenLicensdLicense>.Fail(
                "Zeitüberschreitung beim Sperren der Lizenz.", unreachable: true);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "OpenLicensd nicht erreichbar beim Sperren einer Lizenz.");
            return AdminApiResult<OpenLicensdLicense>.Fail(
                "Der Lizenzserver ist derzeit nicht erreichbar.", unreachable: true);
        }
    }

    // ---------------------------------------------------------------- intern

    /// <summary>
    /// Baut eine Anfrage und setzt das Bearer-Token aus der (nur serverseitigen)
    /// Konfiguration. Dadurch ist der Client unabhängig davon, wie der HttpClient
    /// erzeugt wurde, und das Token gelangt nie in URL, Logs oder Browser.
    /// </summary>
    private HttpRequestMessage CreateRequest(HttpMethod method, string path, HttpContent? content)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };

        if (!string.IsNullOrWhiteSpace(_options.ApiToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);
        }

        return request;
    }

    private async Task<AdminApiResult<IReadOnlyList<T>>> GetPagedAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return AdminApiResult<IReadOnlyList<T>>.Fail(
                "Die OpenLicensd-Admin-API ist nicht konfiguriert (OpenLicensd:ApiToken fehlt).");
        }

        try
        {
            using var request = CreateRequest(HttpMethod.Get, path, content: null);

            using var response = await _http
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return AdminApiResult<IReadOnlyList<T>>.Fail(
                    await DescribeErrorAsync(response, cancellationToken).ConfigureAwait(false),
                    (int)response.StatusCode);
            }

            var page = await response.Content
                .ReadFromJsonAsync<PagedResponse<T>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return AdminApiResult<IReadOnlyList<T>>.Ok(page?.Items ?? []);
        }
        catch (OperationCanceledException)
        {
            return AdminApiResult<IReadOnlyList<T>>.Fail(
                "Zeitüberschreitung beim Abruf.", unreachable: true);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "OpenLicensd nicht erreichbar beim Abruf von {Path}.", path);
            return AdminApiResult<IReadOnlyList<T>>.Fail(
                "Der Lizenzserver ist derzeit nicht erreichbar.", unreachable: true);
        }
    }

    private static async Task<string> DescribeErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return "OpenLicensd hat den Zugriff verweigert. Bitte das API-Token prüfen.";
        }

        string? message = null;
        try
        {
            var error = await response.Content
                .ReadFromJsonAsync<OpenLicensdError>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            message = error?.Error;
        }
        catch
        {
            // Kein JSON-Body – Statuscode genügt.
        }

        return message is null
            ? $"OpenLicensd antwortete mit Status {(int)response.StatusCode}."
            : $"OpenLicensd: {message}";
    }
}
