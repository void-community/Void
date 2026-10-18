package voidclient.agent;

import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import org.junit.After;
import org.junit.Assert;
import org.junit.Test;

public final class ConnectionOutcomeTest {
    @After
    public void clearOperation() throws Exception {
        field(GameAutomationController.class, "pending").set(null, null);
    }

    @Test
    public void rejectionIsCorrelatedWithTheNativeConnection() throws Exception {
        Object operation = operation("current");
        Network current = new Network();
        Network previous = new Network();
        GameAutomationController.connectionListenerCreated(new Listener(current), current);
        GameAutomationController.connectionRejected(new Listener(previous), "stale rejection");
        Assert.assertNull(field(operation.getClass(), "rejection").get(operation));
        GameAutomationController.connectionRejected(new Listener(current), "server is full");
        Assert.assertEquals("server is full", field(operation.getClass(), "rejection").get(operation));
        Assert.assertNull(field(operation.getClass(), "response").get(operation));
    }

    @Test
    public void cancellationAndLateCompletionCannotReplaceAnotherAttempt() throws Exception {
        Object previous = operation("previous");
        GameAutomationController.cancel("previous");
        Object current = operation("current");
        Method complete = GameAutomationController.class.getDeclaredMethod("complete", previous.getClass(), String.class);
        complete.setAccessible(true);
        complete.invoke(null, previous, "late success");
        Assert.assertSame(current, field(GameAutomationController.class, "pending").get(null));
        Assert.assertTrue(field(previous.getClass(), "response").get(previous).toString().contains("request.cancelled"));
    }

    @Test
    public void connectBeforeIndexingWaitsUntilCancelled() throws Exception {
        final String[] response = new String[1];
        Thread request = new Thread(new Runnable() {
            @Override
            public void run() {
                response[0] = GameAutomationController.connectJson("early", "server:25565");
            }
        });
        request.start();
        request.join(1000L);
        Assert.assertTrue("connect should wait for the UI index", request.isAlive());
        GameAutomationController.cancel("early");
        request.join(5000L);
        Assert.assertFalse(request.isAlive());
        Assert.assertTrue(response[0].contains("request.cancelled"));
        Assert.assertNull(field(GameAutomationController.class, "pending").get(null));
    }

    private static Object operation(String requestId) throws Exception {
        Class<?> type = Class.forName("voidclient.agent.GameAutomationController$PendingOperation");
        Constructor<?> constructor = type.getDeclaredConstructor(String.class, String.class, String.class);
        constructor.setAccessible(true);
        Object operation = constructor.newInstance(requestId, "connect", "server:25565");
        field(type, "submitted").set(operation, Boolean.TRUE);
        field(GameAutomationController.class, "pending").set(null, operation);
        return operation;
    }

    private static Field field(Class<?> type, String name) throws Exception {
        Field field = type.getDeclaredField(name);
        field.setAccessible(true);
        return field;
    }

    public static class Network extends io.netty.channel.ChannelInboundHandlerAdapter {
    }
    public static class Listener {
        private final Network connection;
        Listener(Network connection) {
            this.connection = connection;
        }
    }
}
