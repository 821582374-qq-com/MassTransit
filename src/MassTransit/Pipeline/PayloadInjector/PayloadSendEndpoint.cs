namespace MassTransit.Pipeline.PayloadInjector
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Context.Converters;
    using GreenPipes;
    using GreenPipes.Util;
    using Initializers;


    public class PayloadSendEndpoint<TPayload> :
        ISendEndpoint
        where TPayload : class
    {
        readonly ISendEndpoint _endpoint;
        readonly TPayload _payload;

        public PayloadSendEndpoint(ISendEndpoint endpoint, TPayload payload)
        {
            _endpoint = endpoint;
            _payload = payload;
        }

        ConnectHandle ISendObserverConnector.ConnectSendObserver(ISendObserver observer)
        {
            return _endpoint.ConnectSendObserver(observer);
        }

        Task ISendEndpoint.Send<T>(T message, CancellationToken cancellationToken)
        {
            return _endpoint.Send(message, new PayloadPipe<T>(_payload), cancellationToken);
        }

        Task ISendEndpoint.Send<T>(T message, IPipe<SendContext<T>> pipe, CancellationToken cancellationToken)
        {
            return _endpoint.Send(message, new PayloadPipe<T>(_payload, pipe), cancellationToken);
        }

        Task ISendEndpoint.Send<T>(T message, IPipe<SendContext> pipe, CancellationToken cancellationToken)
        {
            return _endpoint.Send(message, new PayloadPipe<T>(_payload, pipe), cancellationToken);
        }

        public Task Send(object message, CancellationToken cancellationToken)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            var messageType = message.GetType();

            return SendEndpointConverterCache.Send(this, message, messageType, cancellationToken);
        }

        public Task Send(object message, Type messageType, CancellationToken cancellationToken)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            if (messageType == null)
                throw new ArgumentNullException(nameof(messageType));

            return SendEndpointConverterCache.Send(this, message, messageType, cancellationToken);
        }

        public Task Send(object message, IPipe<SendContext> pipe, CancellationToken cancellationToken)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            if (pipe == null)
                throw new ArgumentNullException(nameof(pipe));

            var messageType = message.GetType();

            return SendEndpointConverterCache.Send(this, message, messageType, pipe, cancellationToken);
        }

        public Task Send(object message, Type messageType, IPipe<SendContext> pipe, CancellationToken cancellationToken)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            if (messageType == null)
                throw new ArgumentNullException(nameof(messageType));
            if (pipe == null)
                throw new ArgumentNullException(nameof(pipe));

            return SendEndpointConverterCache.Send(this, message, messageType, pipe, cancellationToken);
        }

        public Task Send<T>(object values, CancellationToken cancellationToken)
            where T : class
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            var initializer = MessageInitializerCache<T>.GetInitializer(values.GetType());

            return initializer.Send(this, values, cancellationToken);
        }

        public Task Send<T>(object values, IPipe<SendContext<T>> pipe, CancellationToken cancellationToken)
            where T : class
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (pipe == null)
                throw new ArgumentNullException(nameof(pipe));

            var initializer = MessageInitializerCache<T>.GetInitializer(values.GetType());

            return initializer.Send(this, values, pipe, cancellationToken);
        }

        public Task Send<T>(object values, IPipe<SendContext> pipe, CancellationToken cancellationToken)
            where T : class
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (pipe == null)
                throw new ArgumentNullException(nameof(pipe));

            var initializer = MessageInitializerCache<T>.GetInitializer(values.GetType());

            return initializer.Send(this, values, pipe, cancellationToken);
        }


        class PayloadPipe<TMessage> :
            IPipe<SendContext<TMessage>>,
            ISendContextPipe
            where TMessage : class
        {
            readonly TPayload _payload;
            readonly IPipe<SendContext<TMessage>> _pipe;

            public PayloadPipe(TPayload payload, IPipe<SendContext<TMessage>> pipe = default)
            {
                _payload = payload;
                _pipe = pipe;
            }

            Task ISendContextPipe.Send<T>(SendContext<T> context)
                where T : class
            {
                context.GetOrAddPayload(() => _payload);

                return _pipe is ISendContextPipe sendContextPipe
                    ? sendContextPipe.Send(context)
                    : TaskUtil.Completed;
            }

            public Task Send(SendContext<TMessage> context)
            {
                return _pipe.IsNotEmpty() ? _pipe.Send(context) : Task.CompletedTask;
            }

            public void Probe(ProbeContext context)
            {
                _pipe?.Probe(context);
            }
        }
    }
}
