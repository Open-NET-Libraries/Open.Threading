
using Open.Disposable;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Open.Threading
{


	[SuppressMessage("ReSharper", "VirtualMemberCallInConstructor")]
	public abstract class ModificationSynchronizedBase : DisposableBase
	{

		private IModificationSynchronizer _sync;
		public IModificationSynchronizer Sync
		{
			get
			{
				var s = _sync;
				this.AssertIsAlive();
				return s;
			}
		}

		public bool IsReadOnly => !(_sync is ModificationSynchronizer);

		protected bool _syncOwned;
		protected virtual ModificationSynchronizer InitSync(object sync = null)
		{
			_syncOwned = true;
			return sync == null
				? new ModificationSynchronizer()
				: sync is ReaderWriterLockSlim slim
					? new ReadWriteModificationSynchronizer(slim)
					: (ModificationSynchronizer)new SimpleLockingModificationSynchronizer();
		}


		protected ModificationSynchronizedBase(ModificationSynchronizer sync = null)
		{
			OnModified();
			SetSync(sync ?? InitSync(), sync != null);
		}


		protected ModificationSynchronizedBase(out ModificationSynchronizer sync)
		{
			OnModified();
			sync = InitSync();
			SetSync(sync, false);
		}

		bool SetSync(IModificationSynchronizer value, bool resetOwnership = true)
		{
			if (resetOwnership)
				_syncOwned = false;
			if (value is ModificationSynchronizer valueM)
				valueM.Modified += OnModified;
			var old = _sync;
			_sync = value;
			if (old is ModificationSynchronizer oldM)
				oldM.Modified -= OnModified;
			return old != value;
		}

		void SetSyncSynced(IModificationSynchronizer value)
		{
			if (_sync is ModificationSynchronizer sync)
			{
				var owned = false;
				// Allow for wrap-up.
				if (!sync.WasDisposed) sync.Modifying(() =>
				 {
					 owned = _syncOwned;
					 SetSync(value);
					 return false; // Prevent triggering a Modified event.
				 });
				if (owned) sync.Dispose();

			}
			else
			{
				SetSync(value);
			}
		}

		protected override void OnDispose()
		{
			SetSyncSynced(null);
		}

		protected void OnModified(object source, EventArgs e)
		{
			OnModified();
		}

		protected virtual void OnModified()
		{

		}

		public void Freeze()
		{
			if (_sync is ModificationSynchronizer sync)
			{
				// Allow for wrap-up.
				sync.Modifying(() =>
				{
					// Swapping out the sync but won't dispose.  Allow GC to care for it.
					if (SetSync(ReadOnlyModificationSynchronizer.Instance))
					{
						OnFrozen();
					}
					return false; // Prevent triggering a Modified event.
				});
			}
		}

		protected virtual void OnFrozen()
		{

		}
	}


}
