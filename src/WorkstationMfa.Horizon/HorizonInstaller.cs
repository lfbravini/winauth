using System.Diagnostics;
using Microsoft.Win32;

namespace WorkstationMfa.Horizon;

public sealed class HorizonInstaller : IHorizonInstaller
{
    public HorizonStatus GetStatus()
    {
        foreach (var root in new[]
                 {
                     RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
                     RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                 })
        {
            using (root)
            {
                var status = FindUninstall(root, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (status is not null)
                    return status;

                status = FindUninstall(root, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
                if (status is not null)
                    return status;
            }
        }

        var defaultPaths = new[]
        {
            @"C:\Program Files\Omnissa\Omnissa Horizon Client\omnissa-horizon-client.exe",
            @"C:\Program Files\VMware\VMware Horizon View Client\vmware-view.exe",
            @"C:\Program Files (x86)\VMware\VMware Horizon View Client\vmware-view.exe"
        };

        foreach (var path in defaultPaths)
        {
            if (File.Exists(path))
            {
                var ver = FileVersionInfo.GetVersionInfo(path).FileVersion;
                return new HorizonStatus
                {
                    State = HorizonInstallState.Installed,
                    Version = ver,
                    InstallPath = Path.GetDirectoryName(path),
                    DisplayName = "Horizon Client"
                };
            }
        }

        return new HorizonStatus { State = HorizonInstallState.NotInstalled };
    }

    public async Task<HorizonStatus> InstallOrUpgradeAsync(HorizonInstallOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!File.Exists(options.MediaPath))
            throw new FileNotFoundException("Horizon Client installer not found.", options.MediaPath);

        var current = GetStatus();
        if (current.State == HorizonInstallState.Installed && !options.AllowUpgrade)
            return current;

        if (options.WhatIf)
            return current;

        var args = "/silent /norestart";
        if (!string.IsNullOrWhiteSpace(options.Broker))
            args += $" VDM_SERVER={options.Broker}";

        var psi = new ProcessStartInfo
        {
            FileName = options.MediaPath,
            Arguments = args,
            UseShellExecute = true,
            Verb = "runas"
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Horizon installer.");

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Horizon installer exited with code {process.ExitCode}.");

        return GetStatus();
    }

    private static HorizonStatus? FindUninstall(RegistryKey root, string subPath)
    {
        using var key = root.OpenSubKey(subPath);
        if (key is null)
            return null;

        foreach (var name in key.GetSubKeyNames())
        {
            using var sub = key.OpenSubKey(name);
            if (sub is null)
                continue;

            var display = sub.GetValue("DisplayName") as string;
            if (string.IsNullOrWhiteSpace(display))
                continue;

            if (!display.Contains("Horizon Client", StringComparison.OrdinalIgnoreCase)
                && !display.Contains("VMware Horizon", StringComparison.OrdinalIgnoreCase)
                && !display.Contains("Omnissa Horizon", StringComparison.OrdinalIgnoreCase))
                continue;

            return new HorizonStatus
            {
                State = HorizonInstallState.Installed,
                Version = sub.GetValue("DisplayVersion") as string,
                InstallPath = sub.GetValue("InstallLocation") as string,
                DisplayName = display
            };
        }

        return null;
    }
}
