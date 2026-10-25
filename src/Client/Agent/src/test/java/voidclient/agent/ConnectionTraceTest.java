package voidclient.agent;

import org.junit.Assert;
import org.junit.Test;

public final class ConnectionTraceTest {
    @Test
    public void remembersTheLastPacketsAConnectionRead() {
        Object connection = new Object();
        Assert.assertEquals("nothing", ConnectionTrace.lastReads(connection));

        for (int index = 0; index < ConnectionTrace.RememberedReads + 2; index++)
            ConnectionTrace.event(ConnectionTrace.Read, connection, index % 2 == 0 ? "text" : Integer.valueOf(index));

        String reads = ConnectionTrace.lastReads(connection);
        Assert.assertTrue(reads, reads.startsWith((ConnectionTrace.RememberedReads + 2) + " packets, the last of them [java.lang.String "));
        Assert.assertEquals(reads, ConnectionTrace.RememberedReads, reads.split(" ms ago").length - 1);
        Assert.assertTrue(reads, reads.contains("java.lang.Integer ") && reads.endsWith(" ms ago]"));
        Assert.assertEquals("nothing", ConnectionTrace.lastReads(new Object()));
    }

    @Test
    public void tellsTheThreadAndChannelOfEachLine() {
        ConnectionOutcomeTest.Network connection = new ConnectionOutcomeTest.Network(false);
        String line = ConnectionTrace.line(connection, "channel inactive");

        Assert.assertTrue(line, line.startsWith("Void client agent connection " + Integer.toHexString(System.identityHashCode(connection)) + " at "));
        Assert.assertTrue(line, line.endsWith(" on " + Thread.currentThread().getName() + " (channel open: false): channel inactive"));
    }

    @Test
    public void tellsWhereACaughtExceptionWasThrown() {
        String stackTrace = ConnectionTrace.stackTrace(new IllegalStateException("Can't serialize unregistered packet"));

        Assert.assertTrue(stackTrace, stackTrace.startsWith("java.lang.IllegalStateException: Can't serialize unregistered packet"));
        Assert.assertTrue(stackTrace, stackTrace.contains("at voidclient.agent.ConnectionTraceTest.tellsWhereACaughtExceptionWasThrown"));
        Assert.assertEquals("not thrown", ConnectionTrace.stackTrace("not thrown"));
    }
}
