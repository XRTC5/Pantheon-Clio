using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace Clio.Core;

/// <summary>
/// Launch at start, via a logon-triggered scheduled task, for the reasons
/// recorded in Argus's Startup.cs: the Run key queues behind Explorer's
/// startup throttle (Argus measured 123s from power-on), a logon trigger does not.
///
/// Same two load-bearing settings as Argus: InteractiveToken (S4U would run
/// on a desktop nobody can see) and normal priority (a task defaults to 7).
///
/// One difference: Argus registers through the TaskScheduler COM library.
/// Clio hands schtasks.exe a task XML instead, so that library never loads
/// into a process whose whole point is to stay small.
/// </summary>
internal static class Startup
{
    private const string TaskName = "Clio";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled() => RegisteredCommand() is not null || RunKeyValue() is not null;

    public static void Set(bool enabled)
    {
        if (!enabled)
        {
            Schtasks($"/Delete /TN \"{TaskName}\" /F");
            RemoveRunKey();
            Log.Info("startup removed");
            return;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        if (RegisterTask(exe))
        {
            RemoveRunKey();
            Log.Info($"startup registered as scheduled task: {exe}");
            return;
        }

        // Late beats never: if the scheduler refuses, fall back to the Run key.
        SetRunKey(exe);
        Log.Warn($"task registration refused; fell back to the Run key: {exe}");
    }

    /// <summary>Brings the machine in line with the setting, and repairs a stale path after a rebuild elsewhere.</summary>
    public static void Sync(bool wanted)
    {
        try
        {
            var registered = RegisteredCommand();
            if (!wanted)
            {
                if (registered is not null || RunKeyValue() is not null) Set(false);
                return;
            }

            var exe = Environment.ProcessPath;
            if (registered is null || !string.Equals(registered.Trim('"'), exe, StringComparison.OrdinalIgnoreCase)
                || RunKeyValue() is not null)
            {
                Set(true);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"startup sync failed: {ex.Message}");
        }
    }

    private static bool RegisterTask(string exe)
    {
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>Clio</Author>
                <Description>Starts Clio at logon, ahead of Explorer's Run key throttle.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
                <StartWhenAvailable>false</StartWhenAvailable>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{SecurityElement.Escape(exe)}"</Command>
                  <WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exe))}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;

        var file = Path.Combine(Path.GetTempPath(), "clio-task.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            return Schtasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F").Code == 0;
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    /// <summary>The command the registered task runs, or null if there is no task.</summary>
    private static string? RegisteredCommand()
    {
        var (code, output) = Schtasks($"/Query /TN \"{TaskName}\" /XML");
        if (code != 0) return null;

        var start = output.IndexOf("<Command>", StringComparison.Ordinal);
        var end = output.IndexOf("</Command>", StringComparison.Ordinal);
        return start < 0 || end < start ? "" : output[(start + 9)..end];
    }

    private static (int Code, string Output) Schtasks(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (p is null) return (-1, "");
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            return (p.ExitCode, output);
        }
        catch (Exception ex)
        {
            Log.Warn($"schtasks {args.Split(' ')[0]} failed: {ex.Message}");
            return (-1, "");
        }
    }

    private static string? RunKeyValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(TaskName) as string;
    }

    private static void SetRunKey(string exe)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.SetValue(TaskName, $"\"{exe}\"");
    }

    private static void RemoveRunKey()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(TaskName, throwOnMissingValue: false);
    }
}
