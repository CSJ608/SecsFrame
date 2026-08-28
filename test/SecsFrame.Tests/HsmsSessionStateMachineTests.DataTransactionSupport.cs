namespace SecsFrame.Tests;

public sealed partial class HsmsSessionStateMachineTests
{
    private static (
        HsmsSessionStateMachine Session,
        HsmsDataTransactionManager Transactions) CreateTransactions(
            FakeHsmsTransport transport,
            ManualTimerFactory t3Timers,
            IHsmsSystemBytesProvider? systemBytesProvider = null)
    {
        var session = CreateMachine(
            transport,
            HsmsConnectionMode.Passive,
            new ManualTimerFactory());
        var transactions = new HsmsDataTransactionManager(
            session,
            new HsmsDataTransactionOptions(T3),
            timerFactory: t3Timers,
            systemBytesProvider:
                systemBytesProvider ??
                new SequenceSystemBytesProvider(1, 2, 3, 4));
        return (session, transactions);
    }

    private static async Task SelectTransactionsPassiveAsync(
        FakeHsmsTransport transport,
        HsmsSessionStateMachine session,
        IAsyncEnumerator<HsmsDataTransactionEvent> events)
    {
        await SelectPassiveAsync(transport, session).ConfigureAwait(true);
        _ = await NextMatchingTransactionEventAsync(
            events,
            transactionEvent =>
                transactionEvent.Kind ==
                    HsmsDataTransactionEventKind.StateChanged &&
                transactionEvent.State == HsmsSessionState.Selected)
            .ConfigureAwait(true);
    }

    private static async Task<HsmsDataTransactionEvent>
        NextMatchingTransactionEventAsync(
            IAsyncEnumerator<HsmsDataTransactionEvent> events,
            Func<HsmsDataTransactionEvent, bool> predicate)
    {
        while (await events.MoveNextAsync().ConfigureAwait(true))
        {
            if (predicate(events.Current))
                return events.Current;
        }

        Assert.Fail("The expected HSMS data transaction event was not received.");
        return default;
    }

    private static void AssertDataMessageEqual(
        HsmsDataMessage expected,
        HsmsDataMessage? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.SessionId, actual.SessionId);
        Assert.Equal(expected.SystemBytes, actual.SystemBytes);
        Assert.Equal(expected.Message.Stream, actual.Message.Stream);
        Assert.Equal(expected.Message.Function, actual.Message.Function);
        Assert.Equal(
            expected.Message.ReplyExpected,
            actual.Message.ReplyExpected);
        Assert.Equal(expected.Message.RootItem, actual.Message.RootItem);
    }

    private sealed class SequenceSystemBytesProvider : IHsmsSystemBytesProvider
    {
        private readonly uint[] _values;
        private int _index = -1;

        public SequenceSystemBytesProvider(params uint[] values)
        {
            if (values is null)
                throw new ArgumentNullException(nameof(values));
            if (values.Length == 0)
                throw new ArgumentException(
                    "At least one System Bytes value is required.",
                    nameof(values));

            _values = values;
        }

        public uint Next()
        {
            var index = Interlocked.Increment(ref _index);
            if ((uint)index >= (uint)_values.Length)
            {
                throw new InvalidOperationException(
                    "The test System Bytes sequence was exhausted.");
            }

            return _values[index];
        }
    }
}
