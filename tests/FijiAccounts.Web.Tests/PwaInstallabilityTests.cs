using System.Buffers.Binary;
using System.Text.Json;

namespace FijiAccounts.Web.Tests;

public sealed class PwaInstallabilityTests
{
    [Fact]
    public void ManifestProvidesChromeInstallIconsAndStableHomeIdentity()
    {
        var webRoot = Path.Combine(RepositoryRoot(), "src", "FijiAccounts.Web", "wwwroot");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(webRoot, "account-island.webmanifest")));
        var root = manifest.RootElement;

        Assert.Equal("/", root.GetProperty("id").GetString());
        Assert.Equal("/", root.GetProperty("start_url").GetString());
        Assert.Equal("/", root.GetProperty("scope").GetString());
        Assert.Equal("standalone", root.GetProperty("display").GetString());

        var icons = root.GetProperty("icons").EnumerateArray().ToArray();
        AssertIcon(webRoot, icons, "192x192", 192);
        AssertIcon(webRoot, icons, "512x512", 512);
    }

    [Fact]
    public void InstallControlCapturesChromePromptAndRegistersOfflineSupport()
    {
        var webRoot = Path.Combine(RepositoryRoot(), "src", "FijiAccounts.Web", "wwwroot");
        var installScript = File.ReadAllText(Path.Combine(webRoot, "install-app.js"));
        var serviceWorker = File.ReadAllText(Path.Combine(webRoot, "service-worker.js"));

        Assert.Contains("beforeinstallprompt", installScript, StringComparison.Ordinal);
        Assert.Contains("navigator.serviceWorker.register", installScript, StringComparison.Ordinal);
        Assert.Contains("event.request.mode !== \"navigate\"", serviceWorker, StringComparison.Ordinal);
        Assert.DoesNotContain("cache.put", serviceWorker, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertIcon(string webRoot, JsonElement[] icons, string size, int expectedPixels)
    {
        var icon = Assert.Single(icons, item => item.GetProperty("sizes").GetString() == size);
        var relativePath = icon.GetProperty("src").GetString()!.Split('?', 2)[0].TrimStart('/');
        var path = Path.Combine(webRoot, relativePath);
        Assert.True(File.Exists(path), $"Manifest icon does not exist: {relativePath}");

        var header = File.ReadAllBytes(path).AsSpan();
        Assert.True(header.Length >= 24, $"Manifest icon is not a readable PNG: {relativePath}");
        Assert.Equal(expectedPixels, BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4)));
        Assert.Equal(expectedPixels, BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4)));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FijiAccounts.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the Account Island repository root.");
    }
}
