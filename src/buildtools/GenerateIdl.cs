// Copyright(c) 2026 Cyclone DDS C# contributors
//
// This program and the accompanying materials are made available under the
// terms of the Eclipse Public License v. 2.0 which is available at
// http://www.eclipse.org/legal/epl-2.0, or the Eclipse Distribution License
// v. 1.0 which is available at
// http://www.eclipse.org/org/documents/edl-v10.php.
//
// SPDX-License-Identifier: EPL-2.0 OR BSD-3-Clause
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Org.Eclipse.CycloneDDS.BuildTasks;

/// <summary>Runs the packaged host compiler and tracks the complete preprocessed IDL input.</summary>
public sealed class GenerateIdl : Microsoft.Build.Utilities.Task
{
    [Required] public ITaskItem[] Sources { get; set; } = [];
    [Required] public string OutputDirectory { get; set; } = "";
    [Required] public string PackageDirectory { get; set; } = "";
    public string ToolDirectory { get; set; } = "";
    public ITaskItem[] IncludeDirectories { get; set; } = [];
    public ITaskItem[] Defines { get; set; } = [];
    public string DefaultExtensibility { get; set; } = "final";
    [Output] public ITaskItem[] GeneratedSources { get; private set; } = [];
    [Output] public ITaskItem[] WrittenFiles { get; private set; } = [];

    public override bool Execute()
    {
        try { return Generate(); }
        catch (Exception error) { Log.LogError($"Cyclone DDS IDL generation failed: {error.Message}"); return false; }
    }
    private bool Generate()
    {
        if (DefaultExtensibility is not ("final" or "appendable" or "mutable"))
            throw new ArgumentException("CycloneDDSDefaultExtensibility must be final, appendable or mutable.");
        var root = Path.GetFullPath(OutputDirectory);
        Directory.CreateDirectory(root);
        if (Sources.Length == 0)
        {
            // Removing the last IDL must clean stale code without requiring host tools.
            foreach (var file in Directory.EnumerateFiles(root))
                if (file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".cs.sha256", StringComparison.Ordinal))
                    File.Delete(file);
            return true;
        }
        var tools = string.IsNullOrEmpty(ToolDirectory)
            ? Path.Combine(PackageDirectory, "tools", System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, "native")
            : Path.GetFullPath(ToolDirectory);
        var windows = OperatingSystem.IsWindows();
        var executable = Path.Combine(tools, windows ? "idlc.exe" : "idlc");
        var plugin = Path.Combine(tools, windows ? "cycloneddsidlcsharp.dll" : OperatingSystem.IsMacOS()
            ? "libcycloneddsidlcsharp.dylib" : "libcycloneddsidlcsharp.so");
        if (!File.Exists(executable) || !File.Exists(plugin))
            throw new FileNotFoundException($"The Org.Eclipse.CycloneDDS package has no IDL compiler for build host {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}. Expected tools in {tools}.");
        if (!windows)
            File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute);
        var fingerprint = new StringBuilder();
        // Include the task and the entire tool distribution, so replacing any binary invalidates outputs.
        foreach (var file in Directory.EnumerateFiles(tools).Append(typeof(GenerateIdl).Assembly.Location).Order(StringComparer.Ordinal))
            fingerprint.Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
        var generated = new List<ITaskItem>();
        var written = new List<ITaskItem>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Sources)
        {
            var source = Path.GetFullPath(item.ItemSpec);
            var stem = Path.GetFileNameWithoutExtension(source);
            if (!names.Add(stem)) throw new ArgumentException($"IDL basenames must be unique within a project: {stem}.");
            var output = Path.Combine(root, stem + ".cs");
            var stamp = output + ".sha256";
            var arguments = new List<string> { "-l", plugin, "-x", DefaultExtensibility };
            foreach (var include in IncludeDirectories) { arguments.Add("-I"); arguments.Add(Path.GetFullPath(include.ItemSpec)); }
            foreach (var define in Defines) { arguments.Add("-D"); arguments.Add(define.ItemSpec); }
            // The native preprocessor resolves conditional and macro includes. Hashing its output
            // tracks included files without guessing dependencies with a regular expression.
            var preprocessed = Run(executable, arguments.Concat(["-E", source]), false);
            if (preprocessed is null) return false;
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                fingerprint.ToString() + string.Join('\0', arguments) + source + preprocessed)));
            if (!File.Exists(output) || !File.Exists(stamp) || File.ReadAllText(stamp) != digest)
            {
                Log.LogMessage(MessageImportance.High, $"Cyclone DDS: generating {Path.GetFileName(source)}");
                // Stage each source; a failed compiler run cannot leave partial C# in obj.
                var temporary = Path.Combine(root, ".stage-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporary);
                try
                {
                    if (Run(executable, arguments.Concat(["-o", temporary, source]), true) is null) return false;
                    var result = Path.Combine(temporary, stem + ".cs");
                    if (!File.Exists(result) || Directory.EnumerateFiles(temporary).Any(p => Path.GetExtension(p) != ".cs"))
                        throw new InvalidOperationException("The IDL backend must generate C# source exclusively.");
                    File.Move(result, output, true);
                    File.WriteAllText(stamp, digest);
                }
                finally { Directory.Delete(temporary, true); }
            }
            generated.Add(new TaskItem(output)); written.Add(new TaskItem(output)); written.Add(new TaskItem(stamp));
        }
        // Delete outputs of removed IDL items before adding generated sources to Compile.
        var expected = written.Select(i => i.ItemSpec).ToHashSet(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root))
            if ((file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".cs.sha256", StringComparison.Ordinal)) && !expected.Contains(file))
                File.Delete(file);
        GeneratedSources = generated.ToArray(); WrittenFiles = written.ToArray();
        return !Log.HasLoggedErrors;
    }
    private string? Run(string executable, IEnumerable<string> arguments, bool diagnostics)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start packaged idlc.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var output = stdout.GetAwaiter().GetResult(); var errors = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0) { Log.LogError($"idlc exited with code {process.ExitCode}: {errors}{output}"); return null; }
        if (diagnostics && errors.Length != 0) Log.LogMessage(MessageImportance.Normal, errors.TrimEnd());
        return output;
    }
}
