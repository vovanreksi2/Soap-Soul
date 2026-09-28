using 'main.bicep'

param name = 'soapandsoul'

// To reuse the old site's plan (must be Linux):
// param appServicePlanId = '/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.Web/serverfarms/<plan>'

// To pick the host name yourself instead of app-soapandsoul-<hash>:
// param webAppName = 'soapandsoul'
