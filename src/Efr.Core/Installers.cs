using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Efr.Core;

public static class Installers
{
    public record Prepared(string Target, string File, string Directory, string Hash, string Publisher);
    private static readonly Dictionary<string,Prepared> Cache = [];
    public static async Task<Prepared> PrepareAsync(string target)
    {
        if (target is not ("Firefox" or "VisualCpp" or "NotepadPlusPlus")) throw new InvalidOperationException("Instalador não autorizado pelo catálogo.");
        if (Cache.TryGetValue(target,out var cached) && File.Exists(cached.File))
        {
            using var heldCache=new FileStream(cached.File,FileMode.Open,FileAccess.Read,FileShare.Read);
            if (!Convert.ToHexString(await SHA256.HashDataAsync(heldCache)).Equals(cached.Hash,StringComparison.OrdinalIgnoreCase)) throw new IOException("O instalador em cache foi alterado. Feche o EFR e valide novamente.");
            await VerifySignatureAsync(cached.File,cached.Publisher);
            return cached;
        }
        var firefox = target == "Firefox";
        var notepad = target == "NotepadPlusPlus";
        var url = firefox ? "https://download.mozilla.org/?product=firefox-latest-ssl&os=win64&lang=pt-BR" : "https://aka.ms/vs/17/release/vc_redist.x64.exe";
        string? expectedHash = null;
        if (notepad) (url, expectedHash) = await ResolveNotepadAsync();
        var directory = Path.Combine(Path.GetTempPath(), "EFR-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "installer.exe");
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(15) };
            var uri = new Uri(url);
            for (var redirects = 0; ; redirects++)
            {
                if (redirects > 8 || !Allowed(uri, target)) throw new IOException("Redirecionamento para origem não autorizada. Use o link oficial da orientação.");
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    uri = response.Headers.Location == null ? throw new IOException("Redirecionamento sem destino.") : new Uri(uri, response.Headers.Location);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var destination = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(15));
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await source.ReadAsync(buffer, deadline.Token)) != 0)
                {
                    total += count;
                    if (total > 600L * 1024 * 1024) throw new IOException("Download excedeu o limite de 600 MB.");
                    await destination.WriteAsync(buffer.AsMemory(0, count), deadline.Token);
                }
                break;
            }
            // Keep an exclusive-to-writers handle through validation and process creation to avoid replacement.
            using var held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (expectedHash != null && !Convert.ToHexString(await SHA256.HashDataAsync(held)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("O SHA-256 não corresponde ao arquivo publicado no repositório oficial. Instalação cancelada.");
            held.Position=0;
            var hash=Convert.ToHexString(await SHA256.HashDataAsync(held));
            var publisher=firefox ? "Mozilla Corporation" : notepad ? "NOTEPAD++" : "Microsoft Corporation";
            await VerifySignatureAsync(file,publisher);
            var prepared=new Prepared(target,file,directory,hash,publisher);
            Cache[target]=prepared;
            return prepared;
        }
        catch (Exception ex)
        {
            DeleteOwned(file,directory);
            if(ex is HttpRequestException) throw new IOException("Falha de rede no download oficial. Verifique a conexão ou use a orientação. "+ex.Message,ex);
            throw;
        }
    }
    private static Task<string> VerifySignatureAsync(string file,string publisher) => Commands.PowerShellAsync("""
                $sig = Get-AuthenticodeSignature -LiteralPath $env:EFR_INSTALLER
                if ($sig.Status -ne 'Valid' -or $null -eq $sig.SignerCertificate) { throw ('Assinatura inválida: ' + $sig.Status) }
                $publisher = $sig.SignerCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
                if ($publisher -ne $env:EFR_PUBLISHER) { throw ('Publicador inesperado: ' + $publisher) }
                """, new Dictionary<string,string> { ["EFR_INSTALLER"] = file, ["EFR_PUBLISHER"] = publisher }, true);
    public static async Task<OperationResult> InstallAsync(string target)
    {
        if (WindowsBackend.Installed(target)) return new(true,"Já instalado.");
        var prepared=await PrepareAsync(target);
        try
        {
            using var held=new FileStream(prepared.File,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(!Convert.ToHexString(await SHA256.HashDataAsync(held)).Equals(prepared.Hash,StringComparison.OrdinalIgnoreCase)) throw new IOException("Instalador alterado após validação.");
            await VerifySignatureAsync(prepared.File,prepared.Publisher);
            var result = await Commands.RunAsync(prepared.File, target == "Firefox" ? ["/S", "/PreventRebootRequired=true"] : target == "NotepadPlusPlus" ? ["/S"] : ["/install", "/quiet", "/norestart"]);
            // 1641 means a reboot was initiated, and is deliberately NOT an accepted success here.
            if (result.ExitCode is not (0 or 3010)) throw new IOException($"Instalador retornou código {result.ExitCode}. {result.Error}");
            if (!WindowsBackend.Installed(target)) throw new IOException("O instalador terminou, mas a presença do programa/dependência não foi confirmada.");
            return new(true, "Instalação e presença verificadas." + (result.ExitCode == 3010 ? " Reinicialização manual necessária." : ""), result.ExitCode == 3010);
        }
        finally
        {
            Cache.Remove(target); DeleteOwned(prepared.File,prepared.Directory);
        }
    }
    public static void Cleanup()
    {
        foreach(var p in Cache.Values) DeleteOwned(p.File,p.Directory);
        Cache.Clear();
    }
    private static void DeleteOwned(string file,string directory)
    {
        // Never traverse a user's directory: only the explicit EFR temporary installer is deleted.
        try { File.Delete(file); Directory.Delete(directory,false); } catch(IOException) { } catch(UnauthorizedAccessException) { }
    }
    public static bool Allowed(Uri uri, string target)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0) return false;
        if (target == "NotepadPlusPlus") return uri.Host == "release-assets.githubusercontent.com" ||
            (uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/notepad-plus-plus/notepad-plus-plus/releases/download/", StringComparison.Ordinal));
        if (target is not ("Firefox" or "VisualCpp")) return false;
        string[] hosts = target == "Firefox" ? ["download.mozilla.org", "download-installer.cdn.mozilla.net", "archive.mozilla.org"]
            : ["aka.ms", "download.visualstudio.microsoft.com", "download.microsoft.com", "builds.dotnet.microsoft.com"];
        return hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }
    private static async Task<(string Url,string Hash)> ResolveNotepadAsync()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("estetele-format-reset/0.1.0");
        using var response = await client.GetAsync("https://api.github.com/repos/notepad-plus-plus/notepad-plus-plus/releases/latest");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (json.RootElement.GetProperty("prerelease").GetBoolean() || json.RootElement.GetProperty("draft").GetBoolean()) throw new IOException("A resposta não corresponde a uma versão estável publicada.");
        foreach (var asset in json.RootElement.GetProperty("assets").EnumerateArray())
        {
            if (!Regex.IsMatch(asset.GetProperty("name").GetString() ?? "", @"^npp\.[0-9.]+\.Installer\.x64\.exe$", RegexOptions.IgnoreCase)) continue;
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            var digest = asset.GetProperty("digest").GetString() ?? "";
            if (!Allowed(new Uri(url), "NotepadPlusPlus") || !Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$")) throw new IOException("Release sem URL oficial ou SHA-256 válido.");
            return (url,digest[7..]);
        }
        throw new IOException("Instalador x64 não encontrado na release oficial. Use o link de orientação.");
    }
}
