using System.Text.Json.Serialization;

namespace Company.Function.Models;

public class ReleaseMetadata
{
    private static readonly char[] DisallowedKeyChars = { '/', '\\', '#', '?' };

    [JsonPropertyName("applicationName")]
    public string ApplicationName { get; set; } = string.Empty;

    [JsonPropertyName("anNumber")]
    public string AnNumber { get; set; } = string.Empty;

    [JsonPropertyName("releaseVersion")]
    public string ReleaseVersion { get; set; } = string.Empty;

    [JsonPropertyName("installerType")]
    public string InstallerType { get; set; } = string.Empty;

    [JsonPropertyName("installCommand")]
    public string InstallCommand { get; set; } = string.Empty;

    [JsonPropertyName("uninstallCommand")]
    public string UninstallCommand { get; set; } = string.Empty;

    [JsonPropertyName("detectionType")]
    public string DetectionType { get; set; } = string.Empty;

    [JsonPropertyName("registryHive")]
    public string? RegistryHive { get; set; }

    [JsonPropertyName("registryPath")]
    public string? RegistryPath { get; set; }

    [JsonPropertyName("registryArchitecture")]
    public string? RegistryArchitecture { get; set; }

    [JsonPropertyName("registryRuleType")]
    public string? RegistryRuleType { get; set; }

    [JsonPropertyName("registryValueName")]
    public string? RegistryValueName { get; set; }

    [JsonPropertyName("registryExpectedValue")]
    public string? RegistryExpectedValue { get; set; }

    [JsonPropertyName("msiProductCode")]
    public string? MsiProductCode { get; set; }

    [JsonPropertyName("publisher")]
    public string Publisher { get; set; } = "Nouryon";

    [JsonPropertyName("uatGroup")]
    public string UatGroup { get; set; } = string.Empty;

    [JsonPropertyName("dependencies")]
    public string Dependencies { get; set; } = string.Empty;

    [JsonPropertyName("supersedence")]
    public string Supersedence { get; set; } = string.Empty;

    public List<string> Validate()
    {
        var errors = new List<string>();

        // Required fields
        if (string.IsNullOrWhiteSpace(ApplicationName))
            errors.Add("applicationName is required.");
        else if (ApplicationName.IndexOfAny(DisallowedKeyChars) >= 0)
            errors.Add("applicationName must not contain /, \\, #, or ? characters.");

        if (string.IsNullOrWhiteSpace(AnNumber))
            errors.Add("anNumber is required.");

        if (string.IsNullOrWhiteSpace(ReleaseVersion))
            errors.Add("releaseVersion is required.");
        else if (ReleaseVersion.IndexOfAny(DisallowedKeyChars) >= 0)
            errors.Add("releaseVersion must not contain /, \\, #, or ? characters.");

        // Installer type: EXE, MSI, or ZIP
        if (string.IsNullOrWhiteSpace(InstallerType))
            errors.Add("installerType is required.");
        else if (!InstallerType.Equals("EXE", StringComparison.OrdinalIgnoreCase) &&
                 !InstallerType.Equals("MSI", StringComparison.OrdinalIgnoreCase) &&
                 !InstallerType.Equals("ZIP", StringComparison.OrdinalIgnoreCase))
            errors.Add("installerType must be 'EXE', 'MSI', or 'ZIP'.");

        // MSI-specific: require msiProductCode
        if (!string.IsNullOrWhiteSpace(InstallerType) &&
            InstallerType.Equals("MSI", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(MsiProductCode))
            errors.Add("msiProductCode is required when installerType is 'MSI'.");

        if (string.IsNullOrWhiteSpace(InstallCommand))
            errors.Add("installCommand is required.");
        if (string.IsNullOrWhiteSpace(UninstallCommand))
            errors.Add("uninstallCommand is required.");

        // Detection type and conditional registry validation
        if (string.IsNullOrWhiteSpace(DetectionType))
            errors.Add("detectionType is required.");
        else if (DetectionType.Equals("registry", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(RegistryHive))
                errors.Add("registryHive is required when detectionType is 'registry'.");
            if (string.IsNullOrWhiteSpace(RegistryPath))
                errors.Add("registryPath is required when detectionType is 'registry'.");
            if (string.IsNullOrWhiteSpace(RegistryArchitecture))
                errors.Add("registryArchitecture is required when detectionType is 'registry'.");
            if (string.IsNullOrWhiteSpace(RegistryRuleType))
                errors.Add("registryRuleType is required when detectionType is 'registry'.");
        }

        if (string.IsNullOrWhiteSpace(UatGroup))
            errors.Add("uatGroup is required.");

        // Dependencies format validation
        if (string.IsNullOrWhiteSpace(Dependencies))
            errors.Add("dependencies is required (use 'none' if not applicable).");
        else if (!Dependencies.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var entry in Dependencies.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (entry.Split('|').Length != 2)
                    errors.Add($"Invalid dependency format: '{entry}'. Expected 'AppName|Version'.");
            }
        }

        // Supersedence format validation
        if (string.IsNullOrWhiteSpace(Supersedence))
            errors.Add("supersedence is required (use 'none' if not applicable).");
        else if (!Supersedence.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var entry in Supersedence.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (entry.Split('|').Length != 2)
                    errors.Add($"Invalid supersedence format: '{entry}'. Expected 'AppName|Version'.");
            }
        }

        return errors;
    }
}
