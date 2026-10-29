using System.Text.Json;

namespace Efr.Core;

public sealed class WindowsBackend : IBackend
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly Func<string, bool> owns;
    private readonly string? expectedSid;
    private Inventory inventory = new();
    private string? inventoryError;
    public MachineInfo Info { get; private set; }
    public WindowsBackend(string? expectedSid, Func<string, bool> owns)
    {
        this.expectedSid = expectedSid; this.owns = owns; Info = Native.Machine(expectedSid);
    }
    public sealed record Package(string Name, string PackageFullName, bool IsFramework, bool IsResourcePackage, bool NonRemovable);
    public sealed record Component(string Name, string State);
    public sealed class Inventory
    {
        public Package[] Packages { get; set; } = [];
        public Component[] Features { get; set; } = [];
        public Component[] Capabilities { get; set; } = [];
        public string ServicingError { get; set; } = "";
    }
    private const string InventoryScript = """
        $packages = @(Get-AppxPackage | Select-Object Name,PackageFullName,IsFramework,IsResourcePackage,NonRemovable)
        $features = @(); $caps = @(); $servicingError = ''
        if ($env:EFR_ADMIN -eq '1') {
            try {
                $features = @(Get-WindowsOptionalFeature -Online | ForEach-Object { @{Name=$_.FeatureName;State=$_.State.ToString()} })
                $caps = @(Get-WindowsCapability -Online | ForEach-Object { @{Name=$_.Name;State=$_.State.ToString()} })
            } catch { $servicingError = $_.Exception.Message }
        }
        @{Packages=$packages; Features=$features; Capabilities=$caps; ServicingError=$servicingError} | ConvertTo-Json -Depth 5 -Compress
        """;
    public async Task RefreshAsync()
    {
        Info = Native.Machine(expectedSid);
        try
        {
            var output = await Commands.PowerShellAsync(InventoryScript, new Dictionary<string,string> { ["EFR_ADMIN"] = Info.Admin ? "1" : "0" }, true);
            inventory = JsonSerializer.Deserialize<Inventory>(output, Json) ?? throw new IOException("Inventário vazio.");
            inventoryError = null;
        }
        catch (Exception ex) { inventoryError = ex.Message; }
    }
    public Task<Detection> DetectAsync(Adjustment item)
    {
        if (!Info.Supported) return Task.FromResult(new Detection(State.Unavailable, "Requer Windows 11 x64 cliente."));
        if (item.Kind == OperationKind.Guide) return Task.FromResult(new Detection(State.Unavailable, item.UnavailableReason!));
        if (item.EnterpriseOnly && !(Info.Edition.Contains("Enterprise") || Info.Edition.Contains("Education")))
            return Task.FromResult(new Detection(State.Unavailable, "A política documentada não é suportada nesta edição: " + Info.Edition));
        if (item.ProOnly && !(Info.Edition.StartsWith("Professional") || Info.Edition.Contains("Enterprise") || Info.Edition.Contains("Education")))
            return Task.FromResult(new Detection(State.Unavailable, "Requer Pro, Enterprise ou Education."));
        if (item.Kind == OperationKind.NetFx && Info.Build >= 28000)
        {
            var installed = RegistryStore.Text("HKLM", @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5", "Install") == "1";
            return Task.FromResult(new Detection(installed ? State.Applied : State.Unavailable,
                installed ? ".NET 3.5 instalado." : "Esta build exige o instalador separado da Microsoft. Abra a orientação oficial; não use NetFx3/DISM."));
        }
        Detection state;
        switch (item.Kind)
        {
            case OperationKind.Registry:
                var all = item.Values!.Select(v => (Target:v, Actual:RegistryStore.Read(v))).ToArray();
                var blocked = PolicyConflict(item);
                state = blocked != null ? new(State.Unavailable, blocked)
                    : new(all.All(v => v.Actual == v.Target.Desired) ? State.Applied : State.NotApplied,
                        "Estado dos valores do Registro. A interface do Windows pode exigir nova sessão; políticas podem prevalecer.");
                if (item.Id.StartsWith("edge.") && !EdgePresent()) state = new(State.Unavailable, "Microsoft Edge não detectado.");
                break;
            case OperationKind.Mouse:
                state = new(Native.Mouse().SequenceEqual(new[] {0,0,0}) ? State.Applied : State.NotApplied, "Estado consultado pela API do Windows."); break;
            case OperationKind.StickyKeys:
                state = new((Native.Sticky() & 5) == 0 ? State.Applied : State.NotApplied, "Recurso e atalho de Shift; outros bits serão preservados."); break;
            case OperationKind.Appx:
                if (inventoryError != null) { state = new(State.Unknown, inventoryError); break; }
                var packages = Packages(item);
                state = packages.Any(p => p.IsFramework || p.IsResourcePackage || p.NonRemovable)
                    ? new(State.Unavailable, "Pacote protegido, de recursos ou dependência: remoção bloqueada.")
                    : new(packages.Length == 0 ? State.Applied : State.NotApplied, packages.Length == 0 ? "Ausente para esta conta; provisionamento e outras contas não foram alterados." : "Instalado nesta conta: " + string.Join(", ", packages.Select(p => p.PackageFullName)));
                break;
            case OperationKind.Capability:
            case OperationKind.Feature:
            case OperationKind.NetFx:
                if (!Info.Admin) { state = new(State.Unknown, "Reabra como administrador da mesma conta para consultar recursos do sistema."); break; }
                if (inventoryError != null || inventory.ServicingError.Length > 0) { state = new(State.Unknown, inventoryError ?? inventory.ServicingError); break; }
                var components = Components(item);
                if (components.Length == 0) { state = new(State.Unavailable, "Este recurso não existe na imagem atual."); break; }
                if (components.Any(c => c.State.Contains("Pending", StringComparison.OrdinalIgnoreCase))) { state = new(State.Unknown, "Há uma alteração pendente. Reinicie o computador manualmente e consulte novamente."); break; }
                bool applied = item.Kind == OperationKind.NetFx ? components.All(c => c.State == "Enabled")
                    : components.All(c => c.State is "Disabled" or "DisabledWithPayloadRemoved" or "NotPresent" or "Removed");
                state = new(applied ? State.Applied : State.NotApplied, string.Join("; ", components.Select(c => c.Name + ": " + c.State)));
                break;
            case OperationKind.Power:
                state = new(PowerPlans.Active() == item.Target ? State.Applied : State.NotApplied, "Plano ativo consultado pela API do Windows."); break;
            case OperationKind.UltimatePower:
                state = new(PowerPlans.IsUltimateActive() ? State.Applied : State.NotApplied, "Plano atual consultado pela API. A disponibilidade de criação depende do Windows/hardware e será verificada na aplicação; falhas preservam o plano anterior."); break;
            case OperationKind.Install:
                var winget = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe"));
                state = new(Installed(item.Target) ? State.Applied : State.NotApplied, Installed(item.Target) ? "Programa/dependência já detectado; não será reinstalado." : "Instalação requer internet e administrador. Download oficial com assinatura verificada. " + (winget ? "WinGet detectado, mas o método usado é o instalador oficial." : "WinGet indisponível: será usado o instalador oficial direto.")); break;
            default: state = new(State.Unavailable, "Operação desconhecida."); break;
        }
        return Task.FromResult(state);
    }
    private string? PolicyConflict(Adjustment item)
    {
        if (item.Values == null) return null;
        foreach (var v in item.Values)
        {
            if (v.Path.Contains(@"\Policies\", StringComparison.OrdinalIgnoreCase)
                && RegistryStore.Read(v).Exists && !owns(item.Id))
                return "Política preexistente: o EFR não assume controle nem sobrescreve configurações de terceiros.";
        }
        return null;
    }
    public void AssertCanWrite(Adjustment item)
    {
        Info = Native.Machine(expectedSid);
        if (!Info.Supported) throw new InvalidOperationException("Requer Windows 11 x64.");
        if (!Info.IdentityMatches) throw new InvalidOperationException("A conta do processo não corresponde à sessão do Explorador. Abra normalmente na conta desejada; elevação com outro administrador é bloqueada.");
        if (Info.Managed) throw new InvalidOperationException(Info.ManagementDetail);
        if (item.Admin && !Info.Admin) throw new UnauthorizedAccessException("Reabra como administrador da mesma conta e revise a seleção novamente.");
        if (item.Kind == OperationKind.Guide) throw new InvalidOperationException("Este item é apenas uma orientação manual.");
        var conflict = PolicyConflict(item);
        if (conflict != null) throw new InvalidOperationException(conflict);
    }
    public Task<Snapshot> CaptureAsync(Adjustment item)
    {
        var registry = item.Values?.ToDictionary(Engine.Key, RegistryStore.Read) ?? [];
        string native = item.Kind switch
        {
            OperationKind.Mouse => JsonSerializer.Serialize(Native.Mouse()),
            OperationKind.StickyKeys => Native.Sticky().ToString(System.Globalization.CultureInfo.InvariantCulture),
            OperationKind.Power => PowerPlans.Active(),
            OperationKind.UltimatePower => JsonSerializer.Serialize(new PowerPlans.SavedState(PowerPlans.Active(), PowerPlans.List().Any(p => p.Id == PowerPlans.ManagedUltimate))),
            _ => ""
        };
        return Task.FromResult(new Snapshot(registry, native));
    }
    public async Task<OperationResult> ApplyAsync(Adjustment item, ApplyOptions options)
    {
        AssertCanWrite(item);
        switch (item.Kind)
        {
            case OperationKind.Registry:
                foreach (var target in item.Values!) RegistryStore.Write(target, target.Desired);
                return new(true, "Valores gravados e leitura posterior será verificada. Efeito: " + item.Restart);
            case OperationKind.Mouse: Native.SetMouse([0,0,0]); return new(true, "Aceleração do mouse desativada pela API.");
            case OperationKind.StickyKeys: Native.SetSticky(Native.Sticky() & ~5); return new(true, "Teclas de Aderência e atalho desativados; demais flags preservadas.");
            case OperationKind.Power: PowerPlans.Set(item.Target); return new(true, "Plano ativo alterado. Monitore bateria e temperatura.");
            case OperationKind.UltimatePower: await PowerPlans.ActivateUltimateAsync(); return new(true, "Desempenho Máximo ativado e confirmado, sem duplicar cópias do EFR. Monitore bateria e temperatura.");
            case OperationKind.Install: return await Installers.InstallAsync(item.Target);
            case OperationKind.Appx:
                await Commands.PowerShellAsync("""
                    $packages = @(Get-AppxPackage -Name $env:EFR_TARGET | Where-Object { $_.Name -eq $env:EFR_TARGET })
                    foreach ($package in $packages) {
                        if ($package.IsFramework -or $package.IsResourcePackage -or $package.NonRemovable) { throw 'Pacote protegido; remoção recusada.' }
                        Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop
                    }
                    if (@(Get-AppxPackage -Name $env:EFR_TARGET | Where-Object { $_.Name -eq $env:EFR_TARGET }).Count -ne 0) { throw 'O pacote ainda está registrado nesta conta.' }
                    """, Env(item));
                return new(true, "Pacote removido desta conta; provisionamento e outras contas preservados. Recuperação pela Store.");
            case OperationKind.Feature:
                return await ServicingAsync("$r = Disable-WindowsOptionalFeature -Online -FeatureName $env:EFR_TARGET -NoRestart -ErrorAction Stop; @{Reboot=[bool]$r.RestartNeeded} | ConvertTo-Json -Compress", Env(item));
            case OperationKind.Capability:
                return await ServicingAsync("""
                    $reboot = $false
                    $caps = @(Get-WindowsCapability -Online | Where-Object { ($_.Name -split '~')[0] -eq $env:EFR_TARGET -and $_.State -eq 'Installed' })
                    foreach ($cap in $caps) { $r = Remove-WindowsCapability -Online -Name $cap.Name -NoRestart -ErrorAction Stop; $reboot = $reboot -or $r.RestartNeeded }
                    @{Reboot=[bool]$reboot} | ConvertTo-Json -Compress
                    """, Env(item));
            case OperationKind.NetFx:
                if (Info.Build >= 28000) throw new InvalidOperationException("Esta build exige o instalador separado da Microsoft.");
                var env = Env(item);
                if (!string.IsNullOrWhiteSpace(options.NetFxSource))
                {
                    var source = Path.GetFullPath(options.NetFxSource);
                    if (!options.SourceConfirmed || !Directory.Exists(source)) throw new InvalidOperationException("Selecione e confirme uma fonte compatível com a build, arquitetura e idioma.");
                    if (!Directory.EnumerateFiles(source, "*netfx3*.cab").Any()) throw new InvalidOperationException("Nenhum pacote NetFx3 CAB encontrado na pasta escolhida.");
                    env["EFR_SOURCE"] = source;
                }
                return await ServicingAsync("""
                    if ($env:EFR_SOURCE) { $r = Enable-WindowsOptionalFeature -Online -FeatureName NetFx3 -All -Source $env:EFR_SOURCE -LimitAccess -NoRestart -ErrorAction Stop }
                    else { $r = Enable-WindowsOptionalFeature -Online -FeatureName NetFx3 -All -NoRestart -ErrorAction Stop }
                    @{Reboot=[bool]$r.RestartNeeded} | ConvertTo-Json -Compress
                    """, env);
            default: throw new InvalidOperationException("Operação não implementada para escrita.");
        }
    }
    private static Dictionary<string,string> Env(Adjustment item) => new() { ["EFR_TARGET"] = item.Target, ["EFR_SOURCE"] = "" };
    private static async Task<OperationResult> ServicingAsync(string script, Dictionary<string,string> environment)
    {
        var output = await Commands.PowerShellAsync(script, environment);
        var result = JsonSerializer.Deserialize<ServicingResult>(output, Json) ?? throw new IOException("DISM retornou uma resposta inválida.");
        return new(true, result.Reboot ? "Windows solicitou reinicialização. A validação definitiva requer reiniciar manualmente." : "Operação de recursos concluída; estado final será consultado.", result.Reboot);
    }
    private sealed record ServicingResult(bool Reboot);
    public Task RestoreAsync(Adjustment item, Snapshot snapshot)
    {
        AssertCanWrite(item);
        if (item.Kind == OperationKind.Mouse) Native.SetMouse(JsonSerializer.Deserialize<int[]>(snapshot.Native)!);
        if (item.Kind == OperationKind.StickyKeys) Native.SetSticky(int.Parse(snapshot.Native, System.Globalization.CultureInfo.InvariantCulture));
        if (item.Kind == OperationKind.Power) PowerPlans.Set(snapshot.Native);
        if (item.Kind == OperationKind.UltimatePower) PowerPlans.RestoreUltimate(JsonSerializer.Deserialize<PowerPlans.SavedState>(snapshot.Native) ?? throw new IOException("Backup de energia inválido."));
        if (item.Values != null)
            foreach (var target in item.Values) RegistryStore.Write(target, snapshot.Registry[Engine.Key(target)]);
        return Task.CompletedTask;
    }
    private Package[] Packages(Adjustment item) => inventory.Packages.Where(p => p.Name.Equals(item.Target, StringComparison.OrdinalIgnoreCase)).ToArray();
    private Component[] Components(Adjustment item) => item.Kind == OperationKind.Capability
        ? inventory.Capabilities.Where(c => c.Name.Split('~')[0] == item.Target).ToArray()
        : inventory.Features.Where(c => c.Name == item.Target).ToArray();
    private static bool EdgePresent() => new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
        .Any(folder => File.Exists(Path.Combine(Environment.GetFolderPath(folder), @"Microsoft\Edge\Application\msedge.exe")));
    public static bool Installed(string target)
    {
        if (target == "VisualCpp") return RegistryStore.Text("HKLM", @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64", "Installed") == "1";
        if (target == "NotepadPlusPlus") return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Any(folder => File.Exists(Path.Combine(Environment.GetFolderPath(folder), @"Notepad++\notepad++.exe"))) || RegistryStore.Exists("HKLM", @"SOFTWARE\Notepad++");
        if (target != "Firefox") return false;
        return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData }
            .Any(folder => File.Exists(Path.Combine(Environment.GetFolderPath(folder), @"Mozilla Firefox\firefox.exe")))
            || RegistryStore.Exists("HKLM", @"SOFTWARE\Mozilla\Mozilla Firefox") || RegistryStore.Exists("HKCU", @"SOFTWARE\Mozilla\Mozilla Firefox");
    }
    public async Task<string> CreateRestorePointAsync()
    {
        AssertCanWrite(new("system.restorepoint", "Ponto de restauração", "", "", OperationKind.Feature, true));
        return await Commands.PowerShellAsync("""
            $description = 'estetele-format-reset ' + [guid]::NewGuid().ToString()
            Checkpoint-Computer -Description $description -RestorePointType MODIFY_SETTINGS -WarningAction Stop -ErrorAction Stop
            $created = @(Get-ComputerRestorePoint | Where-Object { $_.Description -eq $description })
            if ($created.Count -eq 0) { throw 'Nenhum ponto novo foi confirmado. A proteção pode estar desabilitada ou no limite de frequência.' }
            'Ponto criado e confirmado: ' + $created[0].SequenceNumber
            """);
    }
}
