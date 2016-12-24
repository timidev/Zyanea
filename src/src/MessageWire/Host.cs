﻿using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace MessageWire
{
    public class Host : IDisposable
    {
        private readonly string _connectionString;
        private RouterSocket _routerSocket = null;
        private NetMQPoller _socketPoller = null;
        private NetMQPoller _hostPoller = null;
        private readonly NetMQQueue<NetMQMessage> _sendQueue;
        private readonly NetMQQueue<Message> _receivedQueue;
        private readonly NetMQQueue<MessageFailure> _sendFailureQueue;

        /// <summary>
        /// Host constructor.
        /// </summary>
        /// <param name="connectionString">Valid NetMQ server socket connection string.</param>
        public Host(string connectionString)
        {
            _connectionString = connectionString;
            _sendQueue = new NetMQQueue<NetMQMessage>();

            _routerSocket = new RouterSocket(_connectionString);
            _routerSocket.Options.RouterMandatory = true;
            _sendQueue.ReceiveReady += _sendQueue_ReceiveReady;
            _routerSocket.ReceiveReady += _socket_ReceiveReady;
            _socketPoller = new NetMQPoller { _routerSocket, _sendQueue };
            _socketPoller.RunAsync();

            _sendFailureQueue = new NetMQQueue<MessageFailure>();
            _receivedQueue = new NetMQQueue<Message>();
            _sendFailureQueue.ReceiveReady += _sendFailureQueue_ReceiveReady;
            _receivedQueue.ReceiveReady += _receivedQueue_ReceiveReady;
            _hostPoller = new NetMQPoller { _receivedQueue, _sendFailureQueue };
            _hostPoller.RunAsync();
        }

        private EventHandler<MessageEventFailureArgs> _sentFailureEvent;
        private EventHandler<MessageEventArgs> _receivedEvent;
        private EventHandler<MessageEventArgs> _sentEvent;

        /// <summary>
        /// This event occurs when a message has been received. 
        /// </summary>
        /// <remarks>This handler will run on a different thread than the socket poller and
        /// blocking on this thread will not block sending and receiving.</remarks>
        public event EventHandler<MessageEventArgs> MessageReceived {
            add {
                _receivedEvent += value;
            }
            remove {
                _receivedEvent -= value;
            }
        }

        /// <summary>
        /// This event occurs when a message failed to send because the client is no longer connected.
        /// </summary>
        /// <remarks>This handler will run on a different thread than the socket poller and
        /// blocking on this thread will not block sending and receiving.</remarks>
        public event EventHandler<MessageEventFailureArgs> MessageSentFailure {
            add {
                _sentFailureEvent += value;
            }
            remove {
                _sentFailureEvent -= value;
            }
        }

        public void Send(Guid clientId, List<byte[]> frames)
        {
            if (_disposed) throw new ObjectDisposedException("Client", "Cannot send on disposed client.");
            if (null == frames || frames.Count == 0)
            {
                throw new ArgumentException("Cannot be null or empty.", nameof(frames));
            }
            var message = new NetMQMessage();
            message.Append(clientId.ToByteArray());
            message.AppendEmptyFrame();
            foreach (var frame in frames) message.Append(frame);
            _sendQueue.Enqueue(message); //send by message to socket poller
        }

        //occurs on socket polling thread to assure sending and receiving on same thread
        private void _sendQueue_ReceiveReady(object sender, NetMQQueueEventArgs<NetMQMessage> e)
        {
            NetMQMessage message;
            if (e.Queue.TryDequeue(out message, new TimeSpan(1000)))
            {
                try
                {
                    _routerSocket.SendMultipartMessage(message);
                }
                catch (HostUnreachableException ex) //clientId not found or other error, raise event
                {
                    //send by message to host poller
                    _sendFailureQueue.Enqueue(new MessageFailure
                    {
                        Message = message.ToMessageWithClientFrame(),
                        ErrorCode = ex.ErrorCode.ToString(),
                        ErrorMessage = ex.Message
                    });
                }
            }
        }

        //occurs on socket polling thread to assure sending and receiving on same thread
        private void _socket_ReceiveReady(object sender, NetMQSocketEventArgs e)
        {
            var msg = e.Socket.ReceiveMultipartMessage();
            var message = msg.ToMessageWithClientFrame();
            _receivedQueue.Enqueue(message); //sends by message to host poller
        }

        //occurs on host polling thread to allow sending and receiving on a different thread
        private void _sendFailureQueue_ReceiveReady(object sender, NetMQQueueEventArgs<MessageFailure> e)
        {
            MessageFailure mf;
            if (e.Queue.TryDequeue(out mf, new TimeSpan(1000)))
            {
                _sentFailureEvent?.Invoke(this, new MessageEventFailureArgs
                {
                    Failure = mf
                });
            }
        }

        //occurs on host polling thread to allow sending and receiving on a different thread
        private void _receivedQueue_ReceiveReady(object sender, NetMQQueueEventArgs<Message> e)
        {
            Message message;
            if (e.Queue.TryDequeue(out message, new TimeSpan(1000)))
            {
                _receivedEvent?.Invoke(this, new MessageEventArgs
                {
                    Message = message
                });
            }
        }

        #region IDisposable Members

        private bool _disposed = false;

        public void Dispose()
        {
            //MS recommended dispose pattern - prevents GC from disposing again
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true; //prevent second call to Dispose
                if (disposing)
                {
                    if (null != _socketPoller) _socketPoller.Dispose();
                    if (null != _sendQueue) _sendQueue.Dispose();
                    if (null != _routerSocket) _routerSocket.Dispose();

                    if (null != _hostPoller) _hostPoller.Dispose();
                    if (null != _receivedQueue) _receivedQueue.Dispose();
                    if (null != _sendFailureQueue) _sendFailureQueue.Dispose();
                }
            }
        }

        #endregion
    }
}