using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PayGuardAI.Core.Services;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Service for interacting with the Afriex Business API.
/// Handles customers, transactions, rates, and payment methods.
/// </summary>
public interface IAfriexApiService
{
    // Customers
    Task<AfriexCustomer?> CreateCustomerAsync(CreateCustomerRequest request);
    Task<List<AfriexCustomer>> GetCustomersAsync(int page = 0, int limit = 10);
    Task<AfriexCustomer?> GetCustomerByIdAsync(string customerId);
    
    // Transactions
    Task<AfriexTransaction?> CreateTransactionAsync(CreateTransactionRequest request);
    Task<List<AfriexTransaction>> GetTransactionsAsync(int page = 0, int limit = 10);
    Task<AfriexTransaction?> GetTransactionByIdAsync(string transactionId);
    
    // Rates
    Task<ExchangeRateResponse?> GetExchangeRateAsync(string fromCurrency, string toCurrency, decimal amount);
    
    // Balance
    Task<WalletBalanceResponse?> GetWalletBalanceAsync();
    
    // Payment Methods
    Task<List<AfriexPaymentMethod>> GetPaymentMethodsAsync(string customerId);
    Task<AfriexPaymentMethod?> CreatePaymentMethodAsync(CreatePaymentMethodRequest request);
}

public class AfriexApiService : IAfriexApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AfriexApiService> _logger;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly IMemoryCache _cache;
    private readonly ITenantContext _tenantContext;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AfriexApiService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<AfriexApiService> logger,
        IMemoryCache cache,
        ITenantContext tenantContext)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cache = cache;
        _tenantContext = tenantContext;
        
        // Get from configuration (appsettings.json or environment variables)
        _apiKey = configuration["Afriex:ApiKey"] ?? "";
        _baseUrl = configuration["Afriex:BaseUrl"] ?? "https://staging.afx-server.com";
        
        _httpClient.BaseAddress = new Uri(_baseUrl);
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }
    
    #region Customers
    
    public async Task<AfriexCustomer?> CreateCustomerAsync(CreateCustomerRequest request)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/v1/customer", request, JsonOptions);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AfriexApiResponse<AfriexCustomer>>(JsonOptions);
                return result?.Data;
            }
            
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to create customer: {StatusCode} - {Error}", response.StatusCode, error);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating customer");
            return null;
        }
    }
    
    public async Task<List<AfriexCustomer>> GetCustomersAsync(int page = 0, int limit = 10)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<AfriexPagedResponse<AfriexCustomer>>(
                $"/api/v1/customer?page={page}&limit={limit}", JsonOptions);
            return response?.Data ?? new List<AfriexCustomer>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching customers");
            return new List<AfriexCustomer>();
        }
    }
    
    public async Task<AfriexCustomer?> GetCustomerByIdAsync(string customerId)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<AfriexApiResponse<AfriexCustomer>>(
                $"/api/v1/customer/{customerId}", JsonOptions);
            return response?.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching customer {CustomerId}", customerId);
            return null;
        }
    }
    
    #endregion
    
    #region Transactions
    
    public async Task<AfriexTransaction?> CreateTransactionAsync(CreateTransactionRequest request)
    {
        try
        {
            _logger.LogInformation("Creating transaction: {Amount} {Currency} -> {DestCurrency}", 
                request.DestinationAmount, request.SourceCurrency, request.DestinationCurrency);
                
            var response = await _httpClient.PostAsJsonAsync("/api/v1/transaction", request, JsonOptions);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AfriexApiResponse<AfriexTransaction>>(JsonOptions);
                _logger.LogInformation("Transaction created: {TransactionId}", result?.Data?.TransactionId);
                return result?.Data;
            }
            
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to create transaction: {StatusCode} - {Error}", response.StatusCode, error);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating transaction");
            return null;
        }
    }
    
    public async Task<List<AfriexTransaction>> GetTransactionsAsync(int page = 0, int limit = 10)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<AfriexPagedResponse<AfriexTransaction>>(
                $"/api/v1/transaction?page={page}&limit={limit}", JsonOptions);
            return response?.Data ?? new List<AfriexTransaction>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching transactions");
            return new List<AfriexTransaction>();
        }
    }
    
    public async Task<AfriexTransaction?> GetTransactionByIdAsync(string transactionId)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<AfriexApiResponse<AfriexTransaction>>(
                $"/api/v1/transaction/{transactionId}", JsonOptions);
            return response?.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching transaction {TransactionId}", transactionId);
            return null;
        }
    }
    
    #endregion
    
    #region Rates
    
    public async Task<ExchangeRateResponse?> GetExchangeRateAsync(string fromCurrency, string toCurrency, decimal amount)
    {
        try
        {
            var cacheKey = $"{_tenantContext.TenantId}:rate:{fromCurrency}:{toCurrency}:{amount}";
            if (_cache.TryGetValue(cacheKey, out ExchangeRateResponse? cached))
            {
                return cached;
            }

            // Use /v2/public/rates endpoint as per documentation
            var response = await _httpClient.GetFromJsonAsync<ExchangeRateApiResponse>(
                $"/v2/public/rates?base={fromCurrency}&symbols={toCurrency}", JsonOptions);
            
            if (response?.Rates != null && response.Rates.TryGetValue(fromCurrency, out var ratesForBase))
            {
                if (ratesForBase.TryGetValue(toCurrency, out var rateString) && decimal.TryParse(rateString, out var rate))
                {
                    var result = new ExchangeRateResponse
                    {
                        From = fromCurrency,
                        To = toCurrency,
                        Rate = rate,
                        SourceAmount = amount,
                        DestinationAmount = amount * rate
                    };

                    _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
                    return result;
                }
            }
            
            _logger.LogWarning("Rate not found for {From} -> {To}", fromCurrency, toCurrency);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching exchange rate {From} -> {To}", fromCurrency, toCurrency);
            return null;
        }
    }
    
    #endregion
    
    #region Balance
    
    public async Task<WalletBalanceResponse?> GetWalletBalanceAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<AfriexApiResponse<WalletBalanceResponse>>(
                "/api/v1/balance", JsonOptions);
            return response?.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching wallet balance");
            return null;
        }
    }
    
    #endregion
    
    #region Payment Methods
    
    public async Task<List<AfriexPaymentMethod>> GetPaymentMethodsAsync(string customerId)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<AfriexPagedResponse<AfriexPaymentMethod>>(
                $"/api/v1/payment-method?customerId={customerId}", JsonOptions);
            return response?.Data ?? new List<AfriexPaymentMethod>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching payment methods for {CustomerId}", customerId);
            return new List<AfriexPaymentMethod>();
        }
    }
    
    public async Task<AfriexPaymentMethod?> CreatePaymentMethodAsync(CreatePaymentMethodRequest request)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/v1/payment-method", request, JsonOptions);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AfriexApiResponse<AfriexPaymentMethod>>(JsonOptions);
                return result?.Data;
            }
            
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to create payment method: {StatusCode} - {Error}", response.StatusCode, error);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating payment method");
            return null;
        }
    }
    
    #endregion
}

