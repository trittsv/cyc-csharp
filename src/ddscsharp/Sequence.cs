// Copyright(c) 2026 Cyclone DDS C# contributors
//
// This program and the accompanying materials are made available under the
// terms of the Eclipse Public License v. 2.0 which is available at
// http://www.eclipse.org/legal/epl-2.0, or the Eclipse Distribution License
// v. 1.0 which is available at
// http://www.eclipse.org/org/documents/edl-v10.php.
//
// SPDX-License-Identifier: EPL-2.0 OR BSD-3-Clause
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Omg.Types;

/// <summary>IDL4-to-C# sequence interface, based on OMG formal/21-07-01 §7.2.4.2.1.</summary>
public interface ISequence<T> : IList<T>
{
    int Capacity { get; set; }
    void AddRange(IEnumerable<T> collection);
    ReadOnlyCollection<T> AsReadOnly();
    int BinarySearch(int index, int count, T item, IComparer<T>? comparer);
    int BinarySearch(T item);
    int BinarySearch(T item, IComparer<T>? comparer);
    ISequence<TOutput> ConvertAll<TOutput>(Converter<T, TOutput> converter);
    void CopyTo(T[] array);
    void CopyTo(int index, T[] array, int arrayIndex, int count);
    bool Exists(Predicate<T> match);
    [return: MaybeNull] T Find(Predicate<T> match);
    ISequence<T> FindAll(Predicate<T> match);
    int FindIndex(Predicate<T> match);
    int FindIndex(int startIndex, Predicate<T> match);
    int FindIndex(int startIndex, int count, Predicate<T> match);
    [return: MaybeNull] T FindLast(Predicate<T> match);
    int FindLastIndex(Predicate<T> match);
    int FindLastIndex(int startIndex, Predicate<T> match);
    int FindLastIndex(int startIndex, int count, Predicate<T> match);
    void ForEach(Action<T> action);
    int IndexOf(T item, int index);
    int IndexOf(T item, int index, int count);
    void InsertRange(int index, IEnumerable<T> collection);
    int LastIndexOf(T item);
    int LastIndexOf(T item, int index);
    int LastIndexOf(T item, int index, int count);
    int RemoveAll(Predicate<T> match);
    void RemoveRange(int index, int count);
    void Reverse();
    void Reverse(int index, int count);
    void Sort();
    void Sort(IComparer<T>? comparer);
    void Sort(int index, int count, IComparer<T>? comparer);
    T[] ToArray();
    void TrimExcess();
    bool TrueForAll(Predicate<T> match);
}

public sealed class Sequence<T> : List<T>, ISequence<T>
{
    public Sequence() { }
    public Sequence(IEnumerable<T> values) : base(values) { }
    public new ISequence<TOutput> ConvertAll<TOutput>(Converter<T, TOutput> converter) => new Sequence<TOutput>(base.ConvertAll(converter));
    public new ISequence<T> FindAll(Predicate<T> match) => new Sequence<T>(base.FindAll(match));
}
