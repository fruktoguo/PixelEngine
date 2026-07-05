using System.Buffers;
using PixelEngine.Core.Events;
using CoreEventBus = PixelEngine.Core.Events.EventBus;

namespace PixelEngine.Scripting;

internal interface IScriptEventDispatcher
{
    void DrainEvents();
}

/// <summary>
/// 将 Core 事件 ring buffer 适配为脚本相位 1 订阅分发 API。
/// </summary>
/// <param name="coreEvents">Core 事件总线。</param>
public sealed class ScriptEventBus(CoreEventBus coreEvents) : IEventBus, IScriptEventDispatcher, IDisposable
{
    private readonly CoreEventBus _coreEvents = coreEvents ?? throw new ArgumentNullException(nameof(coreEvents));
    private readonly Dictionary<Type, IEventChannel> _channels = [];
    private bool _disposed;

    /// <summary>
    /// 订阅指定 unmanaged 事件类型；脚本可在相位 1 调用，事件会在后续相位 1 drain 时分发。
    /// </summary>
    /// <typeparam name="TEvent">要订阅的 unmanaged 事件载荷类型。</typeparam>
    /// <param name="handler">事件处理器；由运行时在相位 1 调用。</param>
    /// <returns>用于取消订阅的释放句柄。</returns>
    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : unmanaged
    {
        ArgumentNullException.ThrowIfNull(handler);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EventChannel<TEvent> channel = GetOrCreateChannel<TEvent>();
        return channel.Subscribe(handler);
    }

    /// <inheritdoc />
    public bool TryPublish<TEvent>(in TEvent item)
        where TEvent : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _coreEvents.Channel<TEvent>().TryEnqueue(in item);
    }

    /// <summary>
    /// 排空所有已订阅事件通道；由脚本运行时在相位 1 调用。
    /// </summary>
    public void DrainEvents()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (IEventChannel channel in _channels.Values)
        {
            channel.Drain();
        }
    }

    /// <summary>
    /// 释放订阅表与事件暂存缓冲。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (IEventChannel channel in _channels.Values)
        {
            channel.Dispose();
        }

        _channels.Clear();
        _disposed = true;
    }

    private EventChannel<TEvent> GetOrCreateChannel<TEvent>()
        where TEvent : unmanaged
    {
        Type type = typeof(TEvent);
        if (_channels.TryGetValue(type, out IEventChannel? channel))
        {
            return (EventChannel<TEvent>)channel;
        }

        EventChannel<TEvent> created = new(_coreEvents.Channel<TEvent>(), _coreEvents.CapacityPerChannel);
        _channels.Add(type, created);
        return created;
    }

    private interface IEventChannel : IDisposable
    {
        void Drain();
    }

    private sealed class EventChannel<TEvent>(MpscRingBuffer<TEvent> ring, int capacity) : IEventChannel
        where TEvent : unmanaged
    {
        private readonly MpscRingBuffer<TEvent> _ring = ring;
        private readonly List<HandlerEntry?> _handlers = [];
        private readonly Stack<int> _freeSlots = new();
        private TEvent[] _buffer = ArrayPool<TEvent>.Shared.Rent(capacity);

        public IDisposable Subscribe(Action<TEvent> handler)
        {
            HandlerEntry entry = ScriptInvoker.TryGetCurrentOwner(out Behaviour owner, out ScriptInvoker invoker)
                ? new HandlerEntry(handler, owner, invoker)
                : new HandlerEntry(handler, null, null);
            int index;
            if (_freeSlots.Count == 0)
            {
                index = _handlers.Count;
                _handlers.Add(entry);
            }
            else
            {
                index = _freeSlots.Pop();
                _handlers[index] = entry;
            }

            Subscription subscription = new(this, index);
            entry.Owner?.TrackSubscription(subscription);
            return subscription;
        }

        public void Drain()
        {
            int drained;
            do
            {
                drained = _ring.DrainTo(_buffer);
                for (int eventIndex = 0; eventIndex < drained; eventIndex++)
                {
                    TEvent item = _buffer[eventIndex];
                    for (int handlerIndex = 0; handlerIndex < _handlers.Count; handlerIndex++)
                    {
                        _handlers[handlerIndex]?.Invoke(item);
                    }
                }
            }
            while (drained == _buffer.Length);
        }

        public void Dispose()
        {
            TEvent[] buffer = _buffer;
            _buffer = [];
            _handlers.Clear();
            _freeSlots.Clear();
            ArrayPool<TEvent>.Shared.Return(buffer, clearArray: true);
        }

        private void Unsubscribe(int index)
        {
            if ((uint)index >= (uint)_handlers.Count || _handlers[index] is null)
            {
                return;
            }

            _handlers[index] = null;
            _freeSlots.Push(index);
        }

        private sealed class HandlerEntry(Action<TEvent> handler, Behaviour? owner, ScriptInvoker? invoker)
        {
            public Behaviour? Owner { get; } = owner;

            public void Invoke(TEvent item)
            {
                if (Owner is not null && invoker is not null)
                {
                    invoker.InvokeEvent(Owner, handler, item);
                    return;
                }

                handler(item);
            }
        }

        private sealed class Subscription(EventChannel<TEvent> owner, int index) : IDisposable
        {
            private EventChannel<TEvent>? _owner = owner;

            public void Dispose()
            {
                EventChannel<TEvent>? owner = _owner;
                if (owner is null)
                {
                    return;
                }

                owner.Unsubscribe(index);
                _owner = null;
            }
        }
    }
}
