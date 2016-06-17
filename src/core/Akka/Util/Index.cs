﻿//-----------------------------------------------------------------------
// <copyright file="Index.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Util.Internal;
using Akka.Util.Internal.Collections;

namespace Akka.Util
{
    /// <summary>
    /// An implementation of a ConcurrentMultiMap - in CLR that would be something like
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/> where <c>TValue</c> is another <see cref="IEnumerable{T}"/>.
    /// 
    /// Add/remove is serialized over the specified key.
    /// Reads are fully concurrent.
    /// </summary>
    public class Index<TKey, TValue> where TValue : IComparable<TValue>
    {
        public Index()
        {
            _container = new ConcurrentDictionary<TKey, ConcurrentSet<TValue>>();
        }

        private readonly ConcurrentDictionary<TKey, ConcurrentSet<TValue>> _container;
        private readonly ConcurrentSet<TValue> _emptySet = new ConcurrentSet<TValue>();

        /// <summary>
        /// Associates the value of <typeparam name="TValue"></typeparam> with key of type <typeparam name="TKey"></typeparam>.
        /// </summary>
        /// <param name="key">The key to add.</param>
        /// <param name="value">The value to add.</param>
        /// <returns><c>true</c> if the value didn't exist for the key previously, and <c>false</c> otherwise.</returns>
        public bool Put(TKey key, TValue value)
        {
            var retry = false;
            var added = false;

            // iterative spin-locking put
            do
            {
                ConcurrentSet<TValue> set;
                if (_container.TryGetValue(key, out set))
                {
                    if (set.IsEmpty) retry = true; //IF the set is empty then it has been removed, so signal retry
                    else //Else add the value to the set and signal that retry is not needed
                    {
                        added = set.TryAdd(value);
                        retry = false;
                    }
                }
                else
                {
                    var newSet = new ConcurrentSet<TValue>();
                    newSet.TryAdd(value);

                    // Parry for two simultaneous "TryAdd(id,newSet)"
                    var oldSet = _container.GetOrAdd(key, newSet);
                    if (oldSet == newSet) // check to see if the same sets are equal by reference
                    {
                        added = true; // no retry necessary
                    }
                    else // someone added a different set to this key first
                    {
                        if (oldSet.IsEmpty)
                            retry = true; //IF the set is empty then it has been removed, so signal retry
                        else //Else try to add the value to the set and signal that retry is not needed
                        {
                            added = oldSet.TryAdd(value);
                            retry = false;
                        }
                    }
                }
            } while (retry);
            return added;
        }

        /// <summary>
        /// Find some <typeparam name="TValue"></typeparam> for the first matching value where the supplied
        /// <see cref="predicate"/> returns <c>true</c> for the given key.
        ///  </summary>
        /// <param name="key">The key to use.</param>
        /// <param name="predicate">The predicate to filter values associated with <see cref="key"/>.</param>
        /// <returns>The first <typeparam name="TValue"></typeparam> matching <see cref="predicate"/>. <c>default(TValue)</c> otherwise.</returns>
        public TValue FindValue(TKey key, Func<TValue, bool> predicate)
        {
            ConcurrentSet<TValue> set;
            if (_container.TryGetValue(key, out set))
            {
                return set.FirstOrDefault(predicate);
            }
            else
            {
                return default(TValue);
            }
        }

        public IEnumerable<TValue> this[TKey index]
        {
            get
            {
                ConcurrentSet<TValue> set;
                if (_container.TryGetValue(index, out set))
                {
                    return set;
                }
                return _emptySet;
            }
        } 

        /// <summary>
        /// Applies the supplied <see cref="fun"/> to all keys and their values.
        /// </summary>
        /// <param name="fun">The function to apply.</param>
        public void ForEach(Action<TKey, TValue> fun)
        {
            foreach (var kv in _container)
            {
                foreach (var v in kv.Value)
                    fun(kv.Key, v);
            }
        }

        /// <summary>
        /// Returns the union of all value sets. 
        /// </summary>
        public HashSet<TValue> Values
        {
            get { return new HashSet<TValue>(_container.SelectMany(x => x.Value)); }
        }

        /// <summary>
        /// Returns the key set.
        /// </summary>
        public ICollection<TKey> Keys => _container.Keys;

        /// <summary>
        /// Disassociates the value of <typeparam name="TValue"></typeparam> from
        /// the key of <typeparam name="TKey"></typeparam>.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <returns><c>true</c> if <see cref="value"/> was removed. <c>false</c> otherwise.</returns>
        public bool Remove(TKey key, TValue value)
        {
            ConcurrentSet<TValue> set;
            if (_container.TryGetValue(key, out set))
            {
                if (set.TryRemove(value)) // If we can remove the value
                {
                    if (set.IsEmpty) // and the set becomes empty
                        _container.TryRemove(key, out set);
                    return true; // Remove succeeded
                }
                return false; // Remove failed
            }
            return false; // key not in dictionary. Remove failed
        }

        /// <summary>
        /// Remove the given <see cref="value"/> from all keys.
        /// </summary>
        /// <param name="value">The value we're going to remove, if it exists for any key.</param>
        public void RemoveValue(TValue value)
        {
            var i = _container.Iterator();
            while (!i.IsEmpty())
            {
                var e = i.Next();
                var set = e.Value;
                if (set != null)
                {
                    if (set.TryRemove(value)) // If we can remove the value
                    {
                        if (set.IsEmpty) // And the set becomes empty
                        {
                            // We try to remove the key if it's mapped to an empty set
                            _container.TryRemove(e.Key, out set);
                        }
                    }
                }
            }

        }

        /// <summary>
        /// Disassociates all values for the specified key.
        /// </summary>
        /// <param name="key">The key we're going to remove.</param>
        /// <returns>An enumerable collection of <typeparam name="TValue"></typeparam> if the key exists. An empty collection otherwise.</returns>
        public IEnumerable<TValue> Remove(TKey key)
        {
            ConcurrentSet<TValue> set;
            if (_container.TryRemove(key, out set))
            {
                // grab a shallow copy of the set
                var ret = set.ToArray();
                set.Clear(); // clear the original set to signal to any pending writers there was a conflict
                return ret;
            }
            return _emptySet;
        }

        /// <summary>
        /// Returns <c>true</c> if the index is empty.
        /// </summary>
        public bool IsEmpty => _container.IsEmpty;

        /// <summary>
        /// Removes all keys and values
        /// </summary>
        public void Clear()
        {
            var i = _container.Iterator();
            while (!i.IsEmpty())
            {
                var e = i.Next();
                var set = e.Value;
                if(set != null) { set.Clear(); _container.TryRemove(e.Key, out set); }
            }
        }
    }
}

