#region Using Directives
using System;
using System.Collections;
using System.Collections.Generic;
#endregion

namespace TrinanceLib
{
    public sealed class MessageCollection : IEnumerable<String>
    {
        #region Members
        private readonly Boolean m_Block;
        private readonly List<String> m_Messages;
        #endregion

        #region Properties
        public Boolean Block => m_Block;
        #endregion

        #region Constructors
        public MessageCollection(Boolean block, String initialMessage)
        {
            m_Block = block;
            m_Messages = new List<String> { initialMessage };
        }

        public MessageCollection(String initialMessage) : this(false, initialMessage) { }
        #endregion

        #region Methods
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<String> GetEnumerator()
        {
            return m_Messages.GetEnumerator();
        }

        public void AppendMessage(String message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            m_Messages.Add(message.Trim());
        }
        #endregion
    }

    public abstract class MessagePump : DisposableBase
    {
        #region Methods (Abstract)
        public abstract void Signal(MessageCollection message);
        public abstract void Signal(String message);
        #endregion
    }
}
