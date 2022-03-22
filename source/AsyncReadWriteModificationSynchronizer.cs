using System;
using System.Collections.Generic;
using System.Text;

namespace Open.Threading
{


    // NOTE: Cannot handle recursive actions...
    public sealed class AsyncReadWriteModificationSynchronizer : ModificationSynchronizer
    {

        readonly AsyncReaderWriterLock _sync;
        public AsyncReadWriteModificationSynchronizer()
        {
            _sync = new AsyncReaderWriterLock();
        }


        public override void Reading(Action action)
        {
            AssertIsAlive();
            using (_sync.ReaderLock()) action();
        }

        public override T Reading<T>(Func<T> action)
        {
            AssertIsAlive();
            using (_sync.ReaderLock()) return action();
        }

        public override bool Modifying(Func<bool> condition, Func<bool> action)
        {
            AssertIsAlive();

            // Try and early invalidate.
            if (condition != null)
            {
                using (_sync.ReaderLock()) if (!condition()) return false;
            }

            bool modified = false;
            using (var upgradableLock = _sync.UpgradeableReaderLock())
            {
                AssertIsAlive();
                if (condition == null || condition())
                {
                    using (upgradableLock.Upgrade())
                    {
                        modified = base.Modifying(null, action);
                    }
                }
            }
            return modified;
        }


        public override bool Modifying<T>(ref T target, T newValue)
        {
            AssertIsAlive();
            if (target.Equals(newValue)) return false;

            bool changed;

            // Note, there's no need for _modifyingDepth recursion tracking here.
            using (var upgradableLock = _sync.UpgradeableReaderLock())
            {
                var ver = _version; // Capture the version so that if changes occur indirectly...
                changed = !target.Equals(newValue);

                if (changed)
                {
                    using (upgradableLock.Upgrade())
                    {
                        IncrementVersion();
                        target = newValue;
                    }

                    // Events will be triggered but this thread will still have the upgradable read.
                    SignalModified();
                }
            }

            return changed;
        }
    }
