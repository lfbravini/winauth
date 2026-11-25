using System.Text.Json;
using WorkstationMfa.Core.Models;
using WorkstationMfa.Core.Security;
using WorkstationMfa.Engine;
using WorkstationMfa.Profiles;

namespace WorkstationMfa.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        try
        {
            return command switch
            {
                "onboard" => await OnboardAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "revoke" => await RevokeAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                _ => FailUnknown(command)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return (int)ExitCode.GeneralFailure;
        }
    }

    private static int FailUnknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintHelp();
        return (int)ExitCode.GeneralFailure;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            WorkstationMfa.Cli — silent / unattended host

            Commands:
              onboard   Provision/replace token, optional Horizon install, local bind
              revoke    Delete managed TOTP token(s); optional clear local credential

            onboard options:
              --profile <path>          Connection profile JSON
              --profile-url <url|unc>   External profile
              --user <username>         Target PI/AD username (without realm ok)
              --pi-user <admin>         PrivacyIDEA admin username
              --pi-pass-env <VAR>       Env var holding admin password (preferred)
              --pi-pass-stdin           Read admin password from stdin
              --install-horizon         Ensure Horizon Client from bundled media
              --replace-token           Required when a TOTP already exists
              --verify                  Post-bind /validate/check (default on)
              --no-verify               Skip verification
              --what-if                 Dry run

            Exit codes: 0 ok, 10 auth, 20 profile, 30 horizon, 40 enroll, 50 bind, 60 verify, 70 replace-required
            """);
    }

    private static async Task<int> OnboardAsync(string[] args)
    {
        var opts = Parse(args);
        if (string.IsNullOrWhiteSpace(opts.User) || string.IsNullOrWhiteSpace(opts.PiUser))
        {
            Console.Error.WriteLine("--user and --pi-user are required.");
            return (int)ExitCode.GeneralFailure;
        }

        var password = ResolvePassword(opts);
        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("Admin password required via --pi-pass-env or --pi-pass-stdin.");
            return (int)ExitCode.AuthFailed;
        }

        var engine = new ManagementEngine();
        var resolved = await engine.Profiles.ResolveAsync(opts.Profile, opts.ProfileUrl).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(resolved.Warning))
            Console.Error.WriteLine(resolved.Warning);

        var result = await engine.OnboardAsync(new OnboardRequest
        {
            Profile = resolved.Profile,
            AdminUsername = opts.PiUser!,
            AdminPassword = password,
            TargetUsername = opts.User!,
            InstallHorizon = opts.InstallHorizon,
            ReplaceToken = opts.ReplaceToken,
            Verify = opts.Verify,
            WhatIf = opts.WhatIf,
            HorizonMediaPath = opts.HorizonMedia
        }).ConfigureAwait(false);

        password = null;
        WriteResult(result);
        return (int)result.ExitCode;
    }

    private static async Task<int> RevokeAsync(string[] args)
    {
        var opts = Parse(args);
        if (string.IsNullOrWhiteSpace(opts.User) || string.IsNullOrWhiteSpace(opts.PiUser))
        {
            Console.Error.WriteLine("--user and --pi-user are required.");
            return (int)ExitCode.GeneralFailure;
        }

        var password = ResolvePassword(opts);
        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("Admin password required via --pi-pass-env or --pi-pass-stdin.");
            return (int)ExitCode.AuthFailed;
        }

        var engine = new ManagementEngine();
        var resolved = await engine.Profiles.ResolveAsync(opts.Profile, opts.ProfileUrl).ConfigureAwait(false);
        var session = await engine.AuthenticateAsync(resolved.Profile, opts.PiUser!, password).ConfigureAwait(false);
        password = null;

        var result = await engine.RevokeAsync(resolved.Profile, session, opts.User!, clearLocal: true, opts.WhatIf)
            .ConfigureAwait(false);
        WriteResult(result);
        return (int)result.ExitCode;
    }

    private static string? ResolvePassword(CliOptions opts)
    {
        if (!string.IsNullOrWhiteSpace(opts.PiPassEnv))
            return Environment.GetEnvironmentVariable(opts.PiPassEnv);

        if (opts.PiPassStdin)
        {
            if (Console.IsInputRedirected)
                return Console.In.ReadLine();
            var secure = ReadPassword();
            return SecureMemory.SecureStringToString(secure);
        }

        return null;
    }

    private static System.Security.SecureString ReadPassword()
    {
        var s = new System.Security.SecureString();
        Console.Error.Write("PI admin password: ");
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (s.Length > 0) s.RemoveAt(s.Length - 1);
                continue;
            }
            s.AppendChar(key.KeyChar);
        }
        Console.Error.WriteLine();
        s.MakeReadOnly();
        return s;
    }

    private static void WriteResult(OperationResult result)
    {
        // Never include seeds
        var payload = new
        {
            result.Success,
            exitCode = (int)result.ExitCode,
            result.Message,
            result.User,
            result.OldSerial,
            result.NewSerial,
            result.Hostname,
            result.HorizonVersion,
            result.WhatIf,
            result.Actions,
            result.Timestamp
        };
        Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i - 1]}");
            switch (args[i])
            {
                case "--profile": o.Profile = Next(); break;
                case "--profile-url": o.ProfileUrl = Next(); break;
                case "--user": o.User = Next(); break;
                case "--pi-user": o.PiUser = Next(); break;
                case "--pi-pass-env": o.PiPassEnv = Next(); break;
                case "--pi-pass-stdin": o.PiPassStdin = true; break;
                case "--install-horizon": o.InstallHorizon = true; break;
                case "--replace-token":
                case "--force": o.ReplaceToken = true; break;
                case "--verify": o.Verify = true; break;
                case "--no-verify": o.Verify = false; break;
                case "--what-if": o.WhatIf = true; break;
                case "--horizon-media": o.HorizonMedia = Next(); break;
            }
        }
        return o;
    }

    private sealed class CliOptions
    {
        public string? Profile { get; set; }
        public string? ProfileUrl { get; set; }
        public string? User { get; set; }
        public string? PiUser { get; set; }
        public string? PiPassEnv { get; set; }
        public bool PiPassStdin { get; set; }
        public bool InstallHorizon { get; set; }
        public bool ReplaceToken { get; set; }
        public bool Verify { get; set; } = true;
        public bool WhatIf { get; set; }
        public string? HorizonMedia { get; set; }
    }
}
