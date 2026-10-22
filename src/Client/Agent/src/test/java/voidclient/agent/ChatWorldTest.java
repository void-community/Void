package voidclient.agent;

import java.lang.ref.WeakReference;
import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.util.Collections;
import java.util.Map;
import org.junit.After;
import org.junit.Assert;
import org.junit.Before;
import org.junit.Test;

public final class ChatWorldTest {
    private static final String ScreenBase = Screen.class.getName().replace('.', '/');

    @Before
    @After
    public void reset() throws Exception {
        field(GameAutomationController.class, "pending").set(null, null);
        field(GameAutomationController.class, "screenSetterReceiver").set(null, new WeakReference<Object>(null));
        loaderIndexes().clear();
        JoinedConnection.joined(null);
    }

    @Test
    public void chatWaitsForTheWorldOfTheServerTheGameIsOn() throws Exception {
        // A proxy moving the game to another server leaves it without a player until that server's world loads.
        ConnectionOutcomeTest.Network joined = new ConnectionOutcomeTest.Network(true);
        JoinedConnection.joined(joined);
        Object operation = chat();

        advance();
        advance();
        Assert.assertNull(response(operation));

        joined.open = false;
        advance();
        Assert.assertTrue(response(operation), response(operation).contains("\"connection.closed\""));
    }

    @Test
    public void chatFromAGameThatJoinedNoServerFailsAtOnce() throws Exception {
        Object operation = chat();

        advance();

        Assert.assertTrue(response(operation), response(operation).contains("\"world.absent\""));
    }

    @Test
    public void chatStopsWaitingForAWorldThatNeverLoads() throws Exception {
        JoinedConnection.joined(new ConnectionOutcomeTest.Network(true));
        Object operation = chat();

        advance();
        field(operation.getClass(), "worldAbsentSince").set(operation, Long.valueOf(System.currentTimeMillis() - 300000L));
        advance();

        Assert.assertTrue(response(operation), response(operation).contains("\"world.absent\""));
    }

    private static Object chat() throws Exception {
        Class<?> type = Class.forName("voidclient.agent.GameAutomationController$PendingOperation");
        Constructor<?> constructor = type.getDeclaredConstructor(String.class, String.class, String.class);
        constructor.setAccessible(true);
        Object operation = constructor.newInstance("request", "chat", "hello");
        field(GameAutomationController.class, "pending").set(null, operation);
        return operation;
    }

    private static void advance() throws Exception {
        PresentationOverlayPlan overlay = new PresentationOverlayPlan(Client.class.getName().replace('.', '/'), "overlay", "Ljava/lang/Object;");
        AutomationPlan plan = new AutomationPlan(ScreenBase, Client.class.getName().replace('.', '/'), "set", ScreenBase, null, null, null, overlay, null,
            null, null, Collections.<String[]>emptyList());
        loaderIndexes().put(ChatWorldTest.class.getClassLoader(), new GameAutomationIndex.IndexedCode(plan, Collections.<String, String>emptyMap()));

        GameAutomationController.applyClient(new Client());
    }

    private static String response(Object operation) throws Exception {
        return (String) field(operation.getClass(), "response").get(operation);
    }

    @SuppressWarnings("unchecked")
    private static Map<ClassLoader, GameAutomationIndex.IndexedCode> loaderIndexes() throws Exception {
        return (Map<ClassLoader, GameAutomationIndex.IndexedCode>) field(GameAutomationController.class, "LoaderIndexes").get(null);
    }

    private static Field field(Class<?> type, String name) throws Exception {
        Field field = type.getDeclaredField(name);
        field.setAccessible(true);
        return field;
    }

    public static class Screen {
    }

    public static class Client {
        public Screen screen;
        public Object overlay;
    }
}
