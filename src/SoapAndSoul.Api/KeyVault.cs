using Azure.Core;
using Azure.Identity;

namespace SoapAndSoul.Api;

internal static class KeyVault
{
    /// <summary>
    /// Adds Azure Key Vault secrets to configuration when <c>KeyVault:Uri</c> is set (production). Secret names
    /// use <c>--</c> for <c>:</c>, so the secret <c>Llm--ApiKey</c> becomes <c>Llm:ApiKey</c>. Secrets are read
    /// once at startup: restart the app after changing one.
    /// </summary>
    public static void AddKeyVaultSecrets(this WebApplicationBuilder builder)
    {
        if (builder.Configuration["KeyVault:Uri"] is not { Length: > 0 } uri) return;
        TokenCredential credential = builder.Configuration["KeyVault:ManagedIdentityClientId"] is { Length: > 0 } clientId
            ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId))
            : new DefaultAzureCredential();
        builder.Configuration.AddAzureKeyVault(new Uri(uri), credential);
    }
}
