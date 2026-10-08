using System.Security.Cryptography.X509Certificates;

namespace RedNote.AppHost.Extensions;

internal static class DevelopmentCertificateExtensions
{
    public static string ExportDevelopmentCertificate(this IDistributedApplicationBuilder builder)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var certificate = store.Certificates
            .Where(certificate => certificate.NotBefore <= DateTime.Now && certificate.NotAfter > DateTime.Now
                && certificate.Extensions.Any(extension => extension.Oid?.Value == "1.3.6.1.4.1.311.84.1.1"))
            .OrderByDescending(certificate => certificate.NotAfter)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Run 'dotnet dev-certs https --trust' before starting local HTTPS.");

        // Export only the public certificate for Node's additional CA trust store.
        var path = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "artifacts", "certificates", "localhost.pem"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, certificate.ExportCertificatePem());
        return path;
    }
}
