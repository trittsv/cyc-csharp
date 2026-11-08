// Copyright(c) 2022 ZettaScale Technology and others
// Copyright(c) 2026 Cyclone DDS C# contributors
//
// This program and the accompanying materials are made available under the
// terms of the Eclipse Public License v. 2.0 which is available at
// http://www.eclipse.org/legal/epl-2.0, or the Eclipse Distribution License
// v. 1.0 which is available at
// http://www.eclipse.org/org/documents/edl-v10.php.
//
// SPDX-License-Identifier: EPL-2.0 OR BSD-3-Clause
namespace Org.Eclipse.CycloneDDS.Examples.RoundTrip;

internal sealed class Statistics
{
    private readonly List<double> latency = [], writes = [], reads = [];
    internal int Count => latency.Count;
    internal void Add(double halfRoundTrip, double writeAccess, double readAccess)
    {
        latency.Add(halfRoundTrip); writes.Add(writeAccess); reads.Add(readAccess);
    }
    internal void Print(string label)
    {
        if (Count == 0) { Console.WriteLine($"{label} Count: 0"); return; }
        Console.WriteLine($"{label} Count: {Count} | Half RTT us {Describe(latency)} | Write-access us {Describe(writes)} | Read-access us {Describe(reads)}");
    }
    internal void Clear() { latency.Clear(); writes.Clear(); reads.Clear(); }
    private static string Describe(List<double> values)
    {
        var sorted = values.ToArray(); Array.Sort(sorted);
        return FormattableString.Invariant($"median={sorted[sorted.Length / 2]:F2} min={sorted[0]:F2} p99={sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.99))]:F2} max={sorted[^1]:F2}");
    }
}