#region API Response Models

public class AfriexApiResponse<T>
{
    public T? Data { get; set; }
}

public class AfriexPagedResponse<T>
{
    public List<T> Data { get; set; } = new();
    public int Page { get; set; }
    public int Total { get; set; }
}

public class AfriexCustomer
{
    public string CustomerId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public Dictionary<string, string>? Kyc { get; set; }
    public Dictionary<string, object>? Meta { get; set; }
}

public class CreateCustomerRequest
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public Dictionary<string, string>? Kyc { get; set; }
    public Dictionary<string, object>? Meta { get; set; }
}

public class AfriexTransaction
{
    public string TransactionId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string? DestinationId { get; set; }
    public string SourceAmount { get; set; } = "0";
    public string SourceCurrency { get; set; } = "";
    public string DestinationAmount { get; set; } = "0";
    public string DestinationCurrency { get; set; } = "";
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public Dictionary<string, object>? Meta { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateTransactionRequest
{
    public string CustomerId { get; set; } = "";
    public string DestinationAmount { get; set; } = "";
    public string DestinationCurrency { get; set; } = "";
    public string SourceCurrency { get; set; } = "";
    public string DestinationId { get; set; } = "";
    public TransactionMeta? Meta { get; set; }
}

public class TransactionMeta
{
    public string MerchantId { get; set; } = "";
    public string? IdempotencyKey { get; set; }
    public string? Narration { get; set; }
}

public class ExchangeRateApiResponse
{
    public Dictionary<string, Dictionary<string, string>> Rates { get; set; } = new();
    public List<string> Base { get; set; } = new();
    public long UpdatedAt { get; set; }
}

public class ExchangeRateResponse
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public decimal Rate { get; set; }
    public decimal SourceAmount { get; set; }
    public decimal DestinationAmount { get; set; }
}

public class WalletBalanceResponse
{
    public List<WalletBalance> Balances { get; set; } = new();
}

public class WalletBalance
{
    public string Currency { get; set; } = "";
    public decimal Available { get; set; }
    public decimal Pending { get; set; }
}

public class AfriexPaymentMethod
{
    public string PaymentMethodId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string Channel { get; set; } = "";
    public string? AccountNumber { get; set; }
    public string? AccountName { get; set; }
    public string CountryCode { get; set; } = "";
    public InstitutionInfo? Institution { get; set; }
    public Dictionary<string, object>? Meta { get; set; }
}

public class InstitutionInfo
{
    public string InstitutionName { get; set; } = "";
    public string InstitutionCode { get; set; } = "";
    public string? InstitutionId { get; set; }
    public string? InstitutionAddress { get; set; }
}

public class CreatePaymentMethodRequest
{
    public string CustomerId { get; set; } = "";
    public string Channel { get; set; } = "BANK_ACCOUNT";
    public string AccountName { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public InstitutionInfo Institution { get; set; } = new();
}

#endregion
