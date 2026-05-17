using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ThinkBridge_ERP.Data;
using ThinkBridge_ERP.Models.Entities;
using ThinkBridge_ERP.Services.Interfaces;

namespace ThinkBridge_ERP.Services;

public class PayMongoService : IPayMongoService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<PayMongoService> _logger;

    private const string PAYMONGO_API_URL = "https://api.paymongo.com/v1";

    public PayMongoService(
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ApplicationDbContext context,
        ILogger<PayMongoService> logger)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Creates a PayMongo Checkout Session and stores the payment transaction record.
    /// </summary>
    public async Task<PayMongoCheckoutResult> CreateCheckoutSessionAsync(PayMongoCheckoutRequest request)
    {
        try
        {
            var secretKey = GetPayMongoSecretKey();
            if (string.IsNullOrEmpty(secretKey))
            {
                _logger.LogWarning(
                    "PayMongo SecretKey not configured. Environment={Environment}. Expected config key PayMongo:SecretKey (User Secrets/appsettings.Development.json) or env vars PayMongo__SecretKey/PAYMONGO_SECRET_KEY.",
                    _config["ASPNETCORE_ENVIRONMENT"] ?? _config["DOTNET_ENVIRONMENT"] ?? "(unknown)");
                return new PayMongoCheckoutResult
                {
                    Success = false,
                    ErrorMessage = "PayMongo is not configured. Set PayMongo:SecretKey (User Secrets / appsettings.Development.json) or env var PayMongo__SecretKey / PAYMONGO_SECRET_KEY."
                };
            }

            _logger.LogInformation(
                "PayMongo SecretKey configured (masked). Environment={Environment}.",
                _config["ASPNETCORE_ENVIRONMENT"] ?? _config["DOTNET_ENVIRONMENT"] ?? "(unknown)");

            var client = _httpClientFactory.CreateClient();
            var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{secretKey}:"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

            // Amount in centavos (PHP cents)
            var amountInCentavos = (int)(request.Amount * 100);

            // Redirect URLs
            // Prefer explicit configuration (PayMongo:SuccessUrl/CancelUrl) so local dev can force https://localhost:7009.
            // Fallback to request.BaseUrl so it works in any environment.
            var configuredSuccessUrl = _config["PayMongo:SuccessUrl"];
            var configuredCancelUrl = _config["PayMongo:CancelUrl"];

            string successUrl;
            string cancelUrl;
            if (!string.IsNullOrWhiteSpace(configuredSuccessUrl) && !string.IsNullOrWhiteSpace(configuredCancelUrl))
            {
                successUrl = configuredSuccessUrl.TrimEnd('/');
                cancelUrl = configuredCancelUrl.TrimEnd('/');
            }
            else
            {
                var baseUrl = (request.BaseUrl ?? string.Empty).TrimEnd('/');
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    return new PayMongoCheckoutResult
                    {
                        Success = false,
                        ErrorMessage = "Payment redirect URL is not configured. Set PayMongo:SuccessUrl/CancelUrl or provide a valid BaseUrl."
                    };
                }

                successUrl = $"{baseUrl}/Subscription/PaymentSuccess";
                cancelUrl = $"{baseUrl}/Subscription/PaymentCancelled";
            }

            var payload = new
            {
                data = new
                {
                    attributes = new
                    {
                        send_email_receipt = true,
                        show_description = true,
                        show_line_items = true,
                        description = request.Description,
                        line_items = new[]
                        {
                            new
                            {
                                currency = "PHP",
                                amount = amountInCentavos,
                                name = request.Description,
                                quantity = 1
                            }
                        },
                        payment_method_types = new[] { "gcash", "grab_pay", "card", "paymaya" },
                        success_url = AppendQuery(successUrl, "subscription_id", request.SubscriptionId.ToString()),
                        cancel_url = AppendQuery(cancelUrl, "subscription_id", request.SubscriptionId.ToString()),
                        metadata = new
                        {
                            subscription_id = request.SubscriptionId.ToString(),
                            company_name = request.CompanyName
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{PAYMONGO_API_URL}/checkout_sessions", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayMongo API error: {StatusCode} - {Body}", response.StatusCode, responseBody);
                return new PayMongoCheckoutResult
                {
                    Success = false,
                    ErrorMessage = "Failed to create payment session. Please try again."
                };
            }

            // Parse response to get checkout URL and session ID
            using var doc = JsonDocument.Parse(responseBody);
            var data = doc.RootElement.GetProperty("data");
            var checkoutSessionId = data.GetProperty("id").GetString();
            var checkoutUrl = data.GetProperty("attributes").GetProperty("checkout_url").GetString();

            // Create PaymentTransaction record
            var paymentTransaction = new PaymentTransaction
            {
                SubscriptionID = request.SubscriptionId,
                Provider = "PayMongo",
                CheckoutSessionID = checkoutSessionId,
                Amount = request.Amount,
                Currency = "PHP",
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };
            _context.PaymentTransactions.Add(paymentTransaction);

            // Create Invoice record
            var invoiceNumber = $"INV-{DateTime.UtcNow:yyyy}-{DateTime.UtcNow:MMdd}-{request.SubscriptionId:D4}";
            var invoice = new Invoice
            {
                SubscriptionID = request.SubscriptionId,
                InvoiceNumber = invoiceNumber,
                Amount = request.Amount,
                DueDate = DateTime.UtcNow.AddDays(7),
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };
            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();

            // Link invoice to payment
            paymentTransaction.InvoiceID = invoice.InvoiceID;
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "PayMongo checkout session created: {SessionId} for subscription {SubId}",
                checkoutSessionId, request.SubscriptionId);

            return new PayMongoCheckoutResult
            {
                Success = true,
                CheckoutUrl = checkoutUrl,
                CheckoutSessionId = checkoutSessionId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating PayMongo checkout session");
            return new PayMongoCheckoutResult
            {
                Success = false,
                ErrorMessage = "Payment system error. Please try again later."
            };
        }
    }

    private string? GetPayMongoSecretKey()
    {
        return _config["PayMongo:SecretKey"]
            ?? _config["PAYMONGO_SECRET_KEY"]
            ?? _config["PAYMONGO_SECRETKEY"]
            ?? _config["PAYMONGO_SECRET"];
    }

    private static string AppendQuery(string url, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        var separator = url.Contains('?') ? "&" : "?";
        return $"{url}{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
    }

    /// <summary>
    /// Processes a PayMongo webhook event. Validates the event and extracts checkout session info.
    /// </summary>
    public async Task<PayMongoWebhookResult> ProcessWebhookAsync(string payload, string signatureHeader)
    {
        try
        {
            // Parse the webhook payload
            using var doc = JsonDocument.Parse(payload);
            var data = doc.RootElement.GetProperty("data");
            var attributes = data.GetProperty("attributes");
            var eventType = attributes.GetProperty("type").GetString();

            _logger.LogInformation("PayMongo webhook received: {EventType}", eventType);

            // We care about checkout_session.payment.paid
            if (eventType != "checkout_session.payment.paid")
            {
                return new PayMongoWebhookResult
                {
                    Success = true,
                    EventType = eventType,
                    ErrorMessage = "Event type not handled."
                };
            }

            // Extract the checkout session data
            var webhookData = attributes.GetProperty("data");
            var checkoutSessionId = webhookData.GetProperty("id").GetString();

            // Extract payment method and payment ID if available
            var paymentAttributes = webhookData.GetProperty("attributes");
            string? paymentMethod = null;
            string? paymentId = null;

            if (paymentAttributes.TryGetProperty("payment_method_used", out var pmUsed))
            {
                paymentMethod = pmUsed.GetString();
            }

            // Extract PayMongo payment ID from the payments array
            if (paymentAttributes.TryGetProperty("payments", out var payments)
                && payments.ValueKind == JsonValueKind.Array
                && payments.GetArrayLength() > 0)
            {
                var firstPayment = payments[0];
                if (firstPayment.TryGetProperty("id", out var pid))
                {
                    paymentId = pid.GetString();
                }
            }

            // Update payment transaction with method info and external transaction ID
            if (!string.IsNullOrEmpty(checkoutSessionId))
            {
                var payment = await _context.PaymentTransactions
                    .FirstOrDefaultAsync(p => p.CheckoutSessionID == checkoutSessionId);
                if (payment != null)
                {
                    if (!string.IsNullOrEmpty(paymentMethod))
                        payment.PaymentMethod = paymentMethod;
                    if (!string.IsNullOrEmpty(paymentId))
                        payment.ExternalTransactionID = paymentId;
                    await _context.SaveChangesAsync();
                }
            }

            return new PayMongoWebhookResult
            {
                Success = true,
                CheckoutSessionId = checkoutSessionId,
                EventType = eventType,
                PaymentId = paymentId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing PayMongo webhook");
            return new PayMongoWebhookResult
            {
                Success = false,
                ErrorMessage = "Failed to process webhook."
            };
        }
    }
}
