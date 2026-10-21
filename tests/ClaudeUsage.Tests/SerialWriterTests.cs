using ClaudeUsage.Storage;

namespace ClaudeUsage.Tests;

public sealed class SerialWriterTests
{
    [Fact]
    public void Writes_run_in_order_and_a_failing_write_does_not_stop_the_queue()
    {
        var writer = new SerialWriter();
        var order = new List<int>();

        for (int i = 0; i < 50; i++)
        {
            int n = i;
            writer.Enqueue(() => order.Add(n));
        }

        writer.Enqueue(() => throw new InvalidOperationException("boom"));
        writer.Enqueue(() => order.Add(50));

        Assert.True(writer.Flush());
        Assert.Equal(Enumerable.Range(0, 51), order);
    }
}
