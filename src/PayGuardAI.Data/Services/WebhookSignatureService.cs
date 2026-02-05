using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PayGuardAI.Data.Services;

/// <summary>
/// Service for verifying Afriex webhook signatures.
/// Ensures webhooks are authentic and haven't been tampered with.
/// </summary>
public interface IWebhookSignatureService
{
    /// <summary>
    /// Verifies the signature of an incoming webhook.
    /// </summary>
    /// <param name="signature">Base64-encoded signature from x-webhook-signature header</param>
    /// <param name="rawBody">Raw request body bytes (NOT parsed JSON)</param>
    /// <returns>True if signature is valid, false otherwise</returns>
    bool VerifySignature(string signature, byte[] rawBody);
    
    /// <summary>
    /// Verifies the signature of an incoming webhook.
    /// </summary>
    /// <param name="signature">Base64-encoded signature from x-webhook-signature header</param>
    /// <param name="rawBody">Raw request body string (NOT parsed JSON)</param>
    /// <returns>True if signature is valid, false otherwise</returns>
    bool VerifySignature(string signature, string rawBody);
}

public class WebhookSignatureService : IWebhookSignatureService
{
    private readonly ILogger<WebhookSignatureService> _logger;
    private readonly string _publicKeyPem;
    private readonly bool _verificationEnabled;

    public WebhookSignatureService(IConfiguration configuration, ILogger<WebhookSignatureService> logger)
    {
        _logger = logger;
        
        // Get from configuration
        _publicKeyPem = configuration["Afriex:WebhookPublicKey"] ?? "";
        _verificationEnabled = !string.IsNullOrEmpty(_publicKeyPem);
        
        if (!_verificationEnabled)
        {
            _logger.LogWarning("Webhook signature verification is disabled - no public key configured");
        }
    }

    public bool VerifySignature(string signature, byte[] rawBody)
    {
        if (!_verificationEnabled)
        {
            // If no public key is configured, skip verification (for hackathon demo)
            _logger.LogDebug("Signature verification skipped - not configured");
            return true;
        }
        
        if (string.IsNullOrEmpty(signature))
        {
            _logger.LogWarning("Missing x-webhook-signature header");
            return false;
        }

        try
        {
            // Decode the base64 signature
            var signatureBytes = Convert.FromBase64String(signature);
            
            // Load the public key
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(_publicKeyPem);
            
            // Verify the signature using SHA256
            var isValid = ecdsa.VerifyData(rawBody, signatureBytes, HashAlgorithmName.SHA256);
            
            if (!isValid)
            {
                _logger.LogWarning("Webhook signature verification failed");
            }
            else
            {
                _logger.LogDebug("Webhook signature verified successfully");
            }
            
            return isValid;
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Invalid base64 signature format");
            return false;
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Cryptographic error during signature verification");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during signature verification");
            return false;
        }
    }

    public bool VerifySignature(string signature, string rawBody)
    {
        return VerifySignature(signature, Encoding.UTF8.GetBytes(rawBody));
    }
}
