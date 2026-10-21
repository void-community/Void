package voidclient.agent;

import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.lang.reflect.InvocationHandler;
import java.lang.reflect.Method;
import java.lang.reflect.Proxy;
import java.util.Collections;
import java.util.Set;
import org.junit.After;
import org.junit.Assert;
import org.junit.Test;

public final class ConnectionOutcomeTest {
    @After
    public void clearOperation() throws Exception {
        field(GameAutomationController.class, "pending").set(null, null);
        JoinedConnection.joined(null);
    }

    @Test
    public void joinedConnectionReportsItsDisconnectReasonOnceTheChannelCloses() throws Exception {
        Network joined = new Network(true);
        JoinedConnection.joined(joined);
        Assert.assertTrue(GameAutomationController.connectionJson().contains("\"connection.open\""));

        // Only the channel decides whether the connection is open; the callback adds the reason.
        GameAutomationController.connectionRejected(new Listener(new Network(false)), "another connection");
        GameAutomationController.connectionRejected(new Listener(joined), "Timed out");
        Assert.assertTrue(GameAutomationController.connectionJson().contains("\"connection.open\""));

        joined.open = false;
        Assert.assertEquals("{\"status\":\"disconnected\",\"stage\":\"connection.closed\",\"message\":\"Timed out\"}", GameAutomationController.connectionJson());
    }

    @Test
    public void disconnectClosesTheChannelOfTheJoinedServer() {
        Network joined = new Network(true);
        JoinedConnection.joined(joined);

        Assert.assertEquals("{\"status\":\"ok\",\"stage\":\"disconnect.requested\"}", GameAutomationController.disconnectJson());
        Assert.assertFalse(joined.open);

        // Once closed, the game has left, so disconnecting again reports why instead.
        Assert.assertEquals(
            "{\"status\":\"disconnected\",\"stage\":\"connection.closed\",\"message\":\"The connection to the server closed\"}",
            GameAutomationController.disconnectJson()
        );
    }

    @Test
    public void disconnectNeedsAJoinedServer() {
        Assert.assertTrue(GameAutomationController.disconnectJson().contains("\"connection.unknown\""));
    }

    @Test
    public void disconnectReasonUsesTheTextOfMinecraftComponents() {
        Object component = new com.mojang.brigadier.Message() {
            @Override
            public String getString() {
                return "Kicked by an operator";
            }

            @Override
            public String toString() {
                return "literal{Kicked by an operator}";
            }
        };

        Assert.assertEquals("Kicked by an operator", JoinedConnection.reasonText(component));
        Assert.assertEquals("Timed out", JoinedConnection.reasonText("Timed out"));
        Assert.assertEquals("null", JoinedConnection.reasonText(null));
    }

    @Test
    public void textIsReadOnlyFromStringsAndComponents() {
        Assert.assertEquals("Unknown host", JoinedConnection.text(message("Unknown host")));
        Assert.assertEquals("Connection refused", JoinedConnection.text("Connection refused"));
        Assert.assertNull(JoinedConnection.text(new Object()));
        Assert.assertNull(JoinedConnection.text(null));
    }

    @Test
    public void failedConnectionScreenTextJoinsTheTitleAndTheReason() {
        Object screen = new Disconnected(message("Failed to connect to the server"), message("Unknown host"));

        Assert.assertEquals("Failed to connect to the server: Unknown host", GameAutomationController.screenText(screen, ScreenBase));
    }

    @Test
    public void screenTextReadsScreensWithoutATitle() {
        // Before 1.14 screens had no title, and before 1.13 text components weren't Brigadier messages.
        Assert.assertEquals("Failed to connect to the server", GameAutomationController.screenText(new LegacyDisconnected(), ScreenBase));
        Assert.assertEquals(Connecting.class.getName(), GameAutomationController.screenText(new Connecting(null), ScreenBase));
        Assert.assertEquals("no screen", GameAutomationController.screenText(null, ScreenBase));
    }

    @Test
    public void onlyTheConnectingScreenHoldsTheConnection() {
        Set<String> networkTypes = Collections.singleton(Network.class.getName().replace('.', '/'));

        Assert.assertTrue(GameAutomationController.holdsConnection(new Connecting(null), ScreenBase, networkTypes));
        Assert.assertFalse(GameAutomationController.holdsConnection(new Disconnected(null, null), ScreenBase, networkTypes));
    }

    @Test
    public void closedChannelWithoutCallbackStillEndsTheConnection() {
        Network joined = new Network(true);
        JoinedConnection.joined(joined);
        joined.open = false;

        Assert.assertEquals("The connection to the server closed", JoinedConnection.closedReason());
    }

    @Test
    public void connectionIsUnknownUntilTheGameJoinsAServer() {
        Assert.assertTrue(GameAutomationController.connectionJson().contains("\"connection.unknown\""));
        Assert.assertNull(JoinedConnection.closedReason());

        Network unconnected = new Network(true);
        unconnected.channel = null;
        Assert.assertNull(JoinedConnection.isOpen(unconnected));
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

    private static com.mojang.brigadier.Message message(final String text) {
        return new com.mojang.brigadier.Message() {
            @Override
            public String getString() {
                return text;
            }
        };
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
        volatile boolean open;
        io.netty.channel.Channel channel = (io.netty.channel.Channel) Proxy.newProxyInstance(
            Network.class.getClassLoader(), new Class<?>[] { io.netty.channel.Channel.class }, new InvocationHandler() {
                @Override
                public Object invoke(Object proxy, Method method, Object[] arguments) {
                    if ("close".equals(method.getName())) {
                        open = false;
                        return null;
                    }

                    if (!"isOpen".equals(method.getName()))
                        throw new UnsupportedOperationException(method.getName());
                    return Boolean.valueOf(open);
                }
            });

        Network() {
        }

        Network(boolean open) {
            this.open = open;
        }
    }
    public static class Listener {
        private final Network connection;
        Listener(Network connection) {
            this.connection = connection;
        }
    }

    private static final String ScreenBase = Screen.class.getName().replace('.', '/');

    public static class Screen {
        private final Object title;
        private int width;

        Screen(Object title) {
            this.title = title;
        }
    }

    public static class Connecting extends Screen {
        private volatile Network connection;

        Connecting(Object title) {
            super(title);
        }
    }

    public static class Disconnected extends Screen {
        private final Screen parent = null;
        private final Object reason;
        private final Object buttonText = message("Back to server list");

        Disconnected(Object title, Object reason) {
            super(title);
            this.reason = reason;
        }
    }

    public static class LegacyScreen {
        private int width;
    }

    public static class LegacyDisconnected extends LegacyScreen {
        private final String reason = "Failed to connect to the server";
        private final Object message = new Object();
        private final LegacyScreen parent = null;
    }
}
