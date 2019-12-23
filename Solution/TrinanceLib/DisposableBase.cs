#region Using Directives
using System;
using System.Diagnostics.CodeAnalysis;

#endregion

namespace TrinanceLib
{
    [SuppressMessage("ReSharper", "VirtualMemberNeverOverridden.Global")]
    public abstract class DisposableBase : IDisposable
    {
        #region Members
        private Boolean m_Disposed;
        #endregion

        #region Destructors
        ~DisposableBase()
        {
            Dispose(false);
        }
        #endregion

        #region Methods
        private void Dispose(Boolean disposing)
        {
            if (m_Disposed)
                return;

            if (disposing)
                ReleaseManagedResources();

            ReleaseUnmanagedResources();

            m_Disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        #region Methods (Virtual)
        protected virtual void ReleaseManagedResources() { }
        protected virtual void ReleaseUnmanagedResources() { }
        #endregion
    }
}
