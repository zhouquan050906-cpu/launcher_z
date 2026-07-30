/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.ObjectModel;

namespace Launcher.App.ViewModels.Shared;

internal static class ObservableCollectionExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
            collection.Add(item);
    }

    public static bool ReplaceWithIfChanged<T>(this ObservableCollection<T> collection, IReadOnlyList<T> items)
    {
        if (collection.Count == items.Count)
        {
            var isSame = true;
            for (var index = 0; index < items.Count; index++)
            {
                if (!EqualityComparer<T>.Default.Equals(collection[index], items[index]))
                {
                    isSame = false;
                    break;
                }
            }

            if (isSame)
                return false;
        }

        collection.ReplaceWith(items);
        return true;
    }

    public static bool SynchronizeByKey<T, TKey>(
        this ObservableCollection<T> collection,
        IReadOnlyList<T> items,
        Func<T, TKey> keySelector,
        IEqualityComparer<TKey>? comparer = null)
    {
        comparer ??= EqualityComparer<TKey>.Default;
        var changed = false;

        for (var targetIndex = 0; targetIndex < items.Count; targetIndex++)
        {
            var desired = items[targetIndex];
            var desiredKey = keySelector(desired);
            if (targetIndex < collection.Count
                && comparer.Equals(keySelector(collection[targetIndex]), desiredKey))
            {
                if (!ReferenceEquals(collection[targetIndex], desired))
                {
                    collection[targetIndex] = desired;
                    changed = true;
                }
                continue;
            }

            var existingIndex = -1;
            for (var candidateIndex = targetIndex + 1; candidateIndex < collection.Count; candidateIndex++)
            {
                if (comparer.Equals(keySelector(collection[candidateIndex]), desiredKey))
                {
                    existingIndex = candidateIndex;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                collection.Move(existingIndex, targetIndex);
                changed = true;
                if (!ReferenceEquals(collection[targetIndex], desired))
                    collection[targetIndex] = desired;
            }
            else
            {
                collection.Insert(targetIndex, desired);
                changed = true;
            }
        }

        while (collection.Count > items.Count)
        {
            collection.RemoveAt(collection.Count - 1);
            changed = true;
        }

        return changed;
    }
}

