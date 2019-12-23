#region Using Directives
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TrinanceLib;
#endregion

namespace TrinanceApp
{
    public sealed class ConsolePump : MessagePump
    {
        #region Members
        private readonly BlockingCollection<MessageCollection> m_Queue;
        private readonly Task m_Task;
        #endregion

        #region Constructors
        public ConsolePump()
        {
            m_Queue = new BlockingCollection<MessageCollection>(new ConcurrentQueue<MessageCollection>());

            m_Task = Task.Factory.StartNew(() =>
            {
                foreach (MessageCollection message in m_Queue.GetConsumingEnumerable())
                {
                    if (message.Block)
                        Console.WriteLine();

                    foreach (String text in message)
                        Console.WriteLine(text);

                    if (message.Block)
                        Console.WriteLine();
                }
            },
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        #endregion

        #region Methods
        protected override void ReleaseManagedResources()
        {
            if (m_Queue == null)
                return;

            m_Queue.CompleteAdding();

            m_Task.Wait();
            m_Task.Dispose();

            m_Queue.Dispose();

            base.ReleaseManagedResources();
        }

        public override void Signal(MessageCollection message)
        {
            if (message != null)
                m_Queue.Add(message);
        }

        public override void Signal(String text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            m_Queue.Add(new MessageCollection(text.Trim()));
        }
        #endregion
    }
}
