
using System;
using System.Threading;
using Open.Disposable;

namespace Open.Threading
{


	public abstract class ModificationSynchronizedBase : DisposableBase
	{

		private IModificationSynchronizer _sync;
		public IModificationSynchronizer Sync
		{
			get
			{
				var s = _sync;
				AssertIsAlive();
				return s;
			}
			private set
			{
				_sync = value;
			}
		}

		public bool IsReadOnly
		{
			get
			{
				// If Sync happens to be null, we also want this to be 'true'.
				return !(_sync is ModificationSynchronizer);
			}
		}

		protected bool _syncOwned;
		protected virtual ModificationSynchronizer InitSync(object sync = null)
		{
			_syncOwned = true;
			return sync == null
				? new ModificationSynchronizer()
				: sync is ReaderWriterLockSlim
					? (ModificationSynchronizer)(new ReadWriteModificationSynchronizer((ReaderWriterLockSlim)sync))
					: (ModificationSynchronizer)(new SimpleLockingModificationSynchronizer());
		}



		public ModificationSynchronizedBase(ModificationSynchronizer sync = null)
		{
			OnModified();
			SetSync(sync ?? InitSync(), sync != null);
		}


		public ModificationSynchronizedBase(out ModificationSynchronizer sync)
		{
			OnModified();
			sync = InitSync();
			SetSync(sync, false);
		}

		bool SetSync(IModificationSynchronizer value, bool resetOwnership = true)
		{
			if (resetOwnership)
				_syncOwned = false;
			var valueM = value as ModificationSynchronizer;
			if (valueM != null)
				valueM.Modified += OnModified;
			var old = _sync;
			_sync = value;
			var oldM = old as ModificationSynchronizer;
			if (oldM != null)
				oldM.Modified -= OnModified;
			return old != value;
		}

		void SetSyncSynced(IModificationSynchronizer value)
		{
			var sync = _sync as ModificationSynchronizer;
			if (sync != null)
			{
				bool owned = false;
				// Allow for wrap-up.
				if (!sync.IsDisposed) sync.Modifying(() =>
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

		protected override void OnDispose(bool calledExplicitly)
		{
			if (calledExplicitly)
				SetSyncSynced(null);
			else
			{
				// var owned = _syncOwned;
				// var sync = _sync as IDisposable;
				SetSync(null);
				// if(sync!=null)
				// {
				// 	sync.Dispose();
				// }
			}
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
			var sync = _sync as ModificationSynchronizer;
			if (sync != null)
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